using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;
using comviaServer.Security;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly DBServices _dal;

    public UsersController(DBServices dal) => _dal = dal;

    // הערה: הכתיב Model.User (ולא User) נדרש כי ל-ControllerBase יש property בשם User.
    [HttpGet]
    [RequireRole("Admin", "Manager")]
    public IActionResult GetAll() =>
        Ok(Model.User.GetAll(_dal));

    [HttpGet("{id}")]
    [RequireRole("Admin", "Manager")]
    public IActionResult GetById(int id)
    {
        var user = Model.User.GetById(_dal, id);
        if (user is null) return NotFound();
        return Ok(new { user.UserID, user.UserName, user.Email, user.Type, user.CreatedDate });
    }

    [HttpPost("register")]
    [RequireRole("Admin", "Manager")]
    public IActionResult Register(User user)
    {
        var created = user.Register(_dal);
        if (created is null) return Conflict("Email already in use or invalid data.");
        return CreatedAtAction(nameof(GetById), new { id = created.UserID },
            new { created.UserID, created.UserName, created.Email, created.Type, created.CreatedDate });
    }

    // לוגין — פתוח (זו הדרך להשיג טוקן).
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest req)
    {
        var user = Model.User.Login(_dal, req.Email, req.Password);
        if (user is null) return Unauthorized("Invalid credentials.");
        return Ok(new { user.UserID, user.UserName, user.Email, user.Type, user.Token });
    }

    [HttpPost("logout/{id}")]
    [RequireRole]
    public IActionResult Logout(int id)
    {
        if (!Model.User.Logout(_dal, id)) return NotFound();
        return NoContent();
    }

    [HttpPut("{id}")]
    [RequireRole("Admin", "Manager")]
    public IActionResult Update(int id, User updated)
    {
        if (!updated.Update(_dal, id)) return NotFound();
        return Ok(new { updated.UserID, updated.Email, updated.Type });
    }

    [HttpPut("{id}/password")]
    [RequireRole("Admin", "Manager")]
    public IActionResult UpdatePassword(int id, [FromBody] PasswordRequest req)
    {
        if (!Model.User.UpdatePassword(_dal, id, req.Password)) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [RequireRole("Admin", "Manager")]
    public IActionResult Delete(int id)
    {
        if (!Model.User.Delete(_dal, id)) return NotFound();
        return NoContent();
    }
}

public record LoginRequest(string Email, string Password);
public record PasswordRequest(string Password);
