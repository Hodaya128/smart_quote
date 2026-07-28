namespace comviaServer.Model;

public class WebExtractRequest
{
    public string Url { get; set; } = "";
    public int? Qty { get; set; }
}

public class WebExtractResult
{
    public bool Found { get; set; }
    public string SourceUrl { get; set; } = "";
    public string ManufacturerPartNumber { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Description { get; set; } = "";
    public string Packaging { get; set; } = "";
    public string Stock { get; set; } = "";
    public string Currency { get; set; } = "";
    public List<PriceTier> PriceTiers { get; set; } = new();
    public double? UnitPriceForQty { get; set; }
    public string DatasheetUrl { get; set; } = "";
    public string? Error { get; set; }
}
