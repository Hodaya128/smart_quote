using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;
using System.Text.Json;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PriceComparisonController : ControllerBase
{
    private readonly PriceComparisonService _priceBl;
    private readonly IServiceScopeFactory _scopeFactory;

    public PriceComparisonController(
        PriceComparisonService priceBl,
        IServiceScopeFactory scopeFactory)
    {
        _priceBl = priceBl;
        _scopeFactory = scopeFactory;
    }

    // אבחון Waldom: GET /api/PriceComparison/waldom-test?sku=33472-1606&qty=12
    // מחזיר את הסטטוס/ה-body הגולמי מ-Waldom + הספקים שחולצו, לאימות החיבור והמיפוי.
    [HttpGet("waldom-test")]
    public async Task<IActionResult> WaldomTest([FromQuery] string sku, [FromQuery] int qty = 1)
    {
        if (string.IsNullOrWhiteSpace(sku)) return BadRequest(new { message = "חסר sku" });
        return Ok(await _priceBl.WaldomDebugAsync(sku, qty));
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] PriceSearchRequest request)
    {
        try
        {
            var result = await _priceBl.SearchAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Price comparison error", detail = ex.Message });
        }
    }

    [HttpPost("search-background")]
    public IActionResult SearchBackground([FromBody] BackgroundSearchRequest request)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dal = scope.ServiceProvider.GetRequiredService<DBServices>();

            int quoteId;
            // אם נשלח QuoteID (עריכת טיוטה קיימת) — מעדכנים אותה ל-Processing במקום ליצור חדשה.
            if (request.QuoteID.HasValue && Quote.GetById(dal, request.QuoteID.Value) != null)
            {
                quoteId = request.QuoteID.Value;
                Quote.UpdateStatus(dal, quoteId, "Processing");
            }
            else
            {
                // משייכים למשתמש קיים כדי לעמוד באילוץ ה-FK על Created_By (אחרת 0 => 500),
                // בדיוק כמו ב-LandingController. לא משפיע על נראות (MapQuote לא קורא Created_By).
                var createdBy = Model.User.GetAll(dal).FirstOrDefault()?.UserID ?? 0;
                var quote = new Quote
                {
                    CustomerID = request.CustomerID,
                    CreatedBy = createdBy,
                    Status = "Processing",
                    Items = new List<QuoteItem>() // ריק - Items ייווצרו אחרי החיפוש
                };
                quoteId = quote.Create(dal);
            }

            // הרצת חיפוש ברקע
            var searchRequest = new PriceSearchRequest
            {
                Items = request.Items
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _priceBl.SearchAsync(searchRequest);
                    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    using var bgScope = _scopeFactory.CreateScope();
                    var bgDal = bgScope.ServiceProvider.GetRequiredService<DBServices>();
                    Quote.UpdateSearchResults(bgDal, quoteId, json);
                    Quote.UpdateStatus(bgDal, quoteId, "Draft");
                }
                catch (Exception ex)
                {
                    // הערה קריטית: זהו thread רקע (Task.Run fire-and-forget). כל חריגה שתצא
                    // מכאן *מפילה את כל התהליך* (0xe0434352). לכן גם עדכון הסטטוס ל-"Error"
                    // — שעושה גישת DB שעלולה להיכשל (השרת ב-ruppin לא זמין/timeout) — חייב
                    // להיות עטוף ב-try/catch משלו, אחרת כישלון DB בתוך ה-catch מפיל הכול.
                    Console.WriteLine($"Background search failed for quote {quoteId}: {ex.Message}");
                    try
                    {
                        using var bgScope = _scopeFactory.CreateScope();
                        var bgDal = bgScope.ServiceProvider.GetRequiredService<DBServices>();
                        Quote.UpdateStatus(bgDal, quoteId, "Error");
                    }
                    catch (Exception statusEx)
                    {
                        Console.WriteLine($"Also failed to mark quote {quoteId} as Error: {statusEx.Message}");
                    }
                }
            });

            return Ok(new { quoteID = quoteId, message = "Quote created and processing in background" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error creating background search", detail = ex.Message });
        }
    }
}
