namespace comviaServer.Model;

// בונה את כתובת עמוד החיפוש של NetComponents עבור מק"ט יחיד, בפורמט שהתוסף פותח בסשן
// המחובר של המשתמש. הפורמט נלקח מ-URL אמיתי של האתר (search/result עם PartsSearched[0]).
public static class NetComponentsUrl
{
    public static string Build(string partNumber)
    {
        var pn = Uri.EscapeDataString(partNumber ?? "");
        // %5B0%5D = [0]. שומרים על אותם פרמטרים כמו ב-URL האמיתי (SearchLogic=Begins וכו').
        return "https://www.netcomponents.com/search/result"
             + "?SearchId=0&SortBy=0&Demo=false&SearchType=0&SearchLogic=Begins"
             + "&Filters=true&Filters=false&PSA=false"
             + "&PartsSearched%5B0%5D.PartNumber=" + pn
             + "&PartsSearched%5B1%5D.PartNumber="
             + "&PartsSearched%5B2%5D.PartNumber="
             + "&MultiSearchParts=";
    }
}
