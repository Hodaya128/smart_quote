namespace comviaServer.Model;

// בקשת הצעת מחיר מדף הנחיתה הציבורי — הלקוח ממלא בעצמו (ללא התחברות).
public class PublicRfqRequest
{
    public string CustomerName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public List<PublicRfqItem> Items { get; set; } = new();
}

public class PublicRfqItem
{
    public string Sku { get; set; } = "";
    public int Qty { get; set; }
}
