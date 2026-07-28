namespace comviaServer.Model;

public class PriceSearchRequest
{
    public List<PriceSearchItem> Items { get; set; } = new();
}

public class PriceSearchItem
{
    public string Sku { get; set; } = string.Empty;
    public int Qty { get; set; }
    public string Config { get; set; } = string.Empty;
}
