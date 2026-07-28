using System.Text;
using System.Text.Json;
using comviaServer.DAL;

namespace comviaServer.Model;

public class InsightsService
{
    private readonly DBServices _dal;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    private const string ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
    private const string ANTHROPIC_VERSION = "2023-06-01";

    private const string SYSTEM_PROMPT =
        "You are a data assistant for a B2B electronic-components quote management system. " +
        "Given a user question (Hebrew or English) and aggregated business data, return a SINGLE JSON object describing ONE widget — a chart OR a table.\n\n" +
        "Strict output rules:\n" +
        "1. Respond with ONLY a JSON object — no markdown, no code fences, no prose.\n" +
        "2. Decide the kind from the question: if the user asks for a table/list/פירוט/טבלה → kind=\"table\"; otherwise → kind=\"chart\".\n" +
        "3. For a CHART return: {\"kind\":\"chart\",\"title\":\"<short title>\",\"chartConfig\":{<valid Chart.js v4 config with keys type,data,options>}}. type ∈ line|bar|pie|doughnut (time-series→line; distribution→pie/doughnut; ranking/comparison→bar). Always set options.responsive=true and options.plugins.title.display=true.\n" +
        "4. For a TABLE return: {\"kind\":\"table\",\"title\":\"<short title>\",\"columns\":[\"col1\",\"col2\",...],\"rows\":[[v1,v2,...],...]}.\n" +
        "5. Use ONLY values from the provided aggregation data. Do not invent numbers.\n" +
        "6. If the question is in Hebrew, write title/labels/columns in Hebrew; otherwise English.\n" +
        "7. Currency values: label with ₪ or NIS.\n\n" +
        "Available aggregation fields:\n" +
        "- revenueByMonth: array of {month, totalRevenue, totalProfit, count}\n" +
        "- statusCounts: array of {status, count}\n" +
        "- profitByCustomer: array of {customer, totalProfit, count}\n\n" +
        "If the question cannot be answered from the data, return a chart titled " +
        "\"אין מספיק נתונים\" (if Hebrew) or \"Insufficient data\" (if English) with empty datasets.";

    public InsightsService(DBServices dal, IConfiguration config, HttpClient http)
    {
        _dal = dal;
        _http = http;
        _apiKey = config["Anthropic:ApiKey"] ?? "";
        _model = config["Anthropic:Model"] ?? "claude-3-5-sonnet-latest";
    }

    public async Task<InsightResponse> GenerateChartAsync(string prompt)
    {
        try
        {
            // בדיקה שיש API key מוגדר
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey.StartsWith("REPLACE_"))
                return new InsightResponse { Error = "Anthropic API key is not configured" };

            // 1. שליפת ההצעות מה-DB
            var quotes = _dal.GetAllQuotes();

            // 2. חישוב aggregations עם LINQ
            var aggregations = BuildAggregations(quotes);
            var dataJson = JsonSerializer.Serialize(aggregations);

            // 3. בניית הבקשה ל-Claude API
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
                        content = $"Question: {prompt}\n\nData:\n{dataJson}"
                    }
                }
            };

            var bodyJson = JsonSerializer.Serialize(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, ANTHROPIC_URL);
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", ANTHROPIC_VERSION);
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

            // 4. שליחה
            var response = await _http.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new InsightResponse { Error = $"Claude API error: {response.StatusCode} - {responseText}" };

            // 5. חילוץ הטקסט מהתשובה של Claude
            // פורמט: { "content": [ { "type": "text", "text": "..." } ], ... }
            using var responseDoc = JsonDocument.Parse(responseText);
            var contentArr = responseDoc.RootElement.GetProperty("content");
            var text = contentArr[0].GetProperty("text").GetString() ?? "";

            // 6. ניקוי markdown code fences אם יש (לפעמים Claude עוטף ב-```json)
            text = StripCodeFences(text);

            // 7. Parse ה-JSON של ה-widget spec (chart או table)
            using var widgetDoc = JsonDocument.Parse(text);
            var widget = JsonSerializer.Deserialize<object>(text);

            return new InsightResponse { Result = widget };
        }
        catch (JsonException jex)
        {
            return new InsightResponse { Error = $"Failed to parse Claude JSON response: {jex.Message}" };
        }
        catch (Exception ex)
        {
            return new InsightResponse { Error = $"Unexpected error: {ex.Message}" };
        }
    }

    // חשיפת ה-aggregations לבניית ווידג'טים ידנית בצד הלקוח (בלי AI).
    public object GetAggregations() => BuildAggregations(_dal.GetAllQuotes());

    // חישוב שלושת ה-aggregations מהרשימה
    private static object BuildAggregations(List<Quote> quotes)
    {
        // הכנסות ורווח לפי חודש
        var revenueByMonth = quotes
            .GroupBy(q => new { q.CreatedDate.Year, q.CreatedDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new
            {
                month = $"{g.Key.Year}-{g.Key.Month:D2}",
                totalRevenue = g.Sum(q => q.FinalTotalPrice),
                totalProfit = g.Sum(q => q.TotalProfit),
                count = g.Count()
            })
            .ToList();

        // ספירה לפי סטטוס
        var statusCounts = quotes
            .GroupBy(q => q.Status ?? "Unknown")
            .Select(g => new { status = g.Key, count = g.Count() })
            .ToList();

        // Top 10 לקוחות לפי רווח
        var profitByCustomer = quotes
            .GroupBy(q => q.Customer?.CustomerName ?? "Unknown")
            .Select(g => new
            {
                customer = g.Key,
                totalProfit = g.Sum(q => q.TotalProfit),
                count = g.Count()
            })
            .OrderByDescending(x => x.totalProfit)
            .Take(10)
            .ToList();

        return new { revenueByMonth, statusCounts, profitByCustomer };
    }

    // הסרת ```json ... ``` אם Claude עטף את התשובה
    private static string StripCodeFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```"))
        {
            // מסיר את השורה הראשונה (``` או ```json)
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text.Substring(firstNewline + 1);

            // מסיר את ה-``` בסוף
            if (text.EndsWith("```"))
                text = text.Substring(0, text.Length - 3);

            text = text.Trim();
        }
        return text;
    }
}
