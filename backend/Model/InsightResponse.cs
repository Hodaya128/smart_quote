namespace comviaServer.Model;

public class InsightResponse
{
    // spec של ווידג'ט: { kind:'chart', title, chartConfig } או { kind:'table', title, columns, rows }.
    public object? Result { get; set; }
    public string? Error { get; set; }
}
