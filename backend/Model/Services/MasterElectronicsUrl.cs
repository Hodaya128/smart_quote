namespace comviaServer.Model;

// בונה את כתובת החיפוש של Master Electronics (masterelectronics.com) למק"ט.
// חיפוש מק"ט מדויק מפנה ישירות לעמוד המוצר (nopCommerce), שם מוצגת טבלת מחירי
// החוזה (.price-breakdown) בסשן המחובר. הדף נפתח בתוסף הדפדפן (סשן המשתמש),
// כי מחירי החוזה מוצגים רק כשמחוברים ו-Akamai חוסם שליפה ישירה מהשרת.
// שימי לב: משתמשים במק"ט *המקורי* (עם מקפים), כפי שמופיע ב-keywordsearch.
public static class MasterElectronicsUrl
{
    public static string Build(string sku)
    {
        var kw = Uri.EscapeDataString((sku ?? "").Trim());
        return "https://www.masterelectronics.com/en/keywordsearch?text=" + kw;
    }
}
