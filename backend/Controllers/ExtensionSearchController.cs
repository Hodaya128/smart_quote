using Microsoft.AspNetCore.Mvc;
using comviaServer.Model;

namespace comviaServer.Controllers;

// נקודות הקצה של תוסף ה-Chrome עבור *חיפושי* ספקים (NetComponents כרגע, ובהמשך נוספים).
// במקביל ל-FarnellController (מחיר בודד), כאן מדובר בחיפוש מק"ט שמחזיר טבלת תוצאות.
// GET  /api/extension/search/jobs         — התוסף מושך בקשות חיפוש ממתינות (long-poll).
// POST /api/extension/search/jobs/result  — התוסף מחזיר את שורות התוצאה שחילץ מה-DOM.
// אימות זהה ל-Farnell (אותו token משותף, Header: X-Farnell-Token) כדי שהתוסף ישתמש ב-token אחד.
[ApiController]
[Route("api/extension/search")]
public class ExtensionSearchController : ControllerBase
{
    private readonly BrowserSearchQueue _queue;
    private readonly IConfiguration _config;
    private readonly ILogger<ExtensionSearchController> _logger;

    public ExtensionSearchController(BrowserSearchQueue queue, IConfiguration config, ILogger<ExtensionSearchController> logger)
    {
        _queue = queue;
        _config = config;
        _logger = logger;
    }

    // אותו token של Farnell — תוסף אחד, token אחד. אם לא הוגדר — פתוח (נוח לפיתוח).
    private bool Authorized()
    {
        var expected = _config["Farnell:ExtensionToken"];
        if (string.IsNullOrEmpty(expected)) return true;
        var got = Request.Headers["X-Farnell-Token"].FirstOrDefault();
        return string.Equals(got, expected, StringComparison.Ordinal);
    }

    // התוסף מושך jobs ממתינים (long-poll). wait=שניות (0–30).
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs([FromQuery] int wait = 25)
    {
        if (!Authorized()) return Unauthorized();
        var seconds = Math.Clamp(wait, 0, 30);
        var jobs = await _queue.DequeuePendingAsync(20, TimeSpan.FromSeconds(seconds));
        return Ok(jobs);
    }

    public class SearchResultDto
    {
        public string Id { get; set; } = "";
        public List<ExtensionSupplierRow>? Rows { get; set; }
        // ok / not_logged_in / no_results / pending / error — לצורכי לוג ודיבוג בלבד.
        public string? Status { get; set; }
    }

    // התוסף מחזיר את שורות התוצאה. rows=null/ריק => אין תוצאות (השרת ימשיך בלי NC).
    [HttpPost("jobs/result")]
    public IActionResult PostResult([FromBody] SearchResultDto result)
    {
        if (!Authorized()) return Unauthorized();
        var matched = _queue.SetResult(result.Id, result.Rows);
        _logger.LogInformation("Browser search result {Id}: rows={Count} status={Status} matched={Matched}",
            result.Id, result.Rows?.Count, result.Status, matched);
        return Ok(new { matched });
    }

    // אנדפוינט בדיקה (פיתוח): מזריק job חיפוש NetComponents ידנית וממתין לתוצאה מהתוסף —
    // בלי תלות בזרימת ההצעה. הזיני מק"ט, כשאת מחוברת ל-NetComponents והתוסף פעיל.
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromQuery] string sku, [FromQuery] int qty = 1)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return BadRequest(new { message = "חסר sku" });

        var url = NetComponentsUrl.Build(sku);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = await _queue.RequestSearchAsync("NetComponents", sku, url, qty, TimeSpan.FromSeconds(60));
        sw.Stop();

        return Ok(new
        {
            sku,
            qty,
            url,
            found = rows != null && rows.Count > 0,
            count = rows?.Count,
            rows,
            elapsedMs = sw.ElapsedMilliseconds,
            note = (rows != null && rows.Count > 0)
                ? "התוסף החזיר תוצאות ✓"
                : "אין תוצאות — ודאי שהתוסף פעיל, שאת מחוברת ל-NetComponents, ושהסלקטורים תואמים את הדף"
        });
    }
}
