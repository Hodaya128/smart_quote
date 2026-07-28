using System.Text.Json;

namespace comviaServer.Model;

// המרת מטבע ל-USD לפי שערים מ-API ציבורי חינמי (open.er-api.com — ללא מפתח/הרשמה).
// טוען שערים פעם ב-_ttl ושומר אותם בזיכרון (singleton). אם ה-API לא זמין — נופלים ל-cache
// קודם, ואם אין — לטבלת fallback מובנית, כדי שמחירים לא-דולריים לא ייעלמו לגמרי.
//
// פורמט ה-API: { "result":"success", "base_code":"USD", "rates": { "USD":1, "EUR":0.92, ... } }
// כלומר rates[C] = כמה יחידות מטבע C שוות ל-1 USD. לכן: USD = amount_in_C / rates[C].
public class CurrencyService
{
    private readonly HttpClient _http;
    private readonly ILogger<CurrencyService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly TimeSpan _ttl = TimeSpan.FromHours(6);

    private Dictionary<string, double> _ratesPerUsd = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _loadedAtUtc = DateTime.MinValue;

    // fallback סטטי (משוערך) למקרה שה-API לא זמין ואין עדיין cache. לא מדויק, אך עדיף
    // מהשמטת המחיר. ברגע שה-API נטען בהצלחה — הערכים האלה לא בשימוש.
    private static readonly Dictionary<string, double> Fallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["USD"] = 1.0, ["EUR"] = 0.92, ["GBP"] = 0.79, ["ILS"] = 3.6,
        ["JPY"] = 150.0, ["CNY"] = 7.2, ["HKD"] = 7.8, ["CAD"] = 1.36,
        ["AUD"] = 1.5, ["SGD"] = 1.34, ["TWD"] = 31.5, ["KRW"] = 1330.0,
        ["INR"] = 83.0, ["CHF"] = 0.88, ["SEK"] = 10.5, ["MXN"] = 17.0,
    };

    public CurrencyService(IHttpClientFactory httpFactory, ILogger<CurrencyService> logger)
    {
        _http = httpFactory.CreateClient();
        _http.Timeout = TimeSpan.FromSeconds(8);
        _logger = logger;
    }

    // מוודא ששערי ההמרה טעונים ועדכניים. זול לקרוא לפני כל חיפוש (cache ל-6ש').
    public async Task EnsureRatesAsync()
    {
        if (_ratesPerUsd.Count > 0 && DateTime.UtcNow - _loadedAtUtc < _ttl) return;

        await _lock.WaitAsync();
        try
        {
            if (_ratesPerUsd.Count > 0 && DateTime.UtcNow - _loadedAtUtc < _ttl) return;

            var json = await _http.GetStringAsync("https://open.er-api.com/v6/latest/USD");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("rates", out var rates) && rates.ValueKind == JsonValueKind.Object)
            {
                var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in rates.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number)
                        map[p.Name] = p.Value.GetDouble();

                if (map.Count > 0)
                {
                    _ratesPerUsd = map;
                    _loadedAtUtc = DateTime.UtcNow;
                    _logger.LogInformation("FX rates loaded from API: {Count} currencies", map.Count);
                    return;
                }
            }
            _logger.LogWarning("FX API returned no usable rates; keeping cache/fallback");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FX rate fetch failed; keeping cache/fallback");
        }
        finally
        {
            _lock.Release();
        }

        // אין cache בכלל — מאתחלים מ-fallback כדי שתמיד תהיה אפשרות להמיר מטבעות נפוצים.
        if (_ratesPerUsd.Count == 0)
            _ratesPerUsd = new Dictionary<string, double>(Fallback, StringComparer.OrdinalIgnoreCase);
    }

    // ממפה סמלים/וריאציות נפוצות לקוד ISO תקני (NC לעיתים מחזיר סמל ולא קוד).
    private static string NormalizeCurrency(string? currency)
    {
        var c = (currency ?? "").Trim().ToUpperInvariant();
        return c switch
        {
            "" or "USD" or "US$" or "$" or "USD$" => "USD",
            "€" or "EUR€" => "EUR",
            "£" or "GBP£" => "GBP",
            "¥" or "JPY¥" or "RMB" or "CN¥" or "￥" => "JPY", // ¥ עמום; ברירת מחדל JPY (CNY מזוהה לפי הקוד)
            "₪" or "NIS" or "SHEKEL" => "ILS",
            _ => c
        };
    }

    // ממיר סכום מהמטבע הנתון ל-USD. אם כבר USD/ריק — מחזיר כמו שהוא.
    // מחזיר null אם המטבע לא מוכר (אי אפשר להמיר באמינות).
    public double? ToUsd(double amount, string? currency)
    {
        var cur = NormalizeCurrency(currency);
        if (cur == "USD") return amount;

        var src = _ratesPerUsd.Count > 0 ? _ratesPerUsd : Fallback;
        if (src.TryGetValue(cur, out var rate) && rate > 0)
            return amount / rate;

        _logger.LogWarning("No FX rate for currency '{Orig}' (normalized '{Cur}'); leaving price unconverted",
            currency, cur);
        return null;
    }

    // ממיר רשימת שוברי מחיר ל-USD. שובר שלא ניתן להמיר נשמר כמו שהוא (במטבע המקורי),
    // כדי לא לאבד מידע — שובר כזה פשוט לא ייבחר כ"מחיר USD" בהמשך.
    public List<PriceTier> ToUsdTiers(List<PriceTier> tiers)
    {
        var outp = new List<PriceTier>(tiers.Count);
        foreach (var t in tiers)
        {
            var usd = ToUsd(t.Price, t.Currency);
            outp.Add(usd.HasValue
                ? new PriceTier { Price = Math.Round(usd.Value, 4), MinQty = t.MinQty, Currency = "USD" }
                : t);
        }
        return outp;
    }
}
