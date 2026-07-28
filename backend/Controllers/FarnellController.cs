using Microsoft.AspNetCore.Mvc;
using comviaServer.Model;

namespace comviaServer.Controllers;

// נקודות הקצה שאיתן מדבר תוסף ה-Chrome (שרץ בדפדפן המחובר של המשתמש).
// GET  /api/farnell/jobs         — התוסף מושך בקשות מחיר ממתינות (long-poll).
// POST /api/farnell/jobs/result  — התוסף מחזיר את המחיר שחילץ מהסשן של המשתמש.
// אימות פשוט ב-token משותף (Header: X-Farnell-Token) מתוך appsettings.
[ApiController]
[Route("api/farnell")]
public class FarnellController : ControllerBase
{
    private readonly FarnellJobQueue _queue;
    private readonly IConfiguration _config;
    private readonly ILogger<FarnellController> _logger;

    public FarnellController(FarnellJobQueue queue, IConfiguration config, ILogger<FarnellController> logger)
    {
        _queue = queue;
        _config = config;
        _logger = logger;
    }

    // אם לא הוגדר token ב-appsettings — פתוח (נוח לפיתוח מקומי). בפרודקשן: להגדיר token.
    private bool Authorized()
    {
        var expected = _config["Farnell:ExtensionToken"];
        if (string.IsNullOrEmpty(expected)) return true;
        var got = Request.Headers["X-Farnell-Token"].FirstOrDefault();
        return string.Equals(got, expected, StringComparison.Ordinal);
    }

    // התוסף מושך jobs ממתינים. wait=שניות ל-long-poll (0–30): השרת מחזיק את הבקשה
    // עד שמגיע job או עד שפג הזמן, כך שאיסוף ה-job כמעט מיידי בלי polling אגרסיבי.
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs([FromQuery] int wait = 25)
    {
        if (!Authorized()) return Unauthorized();
        var seconds = Math.Clamp(wait, 0, 30);
        var jobs = await _queue.DequeuePendingAsync(20, TimeSpan.FromSeconds(seconds));
        return Ok(jobs);
    }

    // אנדפוינט בדיקה (פיתוח): מזריק job של Farnell ידנית וממתין לתוצאה מהתוסף — בלי תלות
    // ב-NetComponents. שימושי לאימות הזרימה המלאה והסלקטורים. הזיני URL מוצר אמיתי של
    // il.farnell.com + qty, כשאת מחוברת ל-Farnell והתוסף פעיל.
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromQuery] string url, [FromQuery] int qty = 1)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest(new { message = "חסר url של מוצר Farnell" });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await _queue.RequestPriceAsync("Farnell", url, qty, TimeSpan.FromSeconds(30));
        sw.Stop();

        return Ok(new
        {
            url,
            qty,
            price = result?.Price,
            currency = result?.Currency,
            found = result != null,
            elapsedMs = sw.ElapsedMilliseconds,
            note = result != null
                ? "התוסף החזיר מחיר ✓"
                : "אין מחיר — ודאי שהתוסף פעיל, שאת מחוברת ל-Farnell, ושהסלקטורים תואמים את הדף"
        });
    }

    public class JobResultDto
    {
        public string Id { get; set; } = "";
        public double? Price { get; set; }
        // המטבע שזוהה בדף il.farnell.com (USD/GBP/EUR/ILS...) — להמרה ל-USD בצד השרת.
        public string? Currency { get; set; }
        // ok / not_logged_in / no_table / no_price / error — לצורכי לוג ודיבוג בלבד.
        public string? Status { get; set; }
    }

    // התוסף מחזיר את התוצאה. price=null => אין מחיר (השרת ישאיר את מחיר ה-NC).
    [HttpPost("jobs/result")]
    public IActionResult PostResult([FromBody] JobResultDto result)
    {
        if (!Authorized()) return Unauthorized();
        var matched = _queue.SetResult(result.Id, result.Price, result.Currency);
        _logger.LogInformation("Farnell result {Id}: price={Price}{Currency} status={Status} matched={Matched}",
            result.Id, result.Price, result.Currency, result.Status, matched);
        return Ok(new { matched });
    }
}
