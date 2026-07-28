namespace comviaServer.Model;

// בונה את כתובת עמוד החיפוש של Arrow (arrow.com) למק"ט. הדף הוא React SPA (תוכן נטען
// ב-JS), לכן נפתח בתוסף הדפדפן ולא נשלף ב-fetch מהשרת.
// שימי לב: לחיפוש ב-Arrow משתמשים במק"ט *המקורי* (עם מקפים), לפי ה-URL לדוגמה.
public static class ArrowUrl
{
    public static string Build(string keyword)
    {
        var kw = Uri.EscapeDataString((keyword ?? "").Trim());
        return "https://www.arrow.com/en/search-result.html?keyword=" + kw + "&currPage=1";
    }
}
