using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace comviaServer.Model;

// Scraper לאתר Master Electronics (masterelectronics.com).
// ⚠️ הסלקטורים נכתבו כ"מועמדים" (best-effort) ועשויים לדרוש התאמה מול האתר החי.
// כל שלב מנסה כמה סלקטורים ומתעד בלוג מה נמצא/נכשל, כדי שיהיה קל לכוונן.
public class MasterElectronicsScraper : ISupplierLoginScraper
{
    private const string LoginUrl = "https://www.masterelectronics.com/en/login";

    private readonly ILogger<MasterElectronicsScraper> _logger;

    public MasterElectronicsScraper(ILogger<MasterElectronicsScraper> logger)
    {
        _logger = logger;
    }

    public string SupplierName => "Master Electronics";

    public void EnsureLoggedIn(IWebDriver driver, (string account, string user, string pass) creds)
    {
        _logger.LogInformation("MasterElectronics: navigating to login page");
        driver.Navigate().GoToUrl(LoginUrl);

        var userField = ScraperUtils.WaitForFirst(driver, 15,
            By.CssSelector("input[type='email']"),
            By.CssSelector("input[name='email']"),
            By.CssSelector("input[name='username']"),
            By.Id("email"),
            By.Id("username"));

        if (userField == null)
            throw new InvalidOperationException("MasterElectronics: username/email field not found on login page");

        var passField = ScraperUtils.TryFindFirst(driver,
            By.CssSelector("input[type='password']"),
            By.CssSelector("input[name='password']"),
            By.Id("password"));

        if (passField == null)
            throw new InvalidOperationException("MasterElectronics: password field not found on login page");

        userField.Clear();
        userField.SendKeys(creds.user);
        passField.Clear();
        passField.SendKeys(creds.pass);

        var submit = ScraperUtils.TryFindFirst(driver,
            By.CssSelector("button[type='submit']"),
            By.CssSelector("input[type='submit']"),
            By.CssSelector("button[name='login']"),
            By.CssSelector(".login-button, .btn-login"));

        if (submit == null)
            throw new InvalidOperationException("MasterElectronics: submit button not found on login page");

        submit.Click();

        // ממתינים לעזיבת עמוד ההתחברות (היעלמות שדה הסיסמה) כסימן להצלחה.
        try
        {
            new WebDriverWait(driver, TimeSpan.FromSeconds(15))
                .Until(d => ScraperUtils.TryFindFirst(d, By.CssSelector("input[type='password']")) == null);
        }
        catch
        {
            _logger.LogWarning("MasterElectronics: login may not have completed (password field still present)");
        }

        _logger.LogInformation("MasterElectronics login submitted");
    }

    public double? GetPrice(IWebDriver driver, string productUrl, int qty)
    {
        _logger.LogInformation("MasterElectronics: fetching price from {Url} (qty {Qty})", productUrl, qty);
        driver.Navigate().GoToUrl(productUrl);

        var priceEl = ScraperUtils.WaitForFirst(driver, 15,
            By.CssSelector("span[itemprop='price']"),
            By.CssSelector("[data-price]"),
            By.CssSelector(".product-price, .price-now, .your-price, .unit-price"),
            By.CssSelector("[class*='price']"));

        if (priceEl == null)
        {
            _logger.LogWarning("MasterElectronics: no price element found on {Url}", productUrl);
            return null;
        }

        // עדיפות ל-attribute data-price אם קיים, אחרת טקסט.
        var raw = priceEl.GetAttribute("data-price");
        if (string.IsNullOrWhiteSpace(raw)) raw = priceEl.Text;

        var price = ScraperUtils.ParsePrice(raw);
        _logger.LogInformation("MasterElectronics: parsed price {Price} from '{Raw}'", price, raw);
        return price;
    }
}
