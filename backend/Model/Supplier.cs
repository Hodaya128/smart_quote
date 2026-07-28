using comviaServer.DAL;
using comviaServer.Security;

namespace comviaServer.Model;

public class Supplier
{
    public int SupplierID { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string WebsiteUrl { get; set; } = string.Empty;

    // האם נדרשת התחברות לאתר הספק כדי לקבל מחירים מיוחדים
    public bool RequiresLogin { get; set; }

    // פרטי התחברות לאתר הספק. הסיסמה נשמרת מוצפנת ב-DB.
    // ההצפנה/פענוח מתבצעים במתודות שלמטה באמצעות CredentialProtector.
    public string? LoginAccount { get; set; }
    public string? LoginUsername { get; set; }
    public string? LoginPassword { get; set; }

    // ===== לוגיקה עסקית (לשעבר SupplierService) =====

    public static List<Supplier> GetAll(DBServices dal)
    {
        var suppliers = dal.GetAllSuppliers();
        // לא מחזירים סיסמאות ברשימה — גם לא מוצפנות
        foreach (var s in suppliers)
            s.LoginPassword = null;
        return suppliers;
    }

    public static Supplier? GetById(DBServices dal, int id)
    {
        var supplier = dal.GetSupplierById(id);
        if (supplier != null)
            // לא מחזירים את הסיסמה ללקוח; בעריכה משאירים ריק כדי לשמור את הקיימת
            supplier.LoginPassword = null;
        return supplier;
    }

    // מפענח ומחזיר את סיסמת ההתחברות של הספק. מיועד למסך אדמין בלבד —
    // ה-Controller אחראי לאמת שהקורא הוא אדמין לפני הקריאה לכאן.
    public static string? GetDecryptedPassword(DBServices dal, CredentialProtector protector, int id)
    {
        var supplier = dal.GetSupplierById(id);
        return supplier == null ? null : protector.Decrypt(supplier.LoginPassword);
    }

    public int Create(DBServices dal, CredentialProtector protector)
    {
        LoginPassword = protector.Encrypt(LoginPassword);
        return dal.InsertSupplier(this);
    }

    public bool Update(DBServices dal, CredentialProtector protector, int id)
    {
        var existing = dal.GetSupplierById(id);
        if (existing == null) return false;

        // אם לא נשלחה סיסמה חדשה — שומרים את הקיימת (כבר מוצפנת)
        LoginPassword = string.IsNullOrEmpty(LoginPassword)
            ? existing.LoginPassword
            : protector.Encrypt(LoginPassword);

        dal.UpdateSupplier(id, this);
        return true;
    }

    public static bool Delete(DBServices dal, int id)
    {
        if (dal.GetSupplierById(id) == null) return false;
        dal.DeleteSupplier(id);
        return true;
    }
}
