using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace comviaServer.Model;

// POC: מקבל URL של דף מוצר אצל ספק, מושך את ה-HTML, ושולח ל-Claude לחילוץ נתונים מובנים.
public class WebExtractService
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<WebExtractService> _logger;

    private const string ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
    private const string ANTHROPIC_VERSION = "2023-06-01";
    private const int MAX_HTML_CHARS = 45000;

    private const string SYSTEM_PROMPT =
        "You are a data extraction assistant for a B2B electronic-components procurement system. " +
        "You receive the cleaned text/HTML of a single distributor product page. " +
        "Extract the product and pricing data and return a SINGLE valid JSON object.\n\n" +
        "Strict output rules:\n" +
        "1. Respond with ONLY a JSON object — no markdown, no code fences, no prose.\n" +
        "2. Schema (use exactly these keys):\n" +
        "{\n" +
        "  \"found\": boolean,                       // false if this is not a product page or no product data\n" +
        "  \"manufacturerPartNumber\": string,\n" +
        "  \"manufacturer\": string,\n" +
        "  \"description\": string,\n" +
        "  \"packaging\": string,                    // e.g. Bag, Reel, Tube, Cut Tape; \"\" if unknown\n" +
        "  \"stock\": string,                        // availability as shown; \"\" if unknown\n" +
        "  \"currency\": string,                     // ISO code: USD, EUR, GBP, PLN... infer from symbol\n" +
        "  \"priceTiers\": [ { \"minQty\": number, \"unitPrice\": number } ],  // quantity price breaks; unit price per piece\n" +
        "  \"datasheetUrl\": string\n" +
        "}\n" +
        "3. Use ONLY values present on the page. Never invent prices. If a field is missing use \"\" (or [] for priceTiers).\n" +
        "4. unitPrice must be the per-piece price (not the line total). minQty is the break quantity.\n" +
        "5. Convert price symbols to ISO currency codes (€->EUR, $->USD, £->GBP, zł->PLN).\n" +
        "6. Prices may use a comma as the decimal separator (European format, e.g. 0,3944) — convert to a dot (0.3944).";

    public WebExtractService(IConfiguration config, HttpClient http, ILogger<WebExtractService> logger)
    {
        _http = http;
        _logger = logger;
        _apiKey = config["Anthropic:ApiKey"] ?? "";
        _model = config["Anthropic:Model"] ?? "claude-haiku-4-5";
    }

    public async Task<WebExtractResult> ExtractAsync(string url, int? qty)
    {
        var result = new WebExtractResult { SourceUrl = url };

        if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.StartsWith("REPLACE_"))
        {
            result.Error = "Anthropic API key is not configured";
            return result;
        }

        if (!IsAllowedUrl(url))
        {
            result.Error = "Invalid or disallowed URL (must be a public http/https address)";
            return result;
        }

        string html;
        try
        {
            html = await FetchHtmlAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebExtract failed to fetch {Url}", url);
            result.Error = $"Failed to fetch page: {ex.Message}";
            return result;
        }

        var cleaned = CleanHtml(html);
        if (cleaned.Length > MAX_HTML_CHARS)
            cleaned = cleaned.Substring(0, MAX_HTML_CHARS);

        try
        {
            var extracted = await CallClaudeAsync(url, cleaned);
            MapInto(result, extracted);
            if (qty.HasValue)
                result.UnitPriceForQty = PriceForQty(result.PriceTiers, qty.Value);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebExtract Claude call failed for {Url}", url);
            result.Error = $"Extraction failed: {ex.Message}";
            return result;
        }
    }

    private async Task<string> FetchHtmlAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        request.Headers.Add("Accept", "text/html,application/xhtml+xml");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<JsonElement> CallClaudeAsync(string url, string cleanedHtml)
    {
        var requestBody = new
        {
            model = _model,
            max_tokens = 1500,
            system = SYSTEM_PROMPT,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = $"Product page URL: {url}\n\nPage content:\n{cleanedHtml}"
                }
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
        var text = responseDoc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        text = StripCodeFences(text);

        // משכפלים את האלמנט כי ה-JsonDocument המקורי יושלך בסוף הבלוק
        using var extractedDoc = JsonDocument.Parse(text);
        return extractedDoc.RootElement.Clone();
    }

    private static void MapInto(WebExtractResult result, JsonElement el)
    {
        result.Found = el.TryGetProperty("found", out var f) && f.ValueKind == JsonValueKind.True;
        result.ManufacturerPartNumber = GetStr(el, "manufacturerPartNumber");
        result.Manufacturer = GetStr(el, "manufacturer");
        result.Description = GetStr(el, "description");
        result.Packaging = GetStr(el, "packaging");
        result.Stock = GetStr(el, "stock");
        result.Currency = GetStr(el, "currency");
        result.DatasheetUrl = GetStr(el, "datasheetUrl");

        if (el.TryGetProperty("priceTiers", out var tiers) && tiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tiers.EnumerateArray())
            {
                double unit = GetNum(t, "unitPrice");
                double minQty = GetNum(t, "minQty");
                if (unit <= 0) continue;
                result.PriceTiers.Add(new PriceTier
                {
                    Price = unit,
                    MinQty = ((long)minQty).ToString(),
                    Currency = result.Currency
                });
            }
        }
    }

    private static double? PriceForQty(List<PriceTier> tiers, int qty)
    {
        if (tiers.Count == 0) return null;
        var ordered = tiers
            .OrderByDescending(t => double.TryParse(t.MinQty, out var m) ? m : 0)
            .ToList();
        foreach (var t in ordered)
        {
            double min = double.TryParse(t.MinQty, out var m) ? m : 0;
            if (min <= qty) return t.Price;
        }
        return ordered.Last().Price;
    }

    // ===== Helpers =====

    private static bool IsAllowedUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host is "localhost" or "127.0.0.1" or "::1" || host.EndsWith(".local")) return false;
        if (IPAddress.TryParse(host, out var ip) && IsPrivate(ip)) return false;
        return true;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return ip.IsIPv6LinkLocal || IPAddress.IsLoopback(ip);
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || b[0] == 127;
    }

    private static string CleanHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        html = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<svg[\\s\\S]*?</svg>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<!--[\\s\\S]*?-->", " ");
        html = Regex.Replace(html, "<(noscript|head|nav|footer)[\\s\\S]*?</\\1>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, "<[^>]+>", " ");          // הסרת כל התגיות הנותרות -> טקסט נקי (חוסך טוקנים ומוצא את המחירים)
        html = WebUtility.HtmlDecode(html);                  // פענוח &amp; &euro; וכו'
        html = Regex.Replace(html, "\\s+", " ");
        return html.Trim();
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
