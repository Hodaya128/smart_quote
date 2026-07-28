using comviaServer.DAL;
using comviaServer.Security;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
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

    private IWebDriver? _driver;
    private bool _loggedIn;
    private readonly object _seleniumLock = new();
    private readonly SemaphoreSlim _searchQueue = new(1, 1); // תור - חיפוש אחד בכל פעם

    // ספקים (scrapers) שכבר התחברנו אליהם ב-session הנוכחי, לפי שם מנורמל.
    private readonly HashSet<string> _loggedInSuppliers = new();
    // registry של scrapers לפי שם ספק מנורמל (lower/trim).
    private readonly Dictionary<string, ISupplierLoginScraper> _scrapers;

    private string? _dkToken;
    private DateTime _dkTokenExpiry = DateTime.MinValue;
    private readonly HttpClient _httpClient = new();
    private readonly ILogger<PriceComparisonService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // האם להריץ את Chrome ב-headless (חובה על שרת ללא דסקטופ אינטראקטיבי, כמו שרת רופין).
    // נשלט דרך appsettings.json: "Selenium": { "Headless": true }. ברירת מחדל: true (בטוח לשרת).
    private readonly bool _headless;

    // תור המחירים של Farnell — נשלף דרך תוסף הדפדפן בסשן המחובר של המשתמש (לא Selenium).
    private readonly FarnellJobQueue _farnellQueue;
    // כמה זמן להמתין לתוסף לפני שמוותרים ונשארים עם מחיר ה-NC. נשלט ב-appsettings: "Farnell:TimeoutSeconds".
    private readonly TimeSpan _farnellTimeout;

    // תור החיפושים של NetComponents — עבר מ-Selenium לתוסף הדפדפן (אותו רעיון כמו Farnell):
    // השרת מכניס job חיפוש וממתין; התוסף פותח את עמוד החיפוש בסשן המחובר ומחזיר את שורות הטבלה.
    private readonly BrowserSearchQueue _browserQueue;
    // כמה זמן להמתין לתוסף עבור חיפוש NetComponents (טבלה + מחירים ב-AJAX לוקחים יותר מ-Farnell).
    private readonly TimeSpan _ncTimeout;

    // כמה זמן להמתין לתוסף עבור חיפוש Arrow (React SPA — לוקח זמן להתרנדר).
    private readonly TimeSpan _arrowTimeout;

    // מתג הפעלה ל-Arrow. כבוי כרגע (Akamai חוסם בדפדפן הרגיל). כשמדליקים — להחזיר ל-true
    // ב-appsettings ("Arrow:Enabled"). כבוי => לא מוסיף timeout/עומס לחיפוש.
    private readonly bool _arrowEnabled;

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

    public PriceComparisonService(
        ILogger<PriceComparisonService> logger,
        IServiceScopeFactory scopeFactory,
        IEnumerable<ISupplierLoginScraper> scrapers,
        FarnellJobQueue farnellQueue,
        BrowserSearchQueue browserQueue,
        CurrencyService currency,
        IConfiguration config)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _scrapers = scrapers.ToDictionary(s => Normalize(s.SupplierName), s => s);
        _farnellQueue = farnellQueue;
        _browserQueue = browserQueue;
        _currency = currency;
        _headless = config.GetValue("Selenium:Headless", true);
        // ברירת מחדל 45ש': התוסף ממתין עד 30ש' לטעינת הטאב + ~25ש' polling. timeout קצר מדי
        // (היה 15) גרם לשרת לוותר לפני שהתוסף הספיק להחזיר מחיר — ולכן "לא נמשך מחיר מפרנל".
        _farnellTimeout = TimeSpan.FromSeconds(config.GetValue("Farnell:TimeoutSeconds", 45));
        _ncTimeout = TimeSpan.FromSeconds(config.GetValue("NetComponents:TimeoutSeconds", 60));
        _arrowTimeout = TimeSpan.FromSeconds(config.GetValue("Arrow:TimeoutSeconds", 60));
        _arrowEnabled = config.GetValue("Arrow:Enabled", false);
        _waldomApiKey = config["Waldom:ApiKey"] ?? "";
        _waldomBaseUrl = config["Waldom:BaseUrl"] ?? "https://www.waldom.com";
        _waldomEnabled = config.GetValue("Waldom:Enabled", true) && !string.IsNullOrWhiteSpace(_waldomApiKey);
        _waldomInStock = config.GetValue("Waldom:InStockOnly", 0);
        _waldomExact = config.GetValue("Waldom:ExactMatch", 1);
        _waldomCount = config.GetValue("Waldom:ResultsCount", 5);
    }

    private static string Normalize(string name) => (name ?? "").Trim().ToLowerInvariant();

    // מאתר scraper לפי שם ספק שמגיע מ-NetComponents. NC מחזיר שמות מלאים
    // ("Farnell, An Avnet Company") בעוד ה-scraper מוגדר בשם קצר ("Farnell"),
    // לכן ההשוואה היא בהכלה (substring) ולא רק שוויון מדויק.
    private ISupplierLoginScraper? FindScraper(string? supplierName)
    {
        var norm = Normalize(supplierName ?? "");
        if (norm.Length == 0) return null;
        if (_scrapers.TryGetValue(norm, out var exact)) return exact;
        foreach (var kv in _scrapers)
        {
            if (norm.Contains(kv.Key) || kv.Key.Contains(norm))
                return kv.Value;
        }
        return null;
    }

    // טוען את פרטי ההתחברות ל-NetComponents מה-DB (מפוענחים). מחזיר null אם לא הוגדרו.
    private (string account, string username, string password)? LoadNetComponentsCredentials()
        => LoadCredentials(NC_SUPPLIER_NAME);

    // טוען את פרטי ההתחברות של ספק כלשהו מה-DB (מפוענחים), לפי שם מנורמל.
    // מחזיר null אם הספק לא קיים, לא דורש התחברות, או חסרים פרטים.
    private (string account, string username, string password)? LoadCredentials(string supplierName)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dal = scope.ServiceProvider.GetRequiredService<DBServices>();
            var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();

            var target = Normalize(supplierName);
            var supplier = dal.GetAllSuppliers()
                .FirstOrDefault(s => Normalize(s.SupplierName) == target);

            if (supplier == null || !supplier.RequiresLogin) return null;

            var password = protector.Decrypt(supplier.LoginPassword);
            if (string.IsNullOrEmpty(supplier.LoginUsername) || string.IsNullOrEmpty(password))
                return null;

            return (supplier.LoginAccount ?? "", supplier.LoginUsername!, password!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load credentials for '{Supplier}' from DB", supplierName);
            return null;
        }
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

            // הוספת תוצאות Waldom (דרך API) — ספק נוסף, תמיד נבדק.
            suppliers.AddRange(await waldomTask);

            // העשרת מחירים מותאמים: לכל ספק עם מלאי שיש לנו אליו scraper + פרטי התחברות,
            // מתחברים, מנווטים לקישור הישיר מ-NC, ושולפים את המחיר המותאם (מחליף את מחיר ה-NC).
            EnrichWithCustomPrices(suppliers, item.Qty);

            // Farnell — דרך תוסף הדפדפן (סשן המשתמש), לא Selenium. אם התוסף לא זמין/timeout
            // נשאר מחיר ה-NC. נקרא לאחר ה-Selenium כדי לא לערב את הנעילה _seleniumLock.
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
    // ===== Custom price enrichment (login per supplier) =====
    // =============================================

    // לכל ספק ב-suppliers עם מלאי (>0), scraper תואם (שם מנורמל) ו-Link לא ריק:
    // מתחבר (פעם אחת ל-session) ושולף את המחיר המותאם מעמוד המוצר. בהצלחה — מחליף
    // את UnitPrice ומסמן IsCustomPrice. בכישלון — משאיר את מחיר ה-NC הקיים (לוג בלבד).
    private void EnrichWithCustomPrices(List<SupplierResult> suppliers, int requestedQty)
    {
        if (_scrapers.Count == 0) return;

        // מסננים מראש כדי לא לנעול את הסלניום אם אין מה לעשות.
        var targets = suppliers
            .Where(s => ParseNum(s.QtyAvailable) > 0
                        && !string.IsNullOrWhiteSpace(s.Link)
                        && FindScraper(s.Name) != null)
            .ToList();

        if (targets.Count == 0) return;

        lock (_seleniumLock)
        {
            foreach (var s in targets)
            {
                var scraper = FindScraper(s.Name)!;
                try
                {
                    var creds = LoadCredentials(scraper.SupplierName);
                    if (creds == null)
                    {
                        _logger.LogInformation("No credentials configured for '{Supplier}', skipping custom price", scraper.SupplierName);
                        continue;
                    }

                    EnsureDriver();

                    var key = Normalize(scraper.SupplierName);
                    if (!_loggedInSuppliers.Contains(key))
                    {
                        scraper.EnsureLoggedIn(_driver!, creds.Value);
                        _loggedInSuppliers.Add(key);
                    }

                    var customPrice = scraper.GetPrice(_driver!, s.Link, requestedQty);
                    if (customPrice.HasValue)
                    {
                        s.UnitPrice = customPrice.Value;
                        s.Currency ??= "USD";
                        s.IsCustomPrice = true;
                        _logger.LogInformation("Custom price for '{Supplier}': {Price}", scraper.SupplierName, customPrice.Value);
                    }
                    else
                    {
                        _logger.LogInformation("No custom price found for '{Supplier}', keeping NC price", scraper.SupplierName);
                    }
                }
                catch (Exception ex)
                {
                    // כישלון בספק אחד (כולל חסימת anti-bot) לא יפיל את כל החיפוש.
                    _loggedInSuppliers.Remove(Normalize(scraper.SupplierName));
                    _logger.LogWarning(ex, "Custom price enrichment failed for '{Supplier}'", scraper.SupplierName);
                }
            }
        }
    }

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

    // מוודא שיש דפדפן פעיל (משתף את אותו _driver של חיפוש NC).
    private void EnsureDriver()
    {
        if (_driver != null) return;

        _logger.LogInformation("Starting Chrome browser (for custom price scraping)...");
        _driver = CreateChromeDriver();
    }

    // יוצר ChromeDriver עם הפחתת זיהוי-אוטומציה (anti-bot). מסיר את הדגלים הברורים
    // (navigator.webdriver, enable-automation) — לא ערובה מול Akamai/Cloudflare אך מקטין חסימות.
    private ChromeDriver CreateChromeDriver()
    {
        var options = new ChromeOptions();
        options.AddArgument("--window-size=1920,1080");
        options.AddArgument("--user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        options.AddArgument("--disable-blink-features=AutomationControlled");
        options.AddExcludedArgument("enable-automation");
        options.AddAdditionalOption("useAutomationExtension", false);

        // דגלים נדרשים להרצה על שרת (ללא דסקטופ / הרשאות מוגבלות / זיכרון משותף קטן).
        options.AddArgument("--no-sandbox");
        options.AddArgument("--disable-dev-shm-usage");
        options.AddArgument("--disable-gpu");

        // תיקיית פרופיל ייחודית ב-temp — מונע כשל נפוץ תחת IIS app pool
        // ("user data directory is already in use" / אין פרופיל משתמש לזהות ה-app pool).
        var profileDir = Path.Combine(Path.GetTempPath(), "comvia-chrome-" + Guid.NewGuid().ToString("N"));
        options.AddArgument($"--user-data-dir={profileDir}");
        if (_headless)
        {
            // headless חדש (Chrome 109+) — נראה כמו דפדפן רגיל, נדרש על שרת ללא מסך.
            options.AddArgument("--headless=new");
            _logger.LogInformation("Chrome running in HEADLESS mode (server)");
        }

        var driver = new ChromeDriver(options);
        try
        {
            // מסתיר את navigator.webdriver לפני טעינת כל דף.
            driver.ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument",
                new Dictionary<string, object>
                {
                    ["source"] = "Object.defineProperty(navigator,'webdriver',{get:()=>undefined});"
                });
        }
        catch { /* CDP לא זמין — לא קריטי */ }
        return driver;
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

    // חיפוש Arrow דרך תוסף הדפדפן. Arrow הוא React SPA עם הגנת Akamai — לא ניתן fetch מהשרת,
    // לכן התוסף פותח את עמוד החיפוש בדפדפן, ממתין שה-SPA יתרנדר, וקורא את התוצאות.
    // כל שורה ממופה ל-SupplierResult (שם הספק "Arrow"), המחירים מומרים ל-USD. Arrow אינו
    // דורש התחברות (מחיר מחירון ציבורי) ולכן IsCustomPrice=false.
    private async Task<List<SupplierResult>> SearchArrowViaExtensionAsync(string sku, int qty)
    {
        var result = new List<SupplierResult>();
        // Arrow כבוי כרגע (חסימת Akamai בדפדפן הרגיל). מחזירים מיד כדי לא לעכב את החיפוש.
        if (!_arrowEnabled) return result;
        if (string.IsNullOrWhiteSpace(sku)) return result;

        try
        {
            var url = ArrowUrl.Build(sku);
            _logger.LogInformation("Arrow via extension: '{Sku}' -> {Url}", sku, url);

            var rows = await _browserQueue.RequestSearchAsync("Arrow", sku, url, qty, _arrowTimeout);
            if (rows == null)
            {
                _logger.LogWarning("Arrow extension returned null (offline/timeout/blocked) for '{Sku}'", sku);
                return result;
            }

            _logger.LogInformation("Arrow extension returned {Count} rows for '{Sku}'", rows.Count, sku);
            foreach (var r in rows)
            {
                var usdTiers = _currency.ToUsdTiers(r.Prices);
                double? price = GetPriceForQty(usdTiers, qty);
                result.Add(new SupplierResult
                {
                    Name = string.IsNullOrWhiteSpace(r.Supplier) ? "Arrow" : r.Supplier,
                    Country = r.Country,
                    UnitPrice = price,
                    Currency = price != null ? "USD" : null,
                    QtyAvailable = r.Quantity,
                    Link = r.SupplierLink,
                    Description = r.Description,
                    Manufacturer = r.Manufacturer,
                    PriceTiers = usdTiers,
                    IsCustomPrice = false
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
    // ===== NetComponents (Selenium) — LEGACY =====
    // =============================================
    // ⚠️ קוד ישן: NetComponents עבר לתוסף הדפדפן (SearchNetComponentsViaExtensionAsync למעלה).
    // המתודות הבאות כבר אינן בשימוש בזרימה הראשית ונשמרות רק כגיבוי/הפניה. אפשר למחוק
    // בעתיד יחד עם תשתית ה-ChromeDriver אם גם Master Electronics יעבור לתוסף.

    //private List<NetComponentsResult> SearchNetComponents(string partNumber)
    //{
    //    lock (_seleniumLock)
    //    {

    //        catch (Exception ex)
    //        {
    //            _logger.LogError(ex, "NetComponents search failed for '{Part}', retrying with fresh login", partNumber);
    //            try
    //            {
    //                LoginNetComponents();
    //                return DoNetComponentsSearch(partNumber);
    //            }
    //            catch (Exception ex2)
    //            {
    //                _logger.LogError(ex2, "NetComponents search failed after re-login for '{Part}'", partNumber);
    //                return new List<NetComponentsResult>();
    //            }
    //        }
    //    }
    //}

    private List<NetComponentsResult> SearchNetComponents(string partNumber)
    {
        lock (_seleniumLock)
        {
            try
            {
                partNumber = NormalizeSku(partNumber);

                _logger.LogInformation(
                    "Starting NetComponents search for: {Part}",
                    partNumber);

                EnsureLoggedIn();

                var results = DoNetComponentsSearch(partNumber);

                _logger.LogInformation(
                    "NetComponents found {Count} results for: {Part}",
                    results.Count,
                    partNumber);

                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NetComponents search failed for '{Part}', retrying with fresh login", partNumber);

                try
                {
                    LoginNetComponents();
                    return DoNetComponentsSearch(partNumber);
                }
                catch (Exception ex2)
                {
                    _logger.LogError(ex2, "NetComponents search failed after re-login for '{Part}'", partNumber);
                    return new List<NetComponentsResult>();
                }
            }
        }
    }


    private void EnsureLoggedIn()
    {
        if (_loggedIn && _driver != null) return;

        _logger.LogInformation("Starting Chrome browser...");
        _driver = CreateChromeDriver();
        _logger.LogInformation("Chrome browser started successfully");
        LoginNetComponents();
    }

    private void LoginNetComponents()
    {
        var driver = _driver!;
        driver.Navigate().GoToUrl("https://www.netcomponents.com");

        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

        // זיהוי מצב מחובר לפי קישור ה-Login: כשמחוברים הקישור נעלם.
        // חשוב: אסור לזהות לפי תיבת החיפוש (PartsSearched_0__PartNumber) כי היא מופיעה
        // גם בדף הבית הציבורי לפני התחברות — וזה גרם לדילוג על ההתחברות ולחיפוש כאורח.
        IWebElement? loginLink = null;
        try
        {
            loginLink = new WebDriverWait(driver, TimeSpan.FromSeconds(8))
                .Until(d =>
                {
                    var links = d.FindElements(By.CssSelector("a.login-link"));
                    return links.Count > 0 ? links[0] : null;
                });
        }
        catch { /* קישור Login לא הופיע — כנראה כבר מחוברים */ }

        if (loginLink == null)
        {
            _loggedIn = true;
            _logger.LogInformation("NetComponents: login link absent, assuming already logged in");
            return;
        }

        // לא מחוברים — לוחצים על קישור ה-Login כדי לפתוח את טופס ההתחברות.
        loginLink.Click();
        Thread.Sleep(2000);

        wait.Until(d =>
        {
            try { return d.FindElement(By.Id("AccountNumber")).Displayed; }
            catch { return false; }
        });

        var creds = LoadNetComponentsCredentials();
        var ncAccount = creds?.account ?? NC_ACCOUNT;
        var ncUsername = creds?.username ?? NC_USERNAME;
        var ncPassword = creds?.password ?? NC_PASSWORD;

        var account = driver.FindElement(By.Id("AccountNumber"));
        account.Clear(); account.SendKeys(ncAccount);

        var user = driver.FindElement(By.Id("UserName"));
        user.Clear(); user.SendKeys(ncUsername);

        var pass = driver.FindElement(By.Id("Password"));
        pass.Clear(); pass.SendKeys(ncPassword);

        driver.FindElement(By.CssSelector("input.login-button")).Click();

        wait.Until(d => d.FindElement(By.Id("PartsSearched_0__PartNumber")));
        _loggedIn = true;
        _logger.LogInformation("NetComponents login successful");
    }

    private List<NetComponentsResult> DoNetComponentsSearch(string partNumber)
    {
        var driver = _driver!;
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));

        var searchBox = wait.Until(d => d.FindElement(By.Id("PartsSearched_0__PartNumber")));
        searchBox.Clear();
        searchBox.SendKeys(partNumber);

        // After first search the button ID changes
        try
        {
            driver.FindElement(By.Id("btnSearch")).Click();
        }
        catch
        {
            driver.FindElement(By.Id("SearchButton_0")).Click();
        }

        // Wait for results table or "no results" message
        new WebDriverWait(driver, TimeSpan.FromSeconds(30))
            .Until(d =>
            {
                try
                {
                    var table = d.FindElements(By.CssSelector("table.searchresultstable"));
                    if (table.Count > 0) return true;
                    // Check for "no results" indicators
                    var noResults = d.FindElements(By.CssSelector(".no-results, .search-no-results, #noResultsMessage"));
                    if (noResults.Count > 0) return true;
                    return false;
                }
                catch { return false; }
            });
        Thread.Sleep(5000);

        // Wait for price data to load (prices load via AJAX after the table)
        try
        {
            new WebDriverWait(driver, TimeSpan.FromSeconds(10))
                .Until(d => d.FindElements(By.CssSelector("a.ncprc")).Count > 0);
        }
        catch { }
        Thread.Sleep(2000);

        return ExtractNetComponentsResults();
    }

    private List<NetComponentsResult> ExtractNetComponentsResults()
    {
        var results = new List<NetComponentsResult>();
        var rows = _driver!.FindElements(By.CssSelector("table.searchresultstable tbody tr[id^='trv_']"));

        foreach (var row in rows)
        {
            try
            {
                var cols = row.FindElements(By.TagName("td"));
                if (cols.Count < 16) continue;

                var partNumber = cols[0].Text.Trim();
                var manufacturer = cols[3].Text.Trim();

                string description;
                try
                {
                    var descDiv = cols[5].FindElement(By.TagName("div"));
                    description = descDiv.GetAttribute("data-original-title") ?? descDiv.Text;
                }
                catch { description = ""; }

                var country = cols[7].Text.Trim();
                var quantity = cols[8].Text.Trim();

                string supplierName = "";
                try { supplierName = cols[15].FindElement(By.CssSelector("a.supname")).Text.Trim(); } catch { }

                var prices = new List<PriceTier>();
                try
                {
                    var priceElements = cols[9].FindElements(By.CssSelector("a.ncprc"));
                    if (priceElements.Count > 0)
                    {
                        var priceEl = priceElements[0];
                        // Try getting data-pbrk via JavaScript for reliability
                        var js = (IJavaScriptExecutor)_driver!;
                        var priceData = js.ExecuteScript("return arguments[0].getAttribute('data-pbrk');", priceEl)?.ToString();

                        _logger.LogInformation("Price data for {Supplier}: {Data}", supplierName ?? "?", priceData ?? "NULL");

                        if (!string.IsNullOrEmpty(priceData))
                        {
                            using var doc = JsonDocument.Parse(priceData);
                            var currency = doc.RootElement.GetProperty("currency").GetString() ?? "";
                            foreach (var p in doc.RootElement.GetProperty("Prices").EnumerateArray())
                            {
                                double priceVal = p.GetProperty("price").ValueKind == JsonValueKind.String
                                    ? double.Parse(p.GetProperty("price").GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                                    : p.GetProperty("price").GetDouble();
                                string minQtyVal = p.GetProperty("minQty").ValueKind == JsonValueKind.String
                                    ? p.GetProperty("minQty").GetString()!
                                    : p.GetProperty("minQty").GetRawText();

                                prices.Add(new PriceTier
                                {
                                    Price = priceVal,
                                    MinQty = minQtyVal,
                                    Currency = currency
                                });
                            }
                        }
                    }
                }
                catch (Exception priceEx)
                {
                    _logger.LogWarning(priceEx, "Error parsing prices for row");
                }

                string supplierLink = "";
                try { supplierLink = cols[1].FindElement(By.CssSelector("a.nctd")).GetAttribute("data-url") ?? ""; } catch { }

                results.Add(new NetComponentsResult
                {
                    PartNumber = partNumber,
                    Manufacturer = manufacturer,
                    Description = description.Trim(),
                    Country = country,
                    Quantity = quantity,
                    Prices = prices,
                    Supplier = supplierName,
                    SupplierLink = supplierLink
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting NetComponents row");
            }
        }

        return results;
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
                if (product.TryGetProperty("StandardPricing", out var pricingArr))
                {
                    foreach (var tier in pricingArr.EnumerateArray())
                    {
                        prices.Add(new PriceTier
                        {
                            Price = tier.GetProperty("UnitPrice").GetDouble(),
                            MinQty = tier.GetProperty("BreakQuantity").ToString(),
                            Currency = "USD"
                        });
                    }
                }

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
                    QtyAvailable = product.TryGetProperty("QuantityAvailable", out var qa) ? qa.GetInt32() : 0
                });
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DigiKey search error for '{Keyword}'", keyword);
            return new List<DigiKeyResult>();
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
        _driver?.Quit();
        _driver = null;
        _httpClient.Dispose();
    }
}
