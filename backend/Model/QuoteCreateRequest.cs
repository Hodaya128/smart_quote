namespace comviaServer.Model;

public class QuoteCreateRequest
{
    public int? QuoteID { get; set; }  // if set, updates existing quote instead of creating new
    public int CustomerID { get; set; }
    public int CreatedBy { get; set; }
    public string Status { get; set; } = "Completed";
    public List<QuoteItemRequest> Items { get; set; } = new();
}

public class QuoteItemRequest
{
    public string? ComponentSKU { get; set; }
    public string Description { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string SupplierLink { get; set; } = "";
    public string SupplyConfig { get; set; } = "";
    public int Quantity { get; set; }
    public double CostPriceMoment { get; set; }
    public double ProfitMargin { get; set; }
    public double FinalPriceToClient { get; set; }
}

public class BackgroundSearchRequest
{
    public int CustomerID { get; set; }
    // אם מצוין — מעדכן הצעה קיימת (טיוטה) במקום ליצור חדשה. אחרת יוצר חדשה.
    public int? QuoteID { get; set; }
    public List<PriceSearchItem> Items { get; set; } = new();
}
