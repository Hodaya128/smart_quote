namespace comviaServer.Model;

// שורת תוצאה שתוסף הדפדפן שולף מטבלת החיפוש של ספק (NetComponents כרגע, ובהמשך ספקים נוספים).
// התוסף רץ בסשן המחובר של המשתמש, פותח את עמוד החיפוש, קורא את ה-DOM ומחזיר את השורות
// בפורמט הזה. השרת ממפה אותן לאותו זרם עיבוד שהיה קודם ב-Selenium.
public class ExtensionSupplierRow
{
    public string PartNumber { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Description { get; set; } = "";
    public string Country { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string SupplierLink { get; set; } = "";
    public List<PriceTier> Prices { get; set; } = new();

    // ספק מורשה (Authorized distributor) — ב-NetComponents מסומן ב-i.ncauth (ה-A) ליד שם
    // הספק. ברירת מחדל true לתאימות לאחור (תוסף ישן שלא שולח את השדה לא יגרום לסינון הכול).
    public bool Authorized { get; set; } = true;
}
