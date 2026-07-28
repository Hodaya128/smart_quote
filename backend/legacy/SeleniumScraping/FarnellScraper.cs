using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace comviaServer.Model;

// Scraper לאתר Farnell. הסלקטורים מבוססים על ה-DOM האמיתי של il.farnell.com.
// ⚠️ ל-Farnell ייתכן הגנת anti-bot/Cloudflare/Captcha שתחסום Selenium — סיכון אמיתי.
public class FarnellScraper : ISupplierLoginScraper
{
    // NetComponents נותן קישור לדומיין UK; מנווטים לדומיין הישראלי שבו מוצג מחיר החוזה שלנו.
    private const string LoginUrl = "https://il.farnell.com/auth/login";

    private readonly ILogger<FarnellScraper> _logger;

    public FarnellScraper(ILogger<FarnellScraper> logger)
    {
        _logger = logger;
    }

    public string SupplierName => "Farnell";

    // ממיר כל דומיין של farnell.com (למשל uk.farnell.com) ל-il.farnell.com, שם מוצג מחיר החוזה שלנו.
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
            // fallback אם ה-URL לא תקין כ-Uri
            return url.Replace("uk.farnell.com", "il.farnell.com")
                      .Replace("www.farnell.com", "il.farnell.com");
        }
    }

    public void EnsureLoggedIn(IWebDriver driver, (string account, string user, string pass) creds)
    {
        _logger.LogInformation("Farnell: navigating to login page");
        driver.Navigate().GoToUrl(LoginUrl);

        // שדה משתמש/אימייל. הערה: ה-id מכיל נקודות ולכן משתמשים ב-data-testid/name (לא CSS #id).
        var userField = ScraperUtils.WaitForFirst(driver, 20,
            By.CssSelector("input[data-testid='authentication.login.form__logon-id-input']"),
            By.CssSelector("input[name='logonId']"));

        if (userField == null)
            throw new InvalidOperationException("Farnell: logon-id field not found (possible anti-bot/Captcha block)");

        var passField = ScraperUtils.TryFindFirst(driver,
            By.CssSelector("input[data-testid='authentication.login.form__password-input']"),
            By.CssSelector("input[name='password']"));

        if (passField == null)
            throw new InvalidOperationException("Farnell: password field not found on login page");

        userField.Clear();
        userField.SendKeys(creds.user);
        passField.Clear();
        passField.SendKeys(creds.pass);

        var submit = ScraperUtils.TryFindFirst(driver,
            By.CssSelector("button[data-testid='authentication.login.form__submit-button']"),
            By.CssSelector("button[type='submit'][title='Log In']"),
            By.CssSelector("button[type='submit']"));

        if (submit == null)
            throw new InvalidOperationException("Farnell: submit button not found on login page");

        submit.Click();

        // לאחר התחברות מוצלחת האתר מנתב למסך הבית (עוזב את /auth/login).
        try
        {
            new WebDriverWait(driver, TimeSpan.FromSeconds(20))
                .Until(d => !d.Url.Contains("/auth/login"));
            _logger.LogInformation("Farnell login successful");
        }
        catch
        {
            _logger.LogWarning("Farnell: still on login page after submit (login may have failed)");
        }
    }

    public double? GetPrice(IWebDriver driver, string productUrl, int qty)
    {
        var url = ToIsraelFarnell(productUrl);
        _logger.LogInformation("Farnell: fetching price from {Url} (qty {Qty})", url, qty);
        driver.Navigate().GoToUrl(url);

        // טבלת המחירים. שם המחלקה ב-styled-components כולל hash שעלול להשתנות — מתאימים לפי "PriceTable".
        var table = ScraperUtils.WaitForFirst(driver, 20,
            By.CssSelector("table[class*='PriceTable']"));

        if (table == null)
        {
            _logger.LogWarning("Farnell: price table not found on {Url}", url);
            return null;
        }

        // אוספים את שוברי הכמות עם מחיר החוזה ("Web only contract price") ומחיר הרשימה (strike-through).
        var tiers = new List<(int brk, double? contract, double? list)>();
        foreach (var row in table.FindElements(By.CssSelector("tbody tr")))
        {
            var cells = row.FindElements(By.TagName("td"));
            if (cells.Count == 0) continue; // שורת כותרת (th)

            var brk = (int)(ScraperUtils.ParsePrice(cells[0].Text) ?? 0); // "300+" => 300

            // מחיר החוזה המיוחד — העמודה השלישית.
            double? contract = null;
            var contractSpan = ScraperUtils.TryFindFirst(row, By.CssSelector("[class*='PriceWrapper']"));
            if (contractSpan != null) contract = ScraperUtils.ParsePrice(contractSpan.Text);

            // מחיר רשימה (fallback) — התא עם strike-through.
            double? list = null;
            var strike = ScraperUtils.TryFindFirst(row, By.CssSelector("td.strike-through"));
            if (strike != null) list = ScraperUtils.ParsePrice(strike.Text);

            tiers.Add((brk, contract, list));
        }

        if (tiers.Count == 0)
        {
            _logger.LogWarning("Farnell: no price rows parsed on {Url}", url);
            return null;
        }

        // בוחרים את שובר הכמות הגבוה ביותר שקטן/שווה ל-qty (אם qty קטן מהקטן ביותר — לוקחים את הראשון).
        var ordered = tiers.OrderBy(t => t.brk).ToList();
        var chosen = ordered[0];
        foreach (var t in ordered)
            if (t.brk <= qty) chosen = t;

        // עדיפות למחיר החוזה (המותאם); אם אין — מחיר הרשימה.
        var price = chosen.contract ?? chosen.list;
        _logger.LogInformation("Farnell: qty {Qty} -> break {Break}+, contract {Contract}, list {List} => {Price}",
            qty, chosen.brk, chosen.contract, chosen.list, price);
        return price;
    }
}
