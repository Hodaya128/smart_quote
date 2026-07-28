using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;

namespace comviaServer.Controllers;

// ווידג'טים שמורים במסך הבית — CRUD פר משתמש. מבנה זהה ל-CustomersController.
[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly DBServices _dal;

    public DashboardController(DBServices dal) => _dal = dal;

    [HttpGet("user/{userId}")]
    public IActionResult GetUserWidgets(int userId) => Ok(SavedDashboardWidget.GetByUser(_dal, userId));

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var w = SavedDashboardWidget.GetById(_dal, id);
        return w is null ? NotFound() : Ok(w);
    }

    [HttpPost]
    public IActionResult Create(SavedDashboardWidget widget)
    {
        int newId = widget.Insert(_dal);
        widget.WidgetID = newId;
        return CreatedAtAction(nameof(GetById), new { id = newId }, widget);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, SavedDashboardWidget updated)
    {
        if (!updated.Update(_dal, id)) return NotFound();
        updated.WidgetID = id;
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        if (!SavedDashboardWidget.Delete(_dal, id)) return NotFound();
        return NoContent();
    }
}
