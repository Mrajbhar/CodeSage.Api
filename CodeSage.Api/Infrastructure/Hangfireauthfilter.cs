using System.Security.Claims;
using Hangfire.Dashboard;

namespace CodeSage.Api.Infrastructure;

// Restricts the Hangfire dashboard to authenticated platform admins.
// Note: the dashboard reads the auth cookie/header on the same origin; for JWT-in-header
// setups you typically reach it via the app session. In dev you can relax this.
public class HangfireAuthFilter : IDashboardAuthorizationFilter
{
    private readonly bool _allowAll;
    public HangfireAuthFilter(bool allowAll) => _allowAll = allowAll;

    public bool Authorize(DashboardContext context)
    {
        if (_allowAll) return true;   // development convenience

        var http = context.GetHttpContext();
        var user = http.User;
        if (user?.Identity?.IsAuthenticated != true) return false;

        var role = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);
    }
}