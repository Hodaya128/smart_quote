using System.Collections.Concurrent;

namespace comviaServer.Model;

// תור בקשות *חיפוש* עבור תוסף הדפדפן. בניגוד ל-FarnellJobQueue (שמחזיר מחיר בודד למוצר
// ידוע), כאן מדובר בחיפוש מק"ט שמחזיר *טבלת תוצאות* (כמה ספקים, מלאי, מחירים, קישורים).
// הרעיון זהה: NetComponents חוסם/דורש סשן מחובר, לכן השרת לא שולף בעצמו אלא מכניס job
// לתור וממתין; תוסף Chrome שרץ בדפדפן המחובר של המשתמש מושך jobs, פותח את עמוד החיפוש,
// קורא את ה-DOM ומחזיר את השורות. in-memory בלבד (מתאים ל-pilot).
// מתוכנן גנרי לפי שם ספק (Supplier) כדי שנוכל להרחיב לאתרים נוספים בהמשך בלי לשנות מבנה.
public class BrowserSearchQueue
{
    // job כפי שנשלח לתוסף.
    public class SearchJob
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Supplier { get; init; } = "";  // "NetComponents" (ובהמשך ספקים נוספים)
        public string Sku { get; init; } = "";
        public string Url { get; init; } = "";        // עמוד החיפוש שצריך לפתוח בסשן המשתמש
        public int Qty { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    private class PendingJob
    {
        public SearchJob Job { get; init; } = null!;
        public TaskCompletionSource<List<ExtensionSupplierRow>?> Tcs { get; init; } = null!;
        public bool Dispatched { get; set; }
    }

    private readonly ConcurrentDictionary<string, PendingJob> _jobs = new();
    private readonly object _dispatchLock = new();
    private readonly ILogger<BrowserSearchQueue> _logger;

    // איתות long-poll: מוחלף בכל הכנסת job חדש כדי להעיר את התוסף שממתין על /jobs.
    private TaskCompletionSource<bool> _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // זמן ה-poll האחרון של התוסף. תוסף מחובר עושה poll ברציפות (long-poll קצר ומיד poll חדש),
    // כך שאם לא נראה poll ב-45 השניות האחרונות — התוסף לא מחובר לשרת הזה, ואין טעם להמתין
    // ל-timeout המלא (בשרת רופין ההמתנה הארוכה אף גרמה ל-504 מהפרוקסי).
    private DateTime _lastPollUtc = DateTime.MinValue;

    public bool IsExtensionOnline => DateTime.UtcNow - _lastPollUtc < TimeSpan.FromSeconds(45);

    public BrowserSearchQueue(ILogger<BrowserSearchQueue> logger) => _logger = logger;

    // ===== צד השרת =====

    // מכניס job של חיפוש וממתין לתוצאה עד timeout. מחזיר null אם פג הזמן (התוסף לא זמין/
    // איטי או שהמשתמש לא מחובר). null => הקורא יתייחס כ"אין תוצאות NC".
    public async Task<List<ExtensionSupplierRow>?> RequestSearchAsync(string supplier, string sku, string url, int qty, TimeSpan timeout)
    {
        // תוסף לא מחובר — מדלגים מיד במקום להמתין ל-timeout מלא לכל אתר.
        if (!IsExtensionOnline)
        {
            _logger.LogInformation("Browser search skipped (extension offline): {Supplier} '{Sku}'", supplier, sku);
            return null;
        }

        var pending = new PendingJob
        {
            Job = new SearchJob { Supplier = supplier, Sku = sku, Url = url, Qty = qty },
            Tcs = new TaskCompletionSource<List<ExtensionSupplierRow>?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        _jobs[pending.Job.Id] = pending;
        _logger.LogInformation("Browser search job {Id} enqueued: {Supplier} '{Sku}'", pending.Job.Id, supplier, sku);

        // מעיר long-poll ממתין שיש job חדש.
        var old = Interlocked.Exchange(ref _signal, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult(true);

        try
        {
            return await pending.Tcs.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Browser search job {Id} timed out after {Sec}s (extension offline/slow or user not logged in?)",
                pending.Job.Id, timeout.TotalSeconds);
            return null;
        }
        finally
        {
            _jobs.TryRemove(pending.Job.Id, out _);
        }
    }

    // ===== צד התוסף =====

    // מושך jobs שטרם נשלחו (ומסמן dispatched). אם אין — long-poll עד job חדש או timeout.
    public async Task<List<SearchJob>> DequeuePendingAsync(int max, TimeSpan wait)
    {
        _lastPollUtc = DateTime.UtcNow;

        var jobs = DequeuePending(max);
        if (jobs.Count > 0) return jobs;

        var signal = Volatile.Read(ref _signal);
        try { await signal.Task.WaitAsync(wait); }
        catch (TimeoutException) { return new List<SearchJob>(); }

        return DequeuePending(max);
    }

    private List<SearchJob> DequeuePending(int max)
    {
        var result = new List<SearchJob>();
        lock (_dispatchLock)
        {
            foreach (var kv in _jobs)
            {
                if (result.Count >= max) break;
                if (!kv.Value.Dispatched)
                {
                    kv.Value.Dispatched = true;
                    result.Add(kv.Value.Job);
                }
            }
        }
        return result;
    }

    // מחזיר תוצאה ל-job. rows=null => לא נמצאו תוצאות / המשתמש לא מחובר. מחזיר false אם
    // ה-job כבר לא קיים (פג ב-timeout בינתיים).
    public bool SetResult(string id, List<ExtensionSupplierRow>? rows)
    {
        if (_jobs.TryGetValue(id, out var pending))
            return pending.Tcs.TrySetResult(rows);
        return false;
    }
}
