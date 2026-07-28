using System.Text;
using System.Text.Json;

namespace comviaServer.Model;

// חילוץ מחירים באמצעות AI לאתרים "מבולגנים" (DOM לא יציב / class-ים אקראיים כמו Arrow).
// התוסף מושך את הטקסט של אזור התוצאות בסשן המחובר; כאן שולחים אותו ל-Claude ומקבלים
// רשימת "הצעות" מובנות — הצעה לכל מחסן/זמינות, כל אחת עם מדרגות המחיר שלה.
// נשען על אותה תבנית קריאה כמו WebExtractService (מפתח/מודל ב-appsettings תחת Anthropic).
public class AiSupplierExtractor
{
    private readonly HttpClient _http = new();
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<AiSupplierExtractor> _logger;

    private const string ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
    private const string ANTHROPIC_VERSION = "2023-06-01";
    private const int MAX_TEXT_CHARS = 14000;

    private const string SYSTEM_PROMPT =
        "You are a data-extraction assistant for a B2B electronic-components procurement system. " +
        "You receive the plain text of ONE distributor search-results page for a single part number, " +
        "logged in as a customer (so prices are contract prices). The page lists the part under one or more " +
        "AVAILABILITY OFFERS — typically one per warehouse / ship-from location / stock status — and each offer " +
        "has its OWN quantity price breaks and its own minimum order quantity.\n\n" +
        "Extract EVERY distinct offer separately. Return a SINGLE valid JSON object.\n\n" +
        "Strict rules:\n" +
        "1. Respond with ONLY a JSON object — no markdown, no code fences, no prose.\n" +
        "2. Schema (use exactly these keys):\n" +
        "{\n" +
        "  \"offers\": [\n" +
        "    {\n" +
        "      \"warehouse\": string,   // ship-from / warehouse / region label as shown, e.g. \"North America\", \"Hong Kong In Stock\"; \"\" if unknown\n" +
        "      \"stock\": string,       // in-stock quantity for this offer as shown; \"\" if unknown\n" +
        "      \"currency\": string,    // ISO code (USD, EUR, GBP...); infer from symbol\n" +
        "      \"tiers\": [ { \"minQty\": number, \"unitPrice\": number } ]  // quantity price breaks; unitPrice is PER PIECE\n" +
        "    }\n" +
        "  ]\n" +
        "}\n" +
        "3. Use ONLY values present in the text. NEVER invent or round prices. If there are no offers/prices return {\"offers\":[]}.\n" +
        "4. unitPrice is the per-piece price, not a line total. minQty is the break quantity (e.g. \"400+\" -> 400, \"1+\" -> 1).\n" +
        "5. Keep each warehouse/offer separate even if the part number appears more than once.\n" +
        "6. Convert currency symbols to ISO codes (€->EUR, $->USD, £->GBP). Decimal comma -> dot.";

    public AiSupplierExtractor(IConfiguration config, ILogger<AiSupplierExtractor> logger)
    {
        _logger = logger;
        _apiKey = config["Anthropic:ApiKey"] ?? "";
        _model = config["Anthropic:Model"] ?? "claude-haiku-4-5";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey) && !_apiKey.StartsWith("REPLACE_");

    // מחזיר את רשימת ההצעות שחולצו מהטקסט. רשימה ריקה אם לא הוגדר מפתח / שגיאה / אין מחירים.
    public async Task<List<AiSupplierOffer>> ExtractOffersAsync(string supplier, string text)
    {
        var offers = new List<AiSupplierOffer>();
        if (!IsConfigured)
        {
            _logger.LogWarning("AiSupplierExtractor: Anthropic key not configured — skipping {Supplier}", supplier);
            return offers;
        }
        if (string.IsNullOrWhiteSpace(text)) return offers;
        if (text.Length > MAX_TEXT_CHARS) text = text.Substring(0, MAX_TEXT_CHARS);

        try
        {
            var el = await CallClaudeAsync(supplier, text);
            if (el.TryGetProperty("offers", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in arr.EnumerateArray())
                {
                    var offer = new AiSupplierOffer
                    {
                        Warehouse = GetStr(o, "warehouse"),
                        Stock = GetStr(o, "stock"),
                        Currency = GetStr(o, "currency")
                    };
                    if (string.IsNullOrWhiteSpace(offer.Currency)) offer.Currency = "USD";

                    if (o.TryGetProperty("tiers", out var tiers) && tiers.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var t in tiers.EnumerateArray())
                        {
                            double unit = GetNum(t, "unitPrice");
                            double minQty = GetNum(t, "minQty");
                            // ולידציה: מחיר חיובי וסביר (מגן מפני הזיות של המודל).
                            if (unit <= 0 || unit > 1_000_000) continue;
                            offer.Tiers.Add(new PriceTier
                            {
                                Price = unit,
                                MinQty = ((long)Math.Max(0, minQty)).ToString(),
                                Currency = offer.Currency
                            });
                        }
                    }

                    if (offer.Tiers.Count > 0) offers.Add(offer);
                }
            }
            _logger.LogInformation("AiSupplierExtractor: {Supplier} -> {Count} offers", supplier, offers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AiSupplierExtractor failed for {Supplier}", supplier);
        }
        return offers;
    }

    private async Task<JsonElement> CallClaudeAsync(string supplier, string text)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 2000,
            system = SYSTEM_PROMPT,
            messages = new[]
            {
                new { role = "user", content = $"Distributor: {supplier}\n\nSearch-results page text:\n{text}" }
            }
        };

        var bodyJson = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, ANTHROPIC_URL);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", ANTHROPIC_VERSION);
        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Claude API error: {response.StatusCode} - {responseText}");

        using var responseDoc = JsonDocument.Parse(responseText);
        var content = responseDoc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        content = StripCodeFences(content);

        using var extractedDoc = JsonDocument.Parse(content);
        return extractedDoc.RootElement.Clone();
    }

    private static string StripCodeFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text.Substring(firstNewline + 1);
            if (text.EndsWith("```")) text = text.Substring(0, text.Length - 3);
            text = text.Trim();
        }
        return text;
    }

    private static string GetStr(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double GetNum(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var d)) return d;
        return 0;
    }
}

// הצעת ספק בודדת שחולצה ע"י ה-AI (מחסן/זמינות אחת עם מדרגות המחיר שלה).
public class AiSupplierOffer
{
    public string Warehouse { get; set; } = "";
    public string Stock { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public List<PriceTier> Tiers { get; set; } = new();
}
