using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System.Globalization;
using System.Text.RegularExpressions;

namespace comviaServer.Model;

// "קוד נפרד לכל ספק" — לכל אתר ספק שדורש התחברות יש מימוש משלו של הממשק הזה.
// PriceComparisonService מנהל registry של המימושים לפי SupplierName, ומפעיל אותם
// על אותו _driver שמסונכרן ב-_seleniumLock.
public interface ISupplierLoginScraper
{
    // שם הספק כפי שמופיע בטבלת Suppliers וב-NetComponents (להשוואה מנורמלת).
    string SupplierName { get; }

    // מבצע התחברות לאתר הספק. נקרא רק פעם אחת לכל session (PriceComparisonService
    // שומר אילו ספקים כבר מחוברים). זורק חריגה אם ההתחברות נכשלה.
    void EnsureLoggedIn(IWebDriver driver, (string account, string user, string pass) creds);

    // מנווט לעמוד המוצר (productUrl — הקישור הישיר מ-NetComponents) ושולף את מחיר
    // היחידה המותאם לכמות qty. מחזיר null אם לא נמצא מחיר.
    double? GetPrice(IWebDriver driver, string productUrl, int qty);
}

// כלי עזר משותפים לכל ה-scrapers.
public static class ScraperUtils
{
    // מנסה למצוא את האלמנט הראשון מתוך רשימת סלקטורים מועמדים. מחזיר null אם אף אחד לא נמצא.
    public static IWebElement? TryFindFirst(ISearchContext ctx, params By[] selectors)
    {
        foreach (var by in selectors)
        {
            try
            {
                var el = ctx.FindElement(by);
                if (el != null) return el;
            }
            catch { }
        }
        return null;
    }

    // ממתין עד שאחד מהסלקטורים מופיע (או timeout). מחזיר את האלמנט או null.
    public static IWebElement? WaitForFirst(IWebDriver driver, int seconds, params By[] selectors)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(seconds));
        try
        {
            return wait.Until(d => TryFindFirst(d, selectors));
        }
        catch
        {
            return null;
        }
    }

    // מחלץ מספר מחיר ממחרוזת טקסט (למשל "$1.234", "1,234.56 ₪", "US$ 0.95").
    public static double? ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // תופס את המספר הראשון בפורמט 1,234.56 / 1234.56 / 0.95
        var m = Regex.Match(text, @"\d{1,3}(?:[,\s]\d{3})*(?:\.\d+)?|\d+\.\d+|\d+");
        if (!m.Success) return null;

        var cleaned = m.Value.Replace(",", "").Replace(" ", "");
        if (double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            return val;
        return null;
    }
}
