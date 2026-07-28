namespace comviaServer.Model;

public class QuoteItem
{
    public int ItemID { get; set; }
    public int QuoteID { get; set; }
    public string? ComponentSKU { get; set; }
    public int SupplierID { get; set; }
    public string SupplyConfig { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public double CostPriceMoment { get; set; }
    public double ProfitMargin { get; set; }
    public double FinalPriceToClient { get; set; }

    // Navigation (ממולאים ידנית ב-DBServices)
    public Component? Component { get; set; }
    public Supplier? Supplier { get; set; }
}
