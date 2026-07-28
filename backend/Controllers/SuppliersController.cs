using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;
using comviaServer.Security;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuppliersController : ControllerBase
{
    private readonly DBServices _dal;
    private readonly CredentialProtector _protector;

    public SuppliersController(DBServices dal, CredentialProtector protector)
    {
        _dal = dal;
        _protector = protector;
    }

    [HttpGet]
    public IActionResult GetAll() =>
        Ok(Supplier.GetAll(_dal));

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var supplier = Supplier.GetById(_dal, id);
        return supplier is null ? NotFound() : Ok(supplier);
    }

    // צפייה בסיסמת ההתחברות של הספק — לאדמין בלבד.
    // האימות נעשה בצד שרת לפי הטוקן (RequireRole) — לא לפי userId מה-query.
    [HttpGet("{id}/password")]
    [RequireRole("Admin")]
    public IActionResult GetPassword(int id)
    {
        if (Supplier.GetById(_dal, id) is null) return NotFound();

        return Ok(new { password = Supplier.GetDecryptedPassword(_dal, _protector, id) });
    }

    [HttpPost]
    public IActionResult Create(Supplier supplier)
    {
        int newId = supplier.Create(_dal, _protector);
        supplier.SupplierID = newId;
        return CreatedAtAction(nameof(GetById), new { id = newId }, supplier);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, Supplier updated)
    {
        if (!updated.Update(_dal, _protector, id)) return NotFound();
        return Ok(updated);
    }

    // force=true (אחרי אישור המשתמש בפרונט): מוחק קודם את ההצעות שמשתמשות בספק, ואז את הספק.
    [HttpDelete("{id}")]
    public IActionResult Delete(int id, [FromQuery] bool force = false)
    {
        try
        {
            if (force)
                foreach (var quoteId in _dal.GetQuoteIdsBySupplier(id))
                    Quote.Delete(_dal, quoteId);

            if (!Supplier.Delete(_dal, id)) return NotFound();
            return NoContent();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 547)
        {
            // הפרת FK — הספק משויך להצעות. 409 עם מספר ההצעות; הפרונט יציע מחיקה מדורגת.
            var count = _dal.GetQuoteIdsBySupplier(id).Count;
            return Conflict(new { error = "לא ניתן למחוק ספק שמשויך להצעות מחיר קיימות", quoteCount = count });
        }
    }
}
