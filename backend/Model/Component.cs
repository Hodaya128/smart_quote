using comviaServer.DAL;

namespace comviaServer.Model;

public class Component
{
    public string ComponentSKU { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BaseUnit { get; set; } = string.Empty;
    public string? AlternativeSKU { get; set; }

    // ===== לוגיקה עסקית (לשעבר ComponentService) =====

    public static List<Component> GetAll(DBServices dal) => dal.GetAllComponents();

    public static Component? GetBySku(DBServices dal, string sku) => dal.GetComponentBySku(sku);

    public void Create(DBServices dal) => dal.InsertComponent(this);

    public bool Update(DBServices dal, string sku)
    {
        if (dal.GetComponentBySku(sku) == null) return false;
        dal.UpdateComponent(sku, this);
        return true;
    }

    public static bool Delete(DBServices dal, string sku)
    {
        if (dal.GetComponentBySku(sku) == null) return false;
        dal.DeleteComponent(sku);
        return true;
    }
}
