using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ComponentsController : ControllerBase
{
    private readonly DBServices _dal;

    public ComponentsController(DBServices dal) => _dal = dal;

    [HttpGet]
    public IActionResult GetAll() =>
        Ok(Component.GetAll(_dal));

    [HttpGet("{sku}")]
    public IActionResult GetBySku(string sku)
    {
        var component = Component.GetBySku(_dal, sku);
        return component is null ? NotFound() : Ok(component);
    }

    [HttpPost]
    public IActionResult Create(Component component)
    {
        component.Create(_dal);
        return CreatedAtAction(nameof(GetBySku), new { sku = component.ComponentSKU }, component);
    }

    [HttpPut("{sku}")]
    public IActionResult Update(string sku, Component updated)
    {
        if (!updated.Update(_dal, sku)) return NotFound();
        return Ok(updated);
    }

    // force=true (אחרי אישור המשתמש בפרונט): מוחק קודם את ההצעות שמכילות את הרכיב, ואז את הרכיב.
    [HttpDelete("{sku}")]
    public IActionResult Delete(string sku, [FromQuery] bool force = false)
    {
        try
        {
            if (force)
                foreach (var quoteId in _dal.GetQuoteIdsByComponent(sku))
                    Quote.Delete(_dal, quoteId);

            if (!Component.Delete(_dal, sku)) return NotFound();
            return NoContent();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 547)
        {
            // הפרת FK — הרכיב משויך להצעות. 409 עם מספר ההצעות; הפרונט יציע מחיקה מדורגת.
            var count = _dal.GetQuoteIdsByComponent(sku).Count;
            return Conflict(new { error = "לא ניתן למחוק רכיב שמשויך להצעות מחיר קיימות", quoteCount = count });
        }
    }
}
