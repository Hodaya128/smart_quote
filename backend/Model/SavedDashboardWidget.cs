using comviaServer.DAL;

namespace comviaServer.Model;

// ווידג'ט שמור במסך הבית פר משתמש (גרף או טבלה). ConfigJson = spec של הווידג'ט
// (מקור נתונים+סוג תצוגה לווידג'ט "חי", או chartConfig/columns+rows ל-snapshot מ-AI).
public class SavedDashboardWidget
{
    public int WidgetID { get; set; }
    public int UserID { get; set; }
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";        // "chart" | "table"
    public string ConfigJson { get; set; } = "";
    public int SortOrder { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedDate { get; set; }

    // ===== לוגיקה עסקית (לשעבר DashboardService) =====

    public static List<SavedDashboardWidget> GetByUser(DBServices dal, int userId) => dal.GetWidgetsByUserId(userId);

    public static SavedDashboardWidget? GetById(DBServices dal, int id) => dal.GetWidgetById(id);

    public int Insert(DBServices dal) => dal.InsertWidget(this);

    public bool Update(DBServices dal, int id)
    {
        if (dal.GetWidgetById(id) == null) return false;
        dal.UpdateWidget(id, this);
        return true;
    }

    public static bool Delete(DBServices dal, int id)
    {
        if (dal.GetWidgetById(id) == null) return false;
        dal.DeleteWidget(id);
        return true;
    }
}
