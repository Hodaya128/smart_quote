using Microsoft.AspNetCore.Mvc;
using comviaServer.DAL;
using comviaServer.Model;
using comviaServer.Security;

namespace comviaServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly DBServices _dal;

    public SettingsController(DBServices dal) => _dal = dal;

    [HttpGet]
    public IActionResult GetAll() => Ok(SettingsDTO.Load(_dal));

    [HttpPost]
    [RequireRole("Admin", "Manager")]
    public IActionResult Save([FromBody] SettingsDTO settings)
    {
        settings.Save(_dal);
        return Ok(new { success = true });
    }
}
