using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using comviaServer.DAL;
using comviaServer.Model;

namespace comviaServer.Security;

/// <summary>
/// אכיפת הרשאות בצד שרת לפי הטוקן שנוצר בלוגין (עמודת Token בטבלת Users).
///
/// שימוש: [RequireRole] — כל משתמש מחובר; [RequireRole("Admin")] — אדמין בלבד;
/// [RequireRole("Admin","Manager")] — אדמין או מנהל.
///
/// הלקוח שולח את הטוקן ב-header ‏X-Token (או Authorization: Bearer).
/// חסר/לא מוכר → 401; תפקיד לא מתאים → 403.
/// בהצלחה המשתמש נשמר ב-HttpContext.Items["User"] לשימוש ה-Controller.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireRoleAttribute : Attribute, IAuthorizationFilter
{
    private readonly string[] _roles;

    public RequireRoleAttribute(params string[] roles) => _roles = roles;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // שליפת הטוקן: X-Token, או Authorization: Bearer <token>
        string? token = context.HttpContext.Request.Headers["X-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token))
        {
            var auth = context.HttpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                token = auth["Bearer ".Length..].Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing token" });
            return;
        }

        // ה-attribute לא נבנה ע"י ה-DI, לכן DBServices נשלף מה-RequestServices.
        var dal = context.HttpContext.RequestServices.GetRequiredService<DBServices>();
        var user = dal.GetUserByToken(token);

        if (user == null)
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Invalid or expired token" });
            return;
        }

        if (_roles.Length > 0 &&
            !_roles.Any(r => string.Equals(r, user.Type, StringComparison.OrdinalIgnoreCase)))
        {
            context.Result = new ObjectResult(new { error = "Insufficient permissions" }) { StatusCode = 403 };
            return;
        }

        context.HttpContext.Items["User"] = user;
    }
}
