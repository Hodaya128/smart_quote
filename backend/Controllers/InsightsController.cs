using Microsoft.AspNetCore.Mvc;
using comviaServer.Model;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InsightsController : ControllerBase
{
    private readonly InsightsService _bl;

    public InsightsController(InsightsService bl) => _bl = bl;

    [HttpPost("chart")]
    public async Task<IActionResult> Chart(InsightRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Prompt))
            return BadRequest(new { error = "Prompt required" });

        if (req.Prompt.Length > 500)
            return BadRequest(new { error = "Prompt too long (max 500 characters)" });

        var result = await _bl.GenerateChartAsync(req.Prompt);
        if (result.Error != null)
            return StatusCode(500, result);

        return Ok(result);
    }

    // נתוני aggregation גולמיים לבניית ווידג'טים ידנית בצד הלקוח (revenueByMonth/statusCounts/profitByCustomer).
    [HttpGet("data")]
    public IActionResult Data() => Ok(_bl.GetAggregations());
}
