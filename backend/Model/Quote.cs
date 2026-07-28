using comviaServer.DAL;
using comviaServer.Security;

namespace comviaServer.Model;

public class Quote
{
    public int QuoteID { get; set; }
    public int CustomerID { get; set; }
    public int CreatedBy { get; set; }
    public string? ComponentSKU { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public double TotalProductCost { get; set; }
    public double TotalProfit { get; set; }
    public double FinalTotalPrice { get; set; }
    public string? SearchResultsJson { get; set; }

    public string? CreatedByName { get; set; }

    // Navigation (ממולאים ידנית ב-DBServices)
    public Customer? Customer { get; set; }
    public List<QuoteItem> Items { get; set; } = new();

    // ===== לוגיקה עסקית (לשעבר QuoteService) =====

    public static List<Quote> GetAll(DBServices dal) => dal.GetAllQuotes();

    public static Quote? GetById(DBServices dal, int id) => dal.GetQuoteById(id);

    public int Create(DBServices dal)
    {
        CalculateTotals();
        return dal.InsertQuote(this);
    }

    /// יצירת הצעה מתוצאות חיפוש - כולל יצירה אוטומטית של ספקים חדשים.
    /// protector נדרש כי יצירת ספק חדש עוברת דרך Supplier.Create (הצפנת סיסמה).
    public static int CreateFromRequest(DBServices dal, CredentialProtector protector, QuoteCreateRequest request)
    {
        // טעינת כל הספקים פעם אחת
        var existingSuppliers = dal.GetAllSuppliers();
        var supplierMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in existingSuppliers)
            supplierMap[s.SupplierName] = s.SupplierID;

        // טעינת כל הרכיבים ויצירת חסרים
        var existingComponents = dal.GetAllComponents();
        var componentSet = new HashSet<string>(existingComponents.Select(c => c.ComponentSKU), StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.ComponentSKU) && !componentSet.Contains(item.ComponentSKU))
            {
                dal.InsertComponent(new Component
                {
                    ComponentSKU = item.ComponentSKU,
                    Description = item.Description ?? "",
                    BaseUnit = "pcs"
                });
                componentSet.Add(item.ComponentSKU);
            }
        }

        var quote = new Quote
        {
            CustomerID = request.CustomerID,
            CreatedBy = request.CreatedBy,
            Status = request.Status,
            Items = new List<QuoteItem>()
        };

        foreach (var item in request.Items)
        {
            // חיפוש או יצירת ספק
            int supplierId;
            if (string.IsNullOrWhiteSpace(item.SupplierName) || item.SupplierName == "N/A")
            {
                // שורה בלי ספק (למשל "אין מלאי") — משתמשים בספק "ללא ספק" שנוצר פעם אחת.
                // ברירת המחדל הקודמת הייתה SupplierID=1 קשיח, שנפל על FK כשאין ספק עם מזהה 1.
                const string placeholder = "ללא ספק";
                if (!supplierMap.TryGetValue(placeholder, out supplierId))
                {
                    supplierId = new Supplier { SupplierName = placeholder, WebsiteUrl = "" }.Create(dal, protector);
                    supplierMap[placeholder] = supplierId;
                }
            }
            else
            {
                if (supplierMap.TryGetValue(item.SupplierName, out var existingId))
                {
                    supplierId = existingId;
                }
                else
                {
                    // חילוץ URL בסיסי מהלינק
                    string baseUrl = ExtractBaseUrl(item.SupplierLink);

                    // יצירת ספק חדש
                    supplierId = new Supplier
                    {
                        SupplierName = item.SupplierName,
                        WebsiteUrl = baseUrl
                    }.Create(dal, protector);
                    supplierMap[item.SupplierName] = supplierId;
                }
            }

            quote.Items.Add(new QuoteItem
            {
                ComponentSKU = item.ComponentSKU,
                SupplierID = supplierId,
                SupplyConfig = item.SupplyConfig,
                Quantity = item.Quantity,
                CostPriceMoment = item.CostPriceMoment,
                ProfitMargin = item.ProfitMargin,
                FinalPriceToClient = item.FinalPriceToClient
            });
        }

        // חישוב סכומים מהנתונים שהגיעו
        double totalCost = 0;
        double totalFinal = 0;
        foreach (var item in quote.Items)
        {
            totalCost += item.CostPriceMoment * item.Quantity;
            totalFinal += item.FinalPriceToClient;
        }
        quote.TotalProductCost = totalCost;
        quote.FinalTotalPrice = totalFinal;
        quote.TotalProfit = totalFinal - totalCost;

        // אם QuoteID קיים - עדכון הצעה קיימת (מ-draft), אחרת יצירה חדשה
        if (request.QuoteID.HasValue && request.QuoteID.Value > 0)
        {
            dal.UpdateQuote(request.QuoteID.Value, quote);
            return request.QuoteID.Value;
        }

        return dal.InsertQuote(quote);
    }

    public bool Update(DBServices dal, int id)
    {
        if (dal.GetQuoteById(id) == null) return false;
        CalculateTotals();
        dal.UpdateQuote(id, this);
        return true;
    }

    public static bool UpdateStatus(DBServices dal, int id, string status)
    {
        if (dal.GetQuoteById(id) == null) return false;
        dal.UpdateQuoteStatus(id, status);
        return true;
    }

    public static bool UpdateSearchResults(DBServices dal, int id, string searchResultsJson)
    {
        if (dal.GetQuoteById(id) == null) return false;
        dal.UpdateQuoteSearchResults(id, searchResultsJson);
        return true;
    }

    public static bool Delete(DBServices dal, int id)
    {
        if (dal.GetQuoteById(id) == null) return false;
        dal.DeleteQuote(id);
        return true;
    }

    private void CalculateTotals()
    {
        double totalCost = 0;
        double totalFinal = 0;

        foreach (var item in Items)
        {
            item.FinalPriceToClient = item.CostPriceMoment * (1 + item.ProfitMargin / 100.0) * item.Quantity;
            totalCost += item.CostPriceMoment * item.Quantity;
            totalFinal += item.FinalPriceToClient;
        }

        TotalProductCost = totalCost;
        FinalTotalPrice = totalFinal;
        TotalProfit = totalFinal - totalCost;
    }

    /// חילוץ כתובת בסיסית מלינק מלא (scheme://host)
    private static string ExtractBaseUrl(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return "";
        try
        {
            var uri = new Uri(link);
            return $"{uri.Scheme}://{uri.Host}";
        }
        catch { return ""; }
    }
}
