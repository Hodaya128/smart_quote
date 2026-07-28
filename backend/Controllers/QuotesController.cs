using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;
using comviaServer.Security;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class QuotesController : ControllerBase
{
    private readonly DBServices _dal;
    private readonly CredentialProtector _protector;

    public QuotesController(DBServices dal, CredentialProtector protector)
    {
        _dal = dal;
        _protector = protector;
    }

    // כל משתמש מחובר. Estimator רואה רק הצעות שהוא יצר + הצעות מדף הנחיתה (CreatedBy=0)
    // — הסינון נעשה כאן בצד שרת (בעבר היה קוסמטי בלבד בפרונט).
    [HttpGet]
    [RequireRole]
    public IActionResult GetAll()
    {
        var quotes = Quote.GetAll(_dal);

        var current = (User?)HttpContext.Items["User"];
        if (current != null && string.Equals(current.Type, "Estimator", StringComparison.OrdinalIgnoreCase))
            quotes = quotes.Where(q => q.CreatedBy == current.UserID || q.CreatedBy == 0).ToList();

        return Ok(quotes);
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var quote = Quote.GetById(_dal, id);
        return quote is null ? NotFound() : Ok(quote);
    }

    [HttpPost]
    public IActionResult Create(Quote quote)
    {
        int newId = quote.Create(_dal);
        quote.QuoteID = newId;
        return CreatedAtAction(nameof(GetById), new { id = newId }, quote);
    }

    [HttpPost("create-from-search")]
    public IActionResult CreateFromSearch(QuoteCreateRequest request)
    {
        int newId = Quote.CreateFromRequest(_dal, _protector, request);
        return Ok(new { quoteID = newId });
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, Quote updated)
    {
        if (!updated.Update(_dal, id)) return NotFound();
        updated.QuoteID = id;
        return Ok(updated);
    }

    [HttpPatch("{id}/status")]
    public IActionResult UpdateStatus(int id, [FromBody] string status)
    {
        if (!Quote.UpdateStatus(_dal, id, status)) return NotFound();
        return Ok(new { QuoteID = id, Status = status });
    }

    [HttpDelete("{id}")]
    [RequireRole("Admin", "Manager")]
    public IActionResult Delete(int id)
    {
        if (!Quote.Delete(_dal, id)) return NotFound();
        return NoContent();
    }
}
