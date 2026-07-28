using comviaServer.DAL;
using System.Globalization;

namespace comviaServer.Model
{
    // מנוע "הצעות חלופיות" — בודק לכל פריט בהצעה שתי חלופות, לפי הספים ממסך ההגדרות:
    //   מודול א: מוצר חלופי (AlternativeSKU מטבלת הרכיבים) — כדאי אם החיסכון ≥ MinSavingPercent.
    //   מודול ב: הגדלת כמות למדרגת מחיר זולה — עד תוספת של MaxIncreasePercent אחוז לכמות,
    //            כדאי אם החיסכון ליחידה ≥ MinSavingPercentQuantity.
    // הנתונים אמיתיים: מדרגות המחיר מגיעות מתוצאות החיפוש של האשף, ומחיר המוצר
    // החלופי נבדק בחיפוש חי (APIs + התוסף אם מחובר).
    public class AlternativeComponent
    {
        private readonly PriceComparisonService _priceComparisonService;
        private readonly DBServices _dal;

        public AlternativeComponent(PriceComparisonService priceComparisonService, DBServices dal)
        {
            _priceComparisonService = priceComparisonService;
            _dal = dal;
        }

        public decimal GetSettingValue(string key, decimal defaultValue = 0) =>
            _dal.GetSettingValue(key, defaultValue);

        // ===== מודול א: מוצר חלופי =====
        public async Task<ModuleCheckResult> CheckAlternativeSkuAsync(AlternativeCheckItem item)
        {
            var result = new ModuleCheckResult { ModuleName = "מוצר חלופי" };

            decimal minSavingPercent = GetSettingValue("MinSavingPercent", 10);

            string? alternativeSKU = _dal.GetAlternativeSku(item.Sku);
            if (string.IsNullOrEmpty(alternativeSKU))
            {
                result.Message = $"לא מוגדר מוצר חלופי עבור {item.Sku}";
                return result;
            }

            decimal originalPrice = (decimal)item.UnitPrice;
            if (originalPrice <= 0)
            {
                result.Message = $"אין מחיר נוכחי עבור {item.Sku} — לא ניתן להשוות לחלופה";
                return result;
            }

            // תמחור חי של החלופה — אותו מנוע חיפוש שמשמש את האשף.
            var response = await _priceComparisonService.SearchAsync(new PriceSearchRequest
            {
                Items = new List<PriceSearchItem> { new PriceSearchItem { Sku = alternativeSKU, Qty = item.Qty } }
            });

            var cheapest = response?.Results?.FirstOrDefault()?.Suppliers?
                .Where(s => s.UnitPrice.HasValue && s.UnitPrice.Value > 0)
                .OrderBy(s => s.UnitPrice!.Value)
                .FirstOrDefault();

            if (cheapest == null)
            {
                result.Message = $"נמצא מוצר חלופי ({alternativeSKU}) אך לא נמצא לו מחיר זמין";
                return result;
            }

            decimal alternativePrice = (decimal)cheapest.UnitPrice!.Value;
            decimal savingPercent = (originalPrice - alternativePrice) / originalPrice * 100;

            if (savingPercent >= minSavingPercent)
            {
                result.Recommendations.Add(new Recommendation
                {
                    RecommendationType = "מוצר חלופי",
                    OriginalSKU = item.Sku,
                    SuggestedSKU = alternativeSKU,
                    OriginalSupplyConfig = item.SupplierName,
                    SuggestedSupplyConfig = cheapest.Name,
                    OriginalQuantity = item.Qty,
                    SuggestedQuantity = item.Qty,
                    OriginalUnitPrice = originalPrice,
                    SuggestedUnitPrice = alternativePrice,
                    PriceDifference = originalPrice - alternativePrice,
                    TotalSavingAmount = Math.Round((originalPrice - alternativePrice) * item.Qty, 2),
                    TotalNewCost = Math.Round(alternativePrice * item.Qty, 2),
                    SavingPercent = Math.Round(savingPercent, 2),
                    Explanation = $"המוצר החלופי {alternativeSKU} (אצל {cheapest.Name}) זול ב-{Math.Round(savingPercent, 2)}% ליחידה"
                });
                result.Message = $"נמצאה חלופת מוצר כדאית עבור {item.Sku}";
            }
            else
            {
                result.Message = $"נמצא מוצר חלופי עבור {item.Sku}, אך החיסכון ({Math.Round(savingPercent, 2)}%) מתחת לסף שהוגדר ({minSavingPercent}%)";
            }

            return result;
        }

        // ===== מודול ב: הגדלת כמות =====
        // עובד על מדרגות המחיר של הספק הנבחר שהגיעו מתוצאות החיפוש — ללא חיפוש נוסף.
        public ModuleCheckResult CheckQuantityIncrease(AlternativeCheckItem item)
        {
            var result = new ModuleCheckResult { ModuleName = "הגדלת כמות" };

            decimal maxIncreasePercent = GetSettingValue("MaxIncreasePercent", 50);
            decimal minSavingPercentQuantity = GetSettingValue("MinSavingPercentQuantity", 8);

            decimal originalPrice = (decimal)item.UnitPrice;
            if (originalPrice <= 0)
            {
                result.Message = $"אין מחיר נוכחי עבור {item.Sku} — לא ניתן לבדוק הגדלת כמות";
                return result;
            }

            bool found = false;

            foreach (var tier in item.PriceTiers ?? new List<PriceTier>())
            {
                if (!int.TryParse((tier.MinQty ?? "").Replace(",", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int newQuantity))
                    continue;
                if (tier.Price <= 0)
                    continue;

                if (newQuantity <= item.Qty)
                    continue;
                if (newQuantity > item.Qty * (1 + (double)maxIncreasePercent / 100))
                    continue;

                decimal newPrice = (decimal)tier.Price;
                decimal savingPercent = (originalPrice - newPrice) / originalPrice * 100;
                if (savingPercent < minSavingPercentQuantity)
                    continue;

                found = true;
                result.Recommendations.Add(new Recommendation
                {
                    RecommendationType = "הגדלת כמות",
                    OriginalSKU = item.Sku,
                    SuggestedSKU = item.Sku,
                    OriginalSupplyConfig = item.SupplierName,
                    SuggestedSupplyConfig = item.SupplierName,
                    OriginalQuantity = item.Qty,
                    SuggestedQuantity = newQuantity,
                    OriginalUnitPrice = originalPrice,
                    SuggestedUnitPrice = newPrice,
                    PriceDifference = originalPrice - newPrice,
                    TotalSavingAmount = Math.Round((originalPrice - newPrice) * newQuantity, 2),
                    TotalNewCost = Math.Round(newPrice * newQuantity, 2),
                    SavingPercent = Math.Round(savingPercent, 2),
                    Explanation = $"בהגדלת הכמות ל-{newQuantity} המחיר יורד ל-${newPrice:0.####} — חיסכון של {Math.Round(savingPercent, 2)}% ליחידה"
                });
            }

            result.Message = found
                ? $"נמצאה המלצה להגדלת כמות עבור {item.Sku}"
                : $"אין מדרגת מחיר כדאית להגדלת כמות עבור {item.Sku}";

            return result;
        }
    }
}
