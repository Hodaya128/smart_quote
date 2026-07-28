namespace comviaServer.Model;

// בקשת בדיקת חלופות מהאשף (שלב 2): לכל פריט מצורפים המחיר הנוכחי ומדרגות המחיר
// של הספק הנבחר — כפי שהתקבלו בתוצאות החיפוש — כדי שהמנוע יעבוד על נתונים אמיתיים.
public class AlternativeCheckRequest
{
    public List<AlternativeCheckItem> Items { get; set; } = new();
}

public class AlternativeCheckItem
{
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
    public double UnitPrice { get; set; }
    public string SupplierName { get; set; } = "";
    public List<PriceTier> PriceTiers { get; set; } = new();
}
