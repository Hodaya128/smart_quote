using Microsoft.AspNetCore.Mvc;
using comviaServer.Model;

namespace comviaServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlternativeProdController : ControllerBase
    {
        private readonly AlternativeComponent _alternativeComponent;

        // AlternativeComponent רשום ב-DI (Scoped) ומוזרק ישירות — לא נבנה ידנית עם new.
        public AlternativeProdController(AlternativeComponent alternativeComponent)
        {
            _alternativeComponent = alternativeComponent;
        }

        // POST api/AlternativeProd/quote
        // מקבל את פריטי ההצעה מהאשף (כולל מחיר נוכחי ומדרגות מחיר מתוצאות החיפוש)
        // ומחזיר המלצות לחלופות לפי הספים שהוגדרו במסך ההגדרות.
        [HttpPost("quote")]
        public async Task<ActionResult> GetRecommendationsForQuote([FromBody] AlternativeCheckRequest request)
        {
            if (request?.Items == null || request.Items.Count == 0)
                return BadRequest("רשימת פריטים ריקה");

            var recommendations = new List<Recommendation>();
            var details = new List<string>();

            foreach (var item in request.Items)
            {
                if (string.IsNullOrEmpty(item.Sku) || item.Qty <= 0)
                    continue;

                // מודול הגדלת כמות — חישוב מקומי על מדרגות המחיר שהגיעו מהחיפוש.
                var quantityResult = _alternativeComponent.CheckQuantityIncrease(item);
                recommendations.AddRange(quantityResult.Recommendations);
                if (!string.IsNullOrWhiteSpace(quantityResult.Message))
                    details.Add(quantityResult.Message);

                // מודול מוצר חלופי — חיפוש חי של מחיר החלופה.
                var alternativeResult = await _alternativeComponent.CheckAlternativeSkuAsync(item);
                recommendations.AddRange(alternativeResult.Recommendations);
                if (!string.IsNullOrWhiteSpace(alternativeResult.Message))
                    details.Add(alternativeResult.Message);
            }

            // מיון לפי חיסכון מהגבוה לנמוך
            var sorted = recommendations
                .OrderByDescending(r => r.SavingPercent)
                .ToList();

            if (sorted.Count == 0)
                return Ok(new
                {
                    message = "לא נמצאו חלופות כדאיות",
                    details = details,
                    recommendations = sorted
                });

            decimal totalSavingAmount = sorted
                .Where(r => r.TotalSavingAmount > 0)
                .Sum(r => r.TotalSavingAmount);

            return Ok(new
            {
                message = $"נמצאו {sorted.Count} המלצות",
                details = details,
                totalSavingAmount = Math.Round(totalSavingAmount, 2),
                recommendations = sorted
            });
        }
    }
}
