using Microsoft.AspNetCore.Mvc;
using comviaServer.Model;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebExtractController : ControllerBase
{
    private readonly WebExtractService _bl;

    public WebExtractController(WebExtractService bl) => _bl = bl;

    [HttpPost("extract")]
    public async Task<IActionResult> Extract(WebExtractRequest req)
    {
        if (req == null || string.IsNullOrWhiteSpace(req.Url))
            return BadRequest(new { error = "Url required" });

        var result = await _bl.ExtractAsync(req.Url, req.Qty);
        if (result.Error != null)
            return StatusCode(500, result);

        return Ok(result);
    }
}
