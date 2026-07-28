namespace comviaServer.Model;

public class PriceSearchResponse
{
    public List<PriceSearchResult> Results { get; set; } = new();
}

public class PriceSearchResult
{
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
    public List<SupplierResult> Suppliers { get; set; } = new();
    public List<CompetitorResult> Competitors { get; set; } = new();
    public CheapestInfo? CheapestSupplier { get; set; }
    public CheapestInfo? CheapestCompetitor { get; set; }
}

public class SupplierResult
{
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public double? UnitPrice { get; set; }
    public string? Currency { get; set; }
    public string QtyAvailable { get; set; } = "";
    public string Link { get; set; } = "";
    public string Description { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public List<PriceTier> PriceTiers { get; set; } = new();
    // true אם המחיר נשלף לאחר התחברות אישית לאתר הספק (מחיר מותאם)
    public bool IsCustomPrice { get; set; }
}

public class CompetitorResult
{
    public string Source { get; set; } = "";
    public double? UnitPrice { get; set; }
    public string? Currency { get; set; }
    public string Description { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public int QtyAvailable { get; set; }
}

public class CheapestInfo
{
    public string Name { get; set; } = "";
    public string? Source { get; set; }
    public double UnitPrice { get; set; }
    public string Currency { get; set; } = "USD";
    public string Description { get; set; } = "";
}

public class PriceTier
{
    public double Price { get; set; }
    public string MinQty { get; set; } = "";
    public string Currency { get; set; } = "";
}
