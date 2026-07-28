using System.Collections.Concurrent;

namespace comviaServer.Model;

// תור בקשות מחיר עבור תוסף הדפדפן (Farnell, ובהמשך ספקים נוספים שדורשים סשן משתמש).
// הרעיון: Farnell חוסם Selenium מהשרת וגם דורש מחיר חוזה אישי (רק כשמחוברים לחשבון COMVIA).
// לכן השרת לא שולף בעצמו — הוא מכניס "job" לתור וממתין; תוסף Chrome שרץ בדפדפן המחובר
// של המשתמש מושך jobs, שולף את המחיר מהסשן האמיתי, ומחזיר תוצאה.
// in-memory בלבד — מתאים ל"משתמשים מעטים" (ה-pilot). אם נרצה ריבוי שרתים/persistence נחליף ל-DB.
public class FarnellJobQueue
{
    // job כפי שנשלח לתוסף.
    public class PriceJob
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Supplier { get; init; } = "";
        public string Url { get; init; } = "";
        public int Qty { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    // תוצאת מחיר מהתוסף — כולל המטבע שזוהה בדף (il.farnell.com מציג מחיר שאינו בהכרח USD).
    // השרת ימיר ל-USD לפי המטבע הזה.
    public record FarnellResult(double Price, string? Currency);

    private class PendingJob
    {
        public PriceJob Job { get; init; } = null!;
        public TaskCompletionSource<FarnellResult?> Tcs { get; init; } = null!;
        public bool Dispatched { get; set; }
    }

    private readonly ConcurrentDictionary<string, PendingJob> _jobs = new();
    private readonly object _dispatchLock = new();
    private readonly ILogger<FarnellJobQueue> _logger;

    // איתות long-poll: מוחלף בכל הכנסת job חדש כדי להעיר את התוסף שממתין על /jobs.
    private TaskCompletionSource<bool> _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // זמן ה-poll האחרון של התוסף — תוסף מחובר עושה poll ברציפות, כך שהיעדר poll
    // ב-45 שניות אחרונות = תוסף לא מחובר, ואז מדלגים מיד בלי להמתין ל-timeout.
    private DateTime _lastPollUtc = DateTime.MinValue;

    public bool IsExtensionOnline => DateTime.UtcNow - _lastPollUtc < TimeSpan.FromSeconds(45);

    public FarnellJobQueue(ILogger<FarnellJobQueue> logger) => _logger = logger;

    // ===== צד השרת =====

    // מכניס job וממתין לתוצאה עד timeout. מחזיר null אם פג הזמן (התוסף לא זמין/איטי)
    // או אם התוסף החזיר "אין מחיר". הקורא מתייחס ל-null כ"להשאיר את מחיר ה-NC".
    public async Task<FarnellResult?> RequestPriceAsync(string supplier, string url, int qty, TimeSpan timeout)
    {
        // תוסף לא מחובר — אין טעם להמתין; נשארים עם מחיר ה-NC הקיים.
        if (!IsExtensionOnline)
        {
            _logger.LogInformation("Farnell job skipped (extension offline): {Url}", url);
            return null;
        }

        var pending = new PendingJob
        {
            Job = new PriceJob { Supplier = supplier, Url = url, Qty = qty },
            Tcs = new TaskCompletionSource<FarnellResult?>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        _jobs[pending.Job.Id] = pending;
        _logger.LogInformation("Farnell job {Id} enqueued: {Url} (qty {Qty})", pending.Job.Id, url, qty);

        // מעיר long-poll ממתין שיש job חדש.
        var old = Interlocked.Exchange(ref _signal, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        old.TrySetResult(true);

        try
        {
            return await pending.Tcs.Task.WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Farnell job {Id} timed out after {Sec}s (extension offline/slow or user not logged in?)",
                pending.Job.Id, timeout.TotalSeconds);
            return null;
        }
        finally
        {
            _jobs.TryRemove(pending.Job.Id, out _);
        }
    }

    // ===== צד התוסף =====

    // מושך jobs שטרם נשלחו (ומסמן אותם dispatched כדי לא לשלוח פעמיים). אם אין —
    // ממתין (long-poll) עד שמגיע job חדש או עד timeout, ואז בודק שוב.
    public async Task<List<PriceJob>> DequeuePendingAsync(int max, TimeSpan wait)
    {
        _lastPollUtc = DateTime.UtcNow;

        var jobs = DequeuePending(max);
        if (jobs.Count > 0) return jobs;

        var signal = Volatile.Read(ref _signal);
        try { await signal.Task.WaitAsync(wait); }
        catch (TimeoutException) { return new List<PriceJob>(); }

        return DequeuePending(max);
    }

    private List<PriceJob> DequeuePending(int max)
    {
        var result = new List<PriceJob>();
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

    // מחזיר תוצאה ל-job. price=null => לא נמצא מחיר / המשתמש לא מחובר. currency = המטבע
    // שזוהה בדף (להמרה ל-USD בצד השרת). מחזיר false אם ה-job כבר לא קיים (פג ב-timeout).
    public bool SetResult(string id, double? price, string? currency = null)
    {
        if (_jobs.TryGetValue(id, out var pending))
            return pending.Tcs.TrySetResult(price.HasValue ? new FarnellResult(price.Value, currency) : null);
        return false;
    }
}
