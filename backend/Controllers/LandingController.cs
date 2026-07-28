using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;
using System.Text.Json;

namespace comviaServer.Controllers;

// נקודת קצה ציבורית (ללא התחברות) לדף הנחיתה: לקוח מגיש בקשה להצעת מחיר, ונוצרת הצעה
// בסטטוס "Draft" עם המק"טים המבוקשים. המתמחר פותח אותה ("המשך עריכה") ומריץ השוואת מחירים.
[ApiController]
[Route("api/landing")]
public class LandingController : ControllerBase
{
    private readonly DBServices _dal;
    private readonly ILogger<LandingController> _logger;

    public LandingController(DBServices dal, ILogger<LandingController> logger)
    {
        _dal = dal;
        _logger = logger;
    }

    [HttpPost("rfq")]
    public IActionResult SubmitRfq([FromBody] PublicRfqRequest request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.CustomerName))
            return BadRequest(new { message = "חסר שם לקוח" });

        var items = (request.Items ?? new())
            .Where(i => !string.IsNullOrWhiteSpace(i.Sku))
            .Select(i => new { sku = i.Sku.Trim(), qty = i.Qty > 0 ? i.Qty : 1 })
            .ToList();

        if (items.Count == 0)
            return BadRequest(new { message = "יש להזין לפחות פריט אחד" });

        try
        {
            // 1. לקוח: קודם בודקים אם כבר קיים (לפי אימייל) — ואם כן משתמשים בו; אחרת מקימים חדש.
            var name = request.CustomerName.Trim();
            var email = request.Email?.Trim() ?? "";
            var phone = request.Phone?.Trim() ?? "";

            Customer? existing = null;
            if (!string.IsNullOrEmpty(email))
                existing = Customer.GetAll(_dal)
                    .FirstOrDefault(c => string.Equals((c.Email ?? "").Trim(), email, StringComparison.OrdinalIgnoreCase));

            int customerId;
            if (existing != null)
            {
                customerId = existing.CustomerID;
                _logger.LogInformation("Landing RFQ matched existing customer {Id} ({Name})", customerId, existing.CustomerName);
            }
            else
            {
                customerId = new Customer { CustomerName = name, Email = email, Phone = phone }.Create(_dal);
                _logger.LogInformation("Landing RFQ created new customer {Id} ({Name})", customerId, name);
            }

            // 2. יצירת הצעה בסטטוס Draft. משייכים למשתמש קיים כלשהו כדי לעמוד באילוץ ה-FK
            //    על Created_By (Users). זה לא משפיע על הנראות — MapQuote לא קורא בכלל את Created_By,
            //    כך שבצד הלקוח createdBy תמיד 0 וכל המתמחרים רואים את הבקשה.
            var createdBy = Model.User.GetAll(_dal).FirstOrDefault()?.UserID ?? 0;
            var quoteId = new Quote
            {
                CustomerID = customerId,
                CreatedBy = createdBy,
                Status = "Draft",
                Items = new List<QuoteItem>()
            }.Create(_dal);

            // 3. שמירת המק"טים המבוקשים ב-SearchResultsJson — בדיוק בפורמט שזרימת "המשך עריכה"
            //    של המתמחר קוראת (results[].sku/qty), כך שהיא תריץ השוואת מחירים ב-step2.
            var json = JsonSerializer.Serialize(new { results = items });
            Quote.UpdateSearchResults(_dal, quoteId, json);

            _logger.LogInformation("Landing RFQ -> quote {QuoteId}, customer {CustomerId}, {Count} items",
                quoteId, customerId, items.Count);

            return Ok(new { quoteID = quoteId, message = "בקשתך התקבלה בהצלחה" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Landing RFQ failed");
            var detail = ex.Message;
            if (ex.InnerException != null) detail += " | " + ex.InnerException.Message;
            return StatusCode(500, new { message = "שגיאה בשליחת הבקשה", detail });
        }
    }
}
