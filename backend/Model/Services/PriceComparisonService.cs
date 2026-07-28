using comviaServer.DAL;
using comviaServer.Security;
using System.Text.Json;

namespace comviaServer.Model;

public class PriceComparisonService : IDisposable
{
    // NetComponents — ערכי ברירת מחדל (fallback). הערכים האמיתיים נטענים מה-DB
    // (ספק בשם "NetComponents" עם RequiresLogin=1) ומפוענחים בזמן ההתחברות.
    private const string NC_ACCOUNT = "542347";
    private const string NC_USERNAME = "Comvia";
    private const string NC_PASSWORD = "comvia6108";
    private const string NC_SUPPLIER_NAME = "NetComponents";

    // מק"ט הדגמה (לא קיים בקטלוג) — מחזיר נתונים מבוקרים להצגת המלצת "מדרגת כמות הבאה".
    // המשתמש מקליד "MX150-2X8D"; NormalizeSku מסיר מקפים/רווחים → "MX1502X8D".
    private const string DEMO_SKU = "MX1502X8D";

    // DigiKey credentials
    private const string DK_CLIENT_ID = "foiE4G9DR0DmvY6JkLfQNMYsP94meBRMejA1aRt6BLpSQl6N";
    private const string DK_CLIENT_SECRET = "klpppGbs8zehBJx8E2aqANJfunj7EBDL4ECqWSABdhxIZUm8OgESasWGLtPlgjN6";
    private const string DK_TOKEN_URL = "https://api.digikey.com/v1/oauth2/token";
    private const string DK_SEARCH_URL = "https://api.digikey.com/products/v4/search/keyword";

    private readonly SemaphoreSlim _searchQueue = new(1, 1); // תור - חיפוש אחד בכל פעם

    private string? _dkToken;
    private DateTime _dkTokenExpiry = DateTime.MinValue;
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<PriceComparisonService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // תור המחירים של Farnell — נשלף דרך תוסף הדפדפן בסשן המחובר של המשתמש (לא Selenium).
    private readonly FarnellJobQueue _farnellQueue;
    // כמה זמן להמתין לתוסף לפני שמוותרים ונשארים עם מחיר ה-NC. נשלט ב-appsettings: "Farnell:TimeoutSeconds".
    private readonly TimeSpan _farnellTimeout;

    // תור החיפושים של NetComponents — עבר מ-Selenium לתוסף הדפדפן (אותו רעיון כמו Farnell):
    // השרת מכניס job חיפוש וממתין; התוסף פותח את עמוד החיפוש בסשן המחובר ומחזיר את שורות הטבלה.
    private readonly BrowserSearchQueue _browserQueue;
    // כמה זמן להמתין לתוסף עבור חיפוש NetComponents (טבלה + מחירים ב-AJAX לוקחים יותר מ-Farnell).
    private readonly TimeSpan _ncTimeout;
    // הצגת ספקים מורשים (A) בלבד מתוצאות NetComponents — סינון ברוקרים/לא-מורשים.
    private readonly bool _ncAuthorizedOnly;

    // כמה זמן להמתין לתוסף עבור חיפוש Arrow (React SPA — לוקח זמן להתרנדר).
    private readonly TimeSpan _arrowTimeout;

    // מתג הפעלה ל-Arrow. כבוי כרגע (Akamai חוסם בדפדפן הרגיל). כשמדליקים — להחזיר ל-true
    // ב-appsettings ("Arrow:Enabled"). כבוי => לא מוסיף timeout/עומס לחיפוש.
    private readonly bool _arrowEnabled;

    // Master Electronics — דרך תוסף הדפדפן (סשן המשתמש). חיפוש מק"ט מפנה לעמוד המוצר
    // שם מוצגת טבלת מחירי החוזה (.price-breakdown). דורש שהמשתמש יהיה מחובר ל-Master.
    private readonly bool _masterEnabled;
    private readonly TimeSpan _masterTimeout;

    // Waldom — ספק נוסף דרך API ציבורי (כמו DigiKey, בלי דפדפן/תוסף). המפתח והכתובת ב-appsettings.
    private readonly bool _waldomEnabled;
    private readonly string _waldomApiKey;
    private readonly string _waldomBaseUrl;
    // פרמטרים של החיפוש (ניתנים לכוונון ב-appsettings בלי קומפילציה). InStockOnly/ExactMatch=0/1.
    private readonly int _waldomInStock;
    private readonly int _waldomExact;
    private readonly int _waldomCount;

    // המרת מחירים שאינם בדולר ל-USD (שערים מ-API ציבורי).
    private readonly CurrencyService _currency;

    // חילוץ מחירים ב-AI לאתרים מבולגנים (Arrow) — הצעה לכל מחסן.
    private readonly AiSupplierExtractor _aiExtractor;

    public PriceComparisonService(
        ILogger<PriceComparisonService> logger,
        IServiceScopeFactory scopeFactory,
        FarnellJobQueue farnellQueue,
        BrowserSearchQueue browserQueue,
        CurrencyService currency,
        AiSupplierExtractor aiExtractor,
        IConfiguration config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _farnellQueue = farnellQueue;
        _browserQueue = browserQueue;
        _currency = currency;
        _aiExtractor = aiExtractor;
        // ברירת מחדל 45ש': התוסף ממתין עד 30ש' לטעינת הטאב + ~25ש' polling. timeout קצר מדי
        // (היה 15) גרם לשרת לוותר לפני שהתוסף הספיק להחזיר מחיר — ולכן "לא נמשך מחיר מפרנל".
        _farnellTimeout = TimeSpan.FromSeconds(config.GetValue("Farnell:TimeoutSeconds", 45));
        _ncTimeout = TimeSpan.FromSeconds(config.GetValue("NetComponents:TimeoutSeconds", 60));
        _ncAuthorizedOnly = config.GetValue("NetComponents:AuthorizedOnly", true);
        _arrowTimeout = TimeSpan.FromSeconds(config.GetValue("Arrow:TimeoutSeconds", 60));
        _arrowEnabled = config.GetValue("Arrow:Enabled", false);
        _masterEnabled = config.GetValue("MasterElectronics:Enabled", true);
        _masterTimeout = TimeSpan.FromSeconds(config.GetValue("MasterElectronics:TimeoutSeconds", 60));
        _waldomApiKey = config["Waldom:ApiKey"] ?? "";
        _waldomBaseUrl = config["Waldom:BaseUrl"] ?? "https://www.waldom.com";
        _waldomEnabled = config.GetValue("Waldom:Enabled", true) && !string.IsNullOrWhiteSpace(_waldomApiKey);
        _waldomInStock = config.GetValue("Waldom:InStockOnly", 0);
        _waldomExact = config.GetValue("Waldom:ExactMatch", 1);
        _waldomCount = config.GetValue("Waldom:ResultsCount", 5);
    }

    // =============================================
    // ===== Main search =====
    // =============================================

    public async Task<PriceSearchResponse> SearchAsync(PriceSearchRequest request)
    {
        // תור - מחכים לתור אם חיפוש אחר כבר רץ
        await _searchQueue.WaitAsync();
        try
        {
            return await DoSearchAsync(request);

        }
        finally
        {
            _searchQueue.Release();
        }
    }

    private async Task<PriceSearchResponse> DoSearchAsync(PriceSearchRequest request)

    {
        var response = new PriceSearchResponse();

        // טוען שערי המרה (cache ל-6ש') — כדי שנוכל לייבא גם מחירים שאינם בדולר ולנרמל ל-USD.
        await _currency.EnsureRatesAsync();

        foreach (var item in request.Items)
        {
            var normalizedSku = NormalizeSku(item.Sku);

            _logger.LogInformation(
            "Searching item {OriginalSku} normalized to {NormalizedSku} Qty={Qty}",
            item.Sku,
            normalizedSku,
            item.Qty);

            // ===== מק"ט הדגמה: מחזיר נתונים מבוקרים (כולל מדרגות כמות) ללא תלות ב-NC/Waldom/DB. =====
            if (normalizedSku == DEMO_SKU)
            {
                response.Results.Add(BuildDemoResult(normalizedSku, item.Qty));
                continue;
            }

            // חיפוש NetComponents (דרך תוסף הדפדפן), DigiKey (API) ו-Arrow (דרך התוסף) במקביל.
            // Arrow תמיד נבדק — המלאי שלו לא תמיד מעודכן ב-NetComponents.
            var ncTask = SearchNetComponentsViaExtensionAsync(normalizedSku, item.Qty);
            var dkTask = SearchDigiKeyAsync(normalizedSku);
            // Arrow מחפש לפי המק"ט המקורי (עם מקפים), כפי שמופיע ב-URL לדוגמה.
            var arrowTask = SearchArrowViaExtensionAsync(item.Sku, item.Qty);
            // Master Electronics — דרך התוסף (מחיר חוזה בסשן המחובר). מק"ט מקורי עם מקפים.
            var masterTask = SearchMasterElectronicsViaExtensionAsync(item.Sku, item.Qty);
            // Waldom — ספק נוסף דרך API (מק"ט מקורי עם מקפים).
            var waldomTask = SearchWaldomAsync(item.Sku, item.Qty);

            var ncResults = await ncTask;
            var dkResults = await dkTask;

            // בניית רשימת ספקים
            var suppliers = new List<SupplierResult>();
            var competitors = new List<CompetitorResult>();

            var competitorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "mouser", "digikey", "mouser electronics", "mouser electronics inc." };

            foreach (var nc in ncResults)
            {
                // ממירים את כל שוברי המחיר ל-USD (גם אם הגיעו ב-EUR/GBP וכו') לפני בחירת המחיר.
                var ncUsdTiers = _currency.ToUsdTiers(nc.Prices);
                double? price = GetPriceForQty(ncUsdTiers, item.Qty);

                // אבחון: מראה בדיוק מה NC החזיר לכל ספק (מחיר+מטבע גולמי) ומה התקבל אחרי המרה.
                // אם rawTiers ריק => ל-NC אין מחיר לשורה הזו (לא בעיית המרה). אם יש מטבע אבל
                // chosen=N/A => בעיית המרה/מטבע לא מוכר. עוזר לאבחן "לא בוצעה המרה".
                _logger.LogInformation("NC '{Name}' [{Country}] rawTiers=[{Raw}] -> chosen={Chosen}",
                    nc.Supplier, nc.Country,
                    string.Join(", ", nc.Prices.Select(p => $"{p.Price}{p.Currency}@{p.MinQty}")),
                    price?.ToString() ?? "N/A");
                var supplierResult = new SupplierResult
                {
                    Name = nc.Supplier,
                    Country = nc.Country,
                    UnitPrice = price,
                    Currency = price != null ? "USD" : null,
                    QtyAvailable = nc.Quantity,
                    Link = nc.SupplierLink,
                    Description = nc.Description,
                    Manufacturer = nc.Manufacturer,
                    PriceTiers = ncUsdTiers

                };

                // Mouser/DigiKey מ-NetComponents -> מתחרים
                if (competitorNames.Contains(nc.Supplier))
                {
                    competitors.Add(new CompetitorResult
                    {
                        Source = nc.Supplier,
                        UnitPrice = price,
                        Currency = price != null ? "USD" : null,
                        Description = nc.Description,
                        Manufacturer = nc.Manufacturer,

                    });
                }
                else
                {
                    suppliers.Add(supplierResult);
                }
            }

            // הוספת תוצאות DigiKey
            foreach (var dk in dkResults)
            {
                double? price = GetPriceForQty(_currency.ToUsdTiers(dk.Prices), item.Qty);
                competitors.Add(new CompetitorResult
                {
                    Source = "DigiKey",
                    UnitPrice = price,
                    Currency = price != null ? "USD" : null,
                    Description = dk.Description,
                    Manufacturer = dk.Manufacturer,
                    QtyAvailable = dk.QtyAvailable
                });
            }

            // הוספת תוצאות Arrow (דרך התוסף) — ספק נוסף, תמיד נבדק (המלאי שלו לא תמיד ב-NC).
            suppliers.AddRange(await arrowTask);

            // הוספת תוצאות Master Electronics (דרך התוסף) — מחיר חוזה מהסשן המחובר.
            suppliers.AddRange(await masterTask);

            // הוספת תוצאות Waldom (דרך API) — ספק נוסף, תמיד נבדק.
            suppliers.AddRange(await waldomTask);

            // Farnell — דרך תוסף הדפדפן (סשן המשתמש). אם התוסף לא זמין/timeout נשאר מחיר ה-NC.
            await EnrichFarnellViaExtensionAsync(suppliers, item.Qty);

            // מציאת הזול ביותר
            CheapestInfo? cheapestSupplier = null;
            foreach (var s in suppliers)
            {
                if (s.UnitPrice.HasValue && (cheapestSupplier == null || s.UnitPrice < cheapestSupplier.UnitPrice))
                    cheapestSupplier = new CheapestInfo
                    {
                        Name = s.Name,
                        UnitPrice = s.UnitPrice.Value,
                        Currency = "USD",
                        Description = s.Description
                    };
            }

            CheapestInfo? cheapestCompetitor = null;
            foreach (var c in competitors)
            {
                if (c.UnitPrice.HasValue && (cheapestCompetitor == null || c.UnitPrice < cheapestCompetitor.UnitPrice))
                    cheapestCompetitor = new CheapestInfo
                    {
                        Name = c.Source,
                        Source = c.Source,
                        UnitPrice = c.UnitPrice.Value,
                        Currency = "USD",
                        Description = c.Description
                    };
            }

            response.Results.Add(new PriceSearchResult
            {
                Sku = normalizedSku/*   Sku = item.Sku*/,
                Qty = item.Qty,
                Suppliers = suppliers,
                Competitors = competitors,
                CheapestSupplier = cheapestSupplier,
                CheapestCompetitor = cheapestCompetitor
            });
        }

        return response;
    }

    // תוצאת הדגמה עבור DEMO_SKU: נתונים מבוקרים עם מדרגות כמות, ללא תלות ב-NC/Waldom/DB.
    // המחיר ליחידה יורד במדרגות כדי שההמלצה "מדרגת כמות הבאה" תמיד תהיה רלוונטית להצגה.
    private static PriceSearchResult BuildDemoResult(string sku, int qty)
    {
        var tiers = new List<PriceTier>
        {
            new PriceTier { MinQty = "10",  Price = 1.50, Currency = "USD" },
            new PriceTier { MinQty = "50",  Price = 1.20, Currency = "USD" },
            new PriceTier { MinQty = "100", Price = 1.00, Currency = "USD" }
        };

        // המחיר לפי הכמות המבוקשת — המדרגה הגבוהה ביותר שהכמות עוברת אותה.
        double unitPrice = qty >= 100 ? 1.00 : qty >= 50 ? 1.20 : 1.50;

        var supplier = new SupplierResult
        {
            Name = "Demo Supplier",
            Country = "IL",
            UnitPrice = unitPrice,
            Currency = "USD",
            QtyAvailable = "5000",
            Link = "",
            Description = "Demo component for presentation (quantity tiers)",
            Manufacturer = "Demo Manufacturer",
            PriceTiers = tiers
        };

        return new PriceSearchResult
        {
            Sku = sku,
            Qty = qty,
            Suppliers = new List<SupplierResult> { supplier },
            Competitors = new List<CompetitorResult>(),
            CheapestSupplier = new CheapestInfo
            {
                Name = supplier.Name,
                UnitPrice = unitPrice,
                Currency = "USD",
                Description = supplier.Description
            },
            CheapestCompetitor = null
        };
    }

    // =============================================
    // =============================================

    // =============================================
    // ===== Farnell via browser extension (user session) =====
    // =============================================

    // ל-Farnell לא משתמשים ב-Selenium (האתר חוסם ב-Akamai מהשרת) ולא ב-API ציבורי
    // (שמחזיר מחיר מחירון, לא מחיר חוזה). במקום זה: מכניסים job לתור, ותוסף Chrome שרץ
    // בדפדפן המחובר של המשתמש שולף את מחיר החוזה מהסשן האמיתי ומחזיר. בהצלחה — מחליף
    // את UnitPrice ומסמן IsCustomPrice. בכישלון/timeout/לא-מחובר — נשאר מחיר ה-NC.
    private async Task EnrichFarnellViaExtensionAsync(List<SupplierResult> suppliers, int requestedQty)
    {
        var targets = suppliers
            .Where(s => IsFarnell(s.Name)
                        && !string.IsNullOrWhiteSpace(s.Link)
                        && ParseNum(s.QtyAvailable) > 0)
            .ToList();

        if (targets.Count == 0) return;

        foreach (var s in targets)
        {
            try
            {
                // מנווטים לדומיין הישראלי שבו מוצג מחיר החוזה של COMVIA.
                var url = ToIsraelFarnell(s.Link);
                var result = await _farnellQueue.RequestPriceAsync("Farnell", url, requestedQty, _farnellTimeout);

                if (result != null)
                {
                    // il.farnell.com מציג מחיר שאינו בהכרח USD — ממירים לפי המטבע שזוהה בדף.
                    // אם לא זוהה מטבע / לא ניתן להמיר — נשארים עם הערך הגולמי (כמו קודם).
                    var usd = _currency.ToUsd(result.Price, result.Currency) ?? result.Price;
                    s.UnitPrice = usd;
                    s.Currency = "USD";
                    s.IsCustomPrice = true;
                    _logger.LogInformation("Farnell extension custom price {Raw} {Cur} -> {Usd} USD for {Url}",
                        result.Price, result.Currency ?? "?", usd, url);
                }
                else
                {
                    _logger.LogInformation("Farnell extension returned no price (offline/timeout/not-logged-in), keeping NC price for {Url}", url);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Farnell extension enrichment failed for {Link}", s.Link);
            }
        }
    }

    private static bool IsFarnell(string? name)
        => !string.IsNullOrWhiteSpace(name) && name.ToLowerInvariant().Contains("farnell");

    // ממיר כל דומיין farnell.com (למשל uk.farnell.com) ל-il.farnell.com, שם מוצג מחיר החוזה.
    private static string ToIsraelFarnell(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        try
        {
            var u = new Uri(url);
            if (u.Host.EndsWith("farnell.com", StringComparison.OrdinalIgnoreCase)
                && !u.Host.Equals("il.farnell.com", StringComparison.OrdinalIgnoreCase))
            {
                return new UriBuilder(u) { Host = "il.farnell.com" }.Uri.ToString();
            }
            return url;
        }
        catch
        {
            return url.Replace("uk.farnell.com", "il.farnell.com")
                      .Replace("www.farnell.com", "il.farnell.com");
        }
    }

    // =============================================
    // ===== Price tier matching =====
    // =============================================

    private static double? GetPriceForQty(List<PriceTier> prices, int requestedQty)
    {
        var usdPrices = prices
            .Where(p => p.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => ParseNum(p.MinQty))
            .ToList();

        if (usdPrices.Count == 0) return null;

        // המחיר הזול ביותr שאליו הכמות המבוקשת *מזכה* (MinQty <= qty).
        // usdPrices ממוין יורד לפי MinQty, לכן ההתאמה הראשונה היא שובר הכמות הגבוה
        // ביותר שעדיין <= qty — כלומר המחיר הטוב ביותר שמגיע ללקוח בכמות הזו.
        foreach (var p in usdPrices)
        {
            if (ParseNum(p.MinQty) <= requestedQty)
                return p.Price;
        }

        // הכמות המבוקשת קטנה מכל שובר כמות זמין (MOQ של הספק גבוה מהכמות).
        // אין מחיר תקף לכמות הזו — מחזירים null (N/A) במקום מחיר שמותנה בכמות גדולה,
        // אחרת הספק נראה זול מלאכותית והמשתמש לא יכול לרכוש בכמות שביקש.
        return null;
    }

    private static double ParseNum(string val)
    {
        if (double.TryParse(val.Replace(",", ""), out var result))
            return result;
        return 0;
    }

    private static string NormalizeSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
            return "";

        return sku
            .Trim()
            .Replace(" ", "")
            .Replace("-", "")
            .Replace("_", "")
            .ToUpperInvariant();
    }

    // =============================================
    // ===== NetComponents (browser extension) =====
    // =============================================

    // חיפוש NetComponents דרך תוסף הדפדפן: בונים URL חיפוש למק"ט, מכניסים job לתור,
    // והתוסף (שרץ בסשן המחובר של המשתמש) פותח את הדף, קורא את טבלת התוצאות ומחזיר שורות.
    // ממפים את השורות לאותו NetComponentsResult שהשתמשנו בו עד היום — כך שאר הזרימה לא משתנה.
    // null/timeout => מתייחסים כ"אין תוצאות NC" (לא מפילים את החיפוש).
    private async Task<List<NetComponentsResult>> SearchNetComponentsViaExtensionAsync(string normalizedSku, int qty)
    {
        try
        {
            var url = NetComponentsUrl.Build(normalizedSku);
            _logger.LogInformation("NetComponents via extension: '{Sku}' -> {Url}", normalizedSku, url);

            var rows = await _browserQueue.RequestSearchAsync("NetComponents", normalizedSku, url, qty, _ncTimeout);
            if (rows == null)
            {
                _logger.LogWarning("NetComponents extension returned null (offline/timeout/not-logged-in) for '{Sku}'", normalizedSku);
                return new List<NetComponentsResult>();
            }

            _logger.LogInformation("NetComponents extension returned {Count} rows for '{Sku}'", rows.Count, normalizedSku);

            // סינון לספקים מורשים בלבד (המסומנים A ב-NC). נשלט ב-appsettings:
            // "NetComponents:AuthorizedOnly" (ברירת מחדל true). ברוקרים/לא-מורשים מסוננים.
            if (_ncAuthorizedOnly)
            {
                var before = rows.Count;
                rows = rows.Where(r => r.Authorized).ToList();
                if (rows.Count < before)
                    _logger.LogInformation("NetComponents: filtered {Removed} non-authorized rows ({Kept} authorized kept) for '{Sku}'",
                        before - rows.Count, rows.Count, normalizedSku);
            }

            return rows.Select(r => new NetComponentsResult
            {
                PartNumber = r.PartNumber,
                Manufacturer = r.Manufacturer,
                Description = r.Description,
                Country = r.Country,
                Quantity = r.Quantity,
                Prices = r.Prices ?? new List<PriceTier>(),
                Supplier = r.Supplier,
                SupplierLink = r.SupplierLink
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NetComponents extension search failed for '{Sku}'", normalizedSku);
            return new List<NetComponentsResult>();
        }
    }

    // =============================================
    // ===== Arrow (browser extension) =====
    // =============================================

    // סנטינל: התוסף מסמן ששורת התוצאה מכילה טקסט גולמי לחילוץ ב-AI (במקום פירסור סלקטורים).
    private const string AI_RAW_SUPPLIER = "__AI_RAW__";

    // חיפוש Arrow דרך תוסף הדפדפן. Arrow הוא React SPA עם הגנת Akamai — לא ניתן fetch מהשרת,
    // לכן התוסף פותח את עמוד החיפוש בסשן המחובר. ה-DOM שלו לא יציב (class-ים אקראיים) ומראה
    // מספר הצעות לכל מחסן, לכן התוסף מחזיר את *הטקסט* של אזור התוצאות ואנחנו מחלצים ב-AI:
    // הצעה לכל מחסן => שורת ספק נפרדת, ומסננים הצעות שהכמות המבוקשת לא מגיעה למדרגה שלהן.
    // מחירי Arrow בסשן המחובר הם מחירי חוזה => IsCustomPrice=true.
    private async Task<List<SupplierResult>> SearchArrowViaExtensionAsync(string sku, int qty)
    {
        var result = new List<SupplierResult>();
        if (!_arrowEnabled) return result;
        if (string.IsNullOrWhiteSpace(sku)) return result;

        try
        {
            var url = ArrowUrl.Build(sku);
            _logger.LogInformation("Arrow via extension: '{Sku}' -> {Url}", sku, url);

            var rows = await _browserQueue.RequestSearchAsync("Arrow", sku, url, qty, _arrowTimeout);
            if (rows == null || rows.Count == 0)
            {
                _logger.LogWarning("Arrow extension returned nothing (offline/timeout/blocked/no-match) for '{Sku}'", sku);
                return result;
            }

            // נתיב AI: התוסף החזיר טקסט גולמי (שורה בודדת עם ה-supplier=__AI_RAW__).
            var rawRow = rows.FirstOrDefault(r => string.Equals(r.Supplier, AI_RAW_SUPPLIER, StringComparison.Ordinal));
            if (rawRow != null)
            {
                var offers = await _aiExtractor.ExtractOffersAsync("Arrow", rawRow.Description);
                _logger.LogInformation("Arrow AI extraction: {Count} offers for '{Sku}'", offers.Count, sku);

                foreach (var offer in offers)
                {
                    var usdTiers = _currency.ToUsdTiers(offer.Tiers);
                    double? price = GetPriceForQty(usdTiers, qty);
                    // סינון לפי התאמת כמות: אם הכמות המבוקשת קטנה מהמדרגה הנמוכה ביותר של
                    // ההצעה (למשל צריך 40 וההצעה מתחילה ב-400) — GetPriceForQty מחזיר null => מדלגים.
                    if (price == null) continue;

                    var wh = string.IsNullOrWhiteSpace(offer.Warehouse) ? "" : $" — {offer.Warehouse}";
                    result.Add(new SupplierResult
                    {
                        Name = "Arrow" + wh,          // כל מחסן = שורת ספק נפרדת
                        Country = offer.Warehouse,
                        UnitPrice = price,
                        Currency = "USD",
                        QtyAvailable = offer.Stock,
                        Link = url,
                        Description = "",
                        Manufacturer = "",
                        PriceTiers = usdTiers,
                        IsCustomPrice = true          // מחיר חוזה מהסשן המחובר
                    });
                }
                return result;
            }

            // נתיב סלקטורים ישן (אם התוסף יחזיר שורות מפורסרות במקום טקסט) — נשמר לתאימות.
            foreach (var r in rows)
            {
                var usdTiers = _currency.ToUsdTiers(r.Prices);
                double? price = GetPriceForQty(usdTiers, qty);
                if (price == null) continue;
                result.Add(new SupplierResult
                {
                    Name = string.IsNullOrWhiteSpace(r.Supplier) ? "Arrow" : r.Supplier,
                    Country = r.Country,
                    UnitPrice = price,
                    Currency = "USD",
                    QtyAvailable = r.Quantity,
                    Link = r.SupplierLink,
                    Description = r.Description,
                    Manufacturer = r.Manufacturer,
                    PriceTiers = usdTiers,
                    IsCustomPrice = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arrow extension search failed for '{Sku}'", sku);
        }
        return result;
    }

    // =============================================
    // ===== Master Electronics via browser extension (user session) =====
    // =============================================

    // חיפוש Master Electronics דרך תוסף הדפדפן: בונים URL חיפוש למק"ט, מכניסים job לתור,
    // והתוסף (בסשן המחובר של המשתמש) פותח את הדף וקורא את טבלת מחירי החוזה (.price-breakdown).
    // מסומן IsCustomPrice=true כי המחיר הוא מחיר החשבון של Comvia. בכישלון/timeout/לא-מחובר —
    // מחזיר ריק והשרת נשאר עם מחיר ה-NC (כמו Arrow/Farnell).
    private async Task<List<SupplierResult>> SearchMasterElectronicsViaExtensionAsync(string sku, int qty)
    {
        var result = new List<SupplierResult>();
        if (!_masterEnabled) return result;
        if (string.IsNullOrWhiteSpace(sku)) return result;

        try
        {
            var url = MasterElectronicsUrl.Build(sku);
            _logger.LogInformation("Master Electronics via extension: '{Sku}' -> {Url}", sku, url);

            var rows = await _browserQueue.RequestSearchAsync("Master Electronics", sku, url, qty, _masterTimeout);
            if (rows == null)
            {
                _logger.LogWarning("Master Electronics extension returned null (offline/timeout/blocked/not-logged-in) for '{Sku}'", sku);
                return result;
            }

            _logger.LogInformation("Master Electronics extension returned {Count} rows for '{Sku}'", rows.Count, sku);
            foreach (var r in rows)
            {
                var usdTiers = _currency.ToUsdTiers(r.Prices);
                double? price = GetPriceForQty(usdTiers, qty);
                result.Add(new SupplierResult
                {
                    Name = string.IsNullOrWhiteSpace(r.Supplier) ? "Master Electronics" : r.Supplier,
                    Country = r.Country,
                    UnitPrice = price,
                    Currency = price != null ? "USD" : null,
                    QtyAvailable = r.Quantity,
                    Link = r.SupplierLink,
                    Description = r.Description,
                    Manufacturer = r.Manufacturer,
                    PriceTiers = usdTiers,
                    // מחיר ה-price-breakdown הוא מחיר החשבון של Comvia (סשן מחובר) — מחיר מותאם.
                    IsCustomPrice = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Master Electronics extension search failed for '{Sku}'", sku);
        }
        return result;
    }

    // =============================================
    // ===== Waldom API =====
    // =============================================

    // אבחון: מחזיר את הסטטוס וה-body הגולמי מ-Waldom (המפתח מוסתר ב-URL המוחזר) + הספקים
    // שחולצו. שימושי לאימות שה-BaseUrl/המפתח/מיפוי השדות נכונים, בלי כל זרימת ההצעה.
    public async Task<object> WaldomDebugAsync(string sku, int qty)
    {
        var term = Uri.EscapeDataString((sku ?? "").Trim());
        var tail = $"InventoryAndPricing/{term}/{_waldomInStock}/{_waldomExact}/{_waldomCount}";
        var maskedUrl = $"{_waldomBaseUrl.TrimEnd('/')}/api/v1/***/{tail}";
        if (!_waldomEnabled)
            return new { enabled = false, url = maskedUrl, note = "Waldom כבוי או חסר ApiKey ב-appsettings" };

        string status = "?", body = "";
        try
        {
            var url = $"{_waldomBaseUrl.TrimEnd('/')}/api/v1/{_waldomApiKey}/{tail}";
            using var reqM = new HttpRequestMessage(HttpMethod.Get, url);
            reqM.Headers.TryAddWithoutValidation("User-Agent", "ComviaServer/1.0");
            reqM.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain");
            var r = await _httpClient.SendAsync(reqM);
            status = ((int)r.StatusCode).ToString();
            body = await r.Content.ReadAsStringAsync();
            if (body.Length > 4000) body = body.Substring(0, 4000) + "...(truncated)";
        }
        catch (Exception ex) { body = "EXCEPTION: " + ex.Message; }

        var parsed = await SearchWaldomAsync(sku, qty);
        return new { enabled = true, url = maskedUrl, status, bodyPreview = body, parsedCount = parsed.Count, parsed };
    }

    // חיפוש Waldom (master distributor) דרך ה-API הציבורי שלהם. בלי דפדפן/תוסף — קריאת HTTP
    // ישירה. המפתח עובר ב-PATH (לא header):
    //   GET {base}/api/v1/{ApiKey}/InventoryAndPricing/{Term}/{InStockOnly}/{ExactMatch}/{ResultsCount}
    // התשובה: products[] עם pricing.priceBreaks[] ({priceBreakQuantity, price}) + totalStockQuantity.
    // המחירים מומרים ל-USD. Term = המק"ט המקורי (עם מקפים).
    private async Task<List<SupplierResult>> SearchWaldomAsync(string sku, int qty)
    {
        var outp = new List<SupplierResult>();
        if (!_waldomEnabled || string.IsNullOrWhiteSpace(sku)) return outp;

        try
        {
            var term = Uri.EscapeDataString(sku.Trim());
            // הפרמטרים מ-appsettings (InStockOnly/ExactMatch/ResultsCount). ברירת מחדל: 0/0/5.
            var url = $"{_waldomBaseUrl.TrimEnd('/')}/api/v1/{_waldomApiKey}/InventoryAndPricing/{term}/{_waldomInStock}/{_waldomExact}/{_waldomCount}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "ComviaServer/1.0");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain");

            var resp = await _httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Waldom returned {Status} for '{Sku}'", (int)resp.StatusCode, sku);
                return outp;
            }

            var json = await resp.Content.ReadAsStringAsync();
            // Waldom מחזיר את שדות המוצר ב-PascalCase ("PartNumber"/"Pricing"/...) — חובה
            // deserialization case-insensitive, אחרת השדות לא נקראים.
            var data = JsonSerializer.Deserialize<WaldomResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data?.Products == null) return outp;

            foreach (var p in data.Products)
            {
                var currency = p.Pricing?.Currency ?? "USD";
                var tiers = (p.Pricing?.PriceBreaks ?? new List<WaldomBreak>())
                    .Select(b => new PriceTier { Price = b.Price, MinQty = b.PriceBreakQuantity.ToString(), Currency = currency })
                    .ToList();

                var usdTiers = _currency.ToUsdTiers(tiers);
                double? unit = GetPriceForQty(usdTiers, qty);
                var region = p.AvailableInventory?.FirstOrDefault()?.ShipsFromRegion ?? "";

                outp.Add(new SupplierResult
                {
                    Name = "Waldom",
                    Country = region,
                    UnitPrice = unit,
                    Currency = unit != null ? "USD" : null,
                    QtyAvailable = p.TotalStockQuantity > 0 ? p.TotalStockQuantity.ToString() : "",
                    Link = p.DataSheetLink ?? "",
                    Description = p.Description ?? "",
                    Manufacturer = p.ManufacturerName ?? "",
                    PriceTiers = usdTiers,
                    // מחיר ה-API של Waldom הוא מחיר החשבון של Comvia (דרך המפתח) — נחשב "מחיר מותאם".
                    IsCustomPrice = true
                });
            }

            _logger.LogInformation("Waldom returned {Count} products for '{Sku}'", outp.Count, sku);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Waldom search failed for '{Sku}'", sku);
        }
        return outp;
    }

    // =============================================
    // ===== DigiKey API =====
    // =============================================

    private async Task<List<DigiKeyResult>> SearchDigiKeyAsync(string keyword)
    {
        try
        {
            var token = await GetDigiKeyTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, DK_SEARCH_URL);
            request.Headers.Add("X-DIGIKEY-Client-Id", DK_CLIENT_ID);
            request.Headers.Add("X-DIGIKEY-Locale-Language", "en");
            request.Headers.Add("X-DIGIKEY-Locale-Currency", "usd");
            request.Headers.Add("X-DIGIKEY-Locale-Site", "IL");
            request.Headers.Add("Authorization", $"Bearer {token}");

            var body = JsonSerializer.Serialize(new { Keywords = keyword, Limit = 10, Offset = 0 });
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var results = new List<DigiKeyResult>();
            foreach (var product in doc.RootElement.GetProperty("Products").EnumerateArray())
            {
                var prices = new List<PriceTier>();

                // DigiKey API v4: המחירים נמצאים תחת ProductVariations[].StandardPricing (לא על המוצר).
                // למוצר יש כמה variations (Cut Tape / Tape&Reel / Digi-Reel...) — כל אחד עם סולם
                // מחירים ו-MOQ משלו. בוחרים את ה-variation עם הכי הרבה מדרגות (הסולם המלא/הייצוגי).
                if (product.TryGetProperty("ProductVariations", out var variations) &&
                    variations.ValueKind == JsonValueKind.Array)
                {
                    JsonElement bestPricing = default;
                    int bestCount = 0;
                    foreach (var v in variations.EnumerateArray())
                    {
                        if (v.TryGetProperty("StandardPricing", out var sp) && sp.ValueKind == JsonValueKind.Array
                            && sp.GetArrayLength() > bestCount)
                        {
                            bestCount = sp.GetArrayLength();
                            bestPricing = sp;
                        }
                    }
                    if (bestCount > 0)
                        AddDkTiers(bestPricing, prices);
                }

                // תאימות לאחור: מבנה v3 הישן (StandardPricing על המוצר).
                if (prices.Count == 0 && product.TryGetProperty("StandardPricing", out var legacyPricing)
                    && legacyPricing.ValueKind == JsonValueKind.Array)
                    AddDkTiers(legacyPricing, prices);

                // fallback: מחיר יחיד ברמת המוצר (UnitPrice) אם אין סולם מדרגות כלל.
                if (prices.Count == 0 && product.TryGetProperty("UnitPrice", out var pu)
                    && pu.ValueKind == JsonValueKind.Number && pu.GetDouble() > 0)
                    prices.Add(new PriceTier { Price = pu.GetDouble(), MinQty = "1", Currency = "USD" });

                var desc = "";
                if (product.TryGetProperty("Description", out var descObj) &&
                    descObj.TryGetProperty("ProductDescription", out var pd))
                    desc = pd.GetString() ?? "";

                var mfr = "";
                if (product.TryGetProperty("Manufacturer", out var mfrObj) &&
                    mfrObj.TryGetProperty("Name", out var mn))
                    mfr = mn.GetString() ?? "";

                results.Add(new DigiKeyResult
                {
                    Manufacturer = mfr,
                    Description = desc,
                    Prices = prices,
                    QtyAvailable = product.TryGetProperty("QuantityAvailable", out var qa) && qa.TryGetInt32(out var qav) ? qav : 0
                });
            }

            _logger.LogInformation("DigiKey '{Keyword}': {Products} products, price-tiers per product: [{Tiers}]",
                keyword, results.Count, string.Join(", ", results.Select(r => r.Prices.Count)));
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DigiKey search error for '{Keyword}'", keyword);
            return new List<DigiKeyResult>();
        }
    }

    // ממיר מערך StandardPricing של DigiKey (PriceBreak: BreakQuantity/UnitPrice) למדרגות שלנו.
    private static void AddDkTiers(JsonElement standardPricing, List<PriceTier> into)
    {
        foreach (var tier in standardPricing.EnumerateArray())
        {
            if (tier.TryGetProperty("UnitPrice", out var up) && up.ValueKind == JsonValueKind.Number
                && tier.TryGetProperty("BreakQuantity", out var bq))
            {
                double price = up.GetDouble();
                if (price <= 0) continue;
                int minQty = bq.TryGetInt32(out var q) ? q : 1;
                into.Add(new PriceTier { Price = price, MinQty = minQty.ToString(), Currency = "USD" });
            }
        }
    }

    private async Task<string> GetDigiKeyTokenAsync()
    {
        if (_dkToken != null && DateTime.UtcNow < _dkTokenExpiry)
            return _dkToken;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = DK_CLIENT_ID,
            ["client_secret"] = DK_CLIENT_SECRET
        });

        var response = await _httpClient.PostAsync(DK_TOKEN_URL, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        _dkToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
        _dkTokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 50);

        _logger.LogInformation("DigiKey token refreshed");
        return _dkToken;
    }

    // =============================================
    // ===== Internal DTOs =====
    // =============================================

    private class NetComponentsResult
    {
        public string PartNumber { get; set; } = "";
        public string Manufacturer { get; set; } = "";
        public string Description { get; set; } = "";
        public string Country { get; set; } = "";
        public string Quantity { get; set; } = "";
        public List<PriceTier> Prices { get; set; } = new();
        public string Supplier { get; set; } = "";
        public string SupplierLink { get; set; } = "";
    }

    private class DigiKeyResult
    {
        public string Manufacturer { get; set; } = "";
        public string Description { get; set; } = "";
        public List<PriceTier> Prices { get; set; } = new();
        public int QtyAvailable { get; set; }
    }

    // ===== Waldom API DTOs (נקראים case-insensitive; Waldom מחזיר PascalCase) =====
    private class WaldomResponse
    {
        public List<WaldomProduct>? Products { get; set; }
        public int TotalCount { get; set; }
    }
    private class WaldomProduct
    {
        public string? PartNumber { get; set; }
        public string? ManufacturerName { get; set; }
        public string? Description { get; set; }
        public int TotalStockQuantity { get; set; }
        public List<WaldomInventory>? AvailableInventory { get; set; }
        public string? DataSheetLink { get; set; }
        public WaldomPricing? Pricing { get; set; }
    }
    private class WaldomInventory
    {
        public string? ShipsFromRegion { get; set; }
        public string? ShipsFromWarehouse { get; set; }
    }
    private class WaldomPricing
    {
        public string? Currency { get; set; }
        public List<WaldomBreak>? PriceBreaks { get; set; }
    }
    private class WaldomBreak
    {
        public int PriceBreakQuantity { get; set; }
        public double Price { get; set; }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
