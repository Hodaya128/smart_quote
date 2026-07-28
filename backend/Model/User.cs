using comviaServer.DAL;

namespace comviaServer.Model;

public class User
{
    public int UserID { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string Type { get; set; } = string.Empty;   // "Admin" | "Manager" | "Estimator"
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // ===== לוגיקה עסקית (לשעבר UserService) =====
    // המתודות מקבלות DBServices כפרמטר ולא בבנאי — כדי שהמחלקה תישאר
    // יעד binding תקין ל-JSON בבקשות API.

    public static List<User> GetAll(DBServices dal) => dal.GetAllUsers();

    public static User? GetById(DBServices dal, int id) => dal.GetUserById(id);

    public static User? GetByEmail(DBServices dal, string email) => dal.GetUserByEmail(email);

    public User? Register(DBServices dal)
    {
        // ולידציה
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            return null;

        // בדיקת כפילות email
        if (dal.GetUserByEmail(Email) != null)
            return null;

        // הכנסה ל-DB
        var created = dal.InsertUser(this);

        // הסתרת סיסמה בתשובה
        if (created != null) created.Password = null!;
        return created;
    }

    public static User? Login(DBServices dal, string email, string password)
    {
        // ולידציה
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return null;

        // אימות
        var user = dal.GetUserByEmail(email);
        if (user == null || user.Password != password) return null;

        // יצירת טוקן
        var token = Guid.NewGuid().ToString();
        dal.UpdateUserToken(user.UserID, token);

        // הסתרת סיסמה בתשובה
        user.Password = null!;
        user.Token = token;
        return user;
    }

    public static bool Logout(DBServices dal, int id)
    {
        if (dal.GetUserById(id) == null) return false;
        dal.UpdateUserToken(id, null);
        return true;
    }

    public bool Update(DBServices dal, int id)
    {
        if (dal.GetUserById(id) == null) return false;
        dal.UpdateUser(id, this);
        return true;
    }

    public static bool UpdatePassword(DBServices dal, int id, string password)
    {
        if (dal.GetUserById(id) == null) return false;
        dal.UpdateUserPassword(id, password);
        return true;
    }

    public static bool Delete(DBServices dal, int id)
    {
        if (dal.GetUserById(id) == null) return false;
        dal.DeleteUser(id);
        return true;
    }
}
