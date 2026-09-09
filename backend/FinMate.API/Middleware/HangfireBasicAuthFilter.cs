using System.Text;
using Hangfire.Dashboard;

namespace FinMate.API.Middleware;

public class HangfireBasicAuthFilter : IDashboardAuthorizationFilter
{
    private readonly string? _user;
    private readonly string? _pass;

    public HangfireBasicAuthFilter(string? user, string? pass)
    {
        _user = user;
        _pass = pass;
    }

    public bool Authorize(DashboardContext context)
    {
        if (string.IsNullOrWhiteSpace(_user) || string.IsNullOrWhiteSpace(_pass))
        {
            return false;
        }

        var httpContext = context.GetHttpContext();
        var header = httpContext.Request.Headers["Authorization"].ToString();

        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            httpContext.Response.Headers["WWW-Authenticate"] = "Basic";
            return false;
        }

        var encoded = header["Basic ".Length..].Trim();
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var parts = decoded.Split(':', 2);

        return parts.Length == 2 && parts[0] == _user && parts[1] == _pass;
    }
}
