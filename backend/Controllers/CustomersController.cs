using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomersController : ControllerBase
{
    private readonly DBServices _dal;

    public CustomersController(DBServices dal) => _dal = dal;

    [HttpGet]
    public IActionResult GetAll() =>
        Ok(Customer.GetAll(_dal));

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        var customer = Customer.GetById(_dal, id);
        return customer is null ? NotFound() : Ok(customer);
    }

    [HttpPost]
    public IActionResult Create(Customer customer)
    {
        int newId = customer.Create(_dal);
        customer.CustomerID = newId;
        return CreatedAtAction(nameof(GetById), new { id = newId }, customer);
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, Customer updated)
    {
        if (!updated.Update(_dal, id)) return NotFound();
        return Ok(updated);
    }

    // force=true (אחרי אישור המשתמש בפרונט): מוחק קודם את הצעות הלקוח, ואז את הלקוח.
    [HttpDelete("{id}")]
    public IActionResult Delete(int id, [FromQuery] bool force = false)
    {
        try
        {
            if (force)
                foreach (var quoteId in _dal.GetQuoteIdsByCustomer(id))
                    Quote.Delete(_dal, quoteId);

            if (!Customer.Delete(_dal, id)) return NotFound();
            return NoContent();
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 547)
        {
            // הפרת FK — ללקוח יש הצעות. 409 עם מספר ההצעות; הפרונט יציע מחיקה מדורגת.
            var count = _dal.GetQuoteIdsByCustomer(id).Count;
            return Conflict(new { error = "לא ניתן למחוק לקוח שיש לו הצעות מחיר קיימות", quoteCount = count });
        }
    }
}
