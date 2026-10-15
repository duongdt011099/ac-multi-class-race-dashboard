using System.Security.Cryptography;
using System.Text;

namespace MulticlassRace.Web;

/// <summary>
/// The secret that proves a request comes from the dashboard's own window. It is made fresh on every
/// launch and only ever handed to the embedded browser, as a cookie, so a regular browser pointed at
/// the same address has nothing to present.
/// </summary>
public sealed class AppAccessToken
{
    public const string CookieName = "ac_dashboard_access";

    public string Value { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(candidate),
            Encoding.UTF8.GetBytes(Value));
    }
}

/// <summary>
/// Keeps the dashboard usable from its own window only. The server has to listen on a local port, and
/// any browser on the machine could otherwise open it. A cookie rather than a header carries the
/// secret because the browser sends cookies on the WebSocket upgrade that Blazor Server depends on,
/// while it does not let the host add headers to that request.
///
/// Anything that fails is answered with a plain 404 so the port does not even admit what is behind it.
/// The Host check refuses DNS rebinding, where a web page points its own name at 127.0.0.1.
/// </summary>
public sealed class AppAccessMiddleware(RequestDelegate next, AppAccessToken token)
{
    private const string AllowedHost = "127.0.0.1";

    public async Task InvokeAsync(HttpContext context)
    {
        var hostIsLoopback = string.Equals(context.Request.Host.Host, AllowedHost, StringComparison.Ordinal);
        var hasSecret = context.Request.Cookies.TryGetValue(AppAccessToken.CookieName, out var presented) && token.Matches(presented);

        if (hostIsLoopback is false || hasSecret is false)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await next(context);
    }
}

public static class AppAccessMiddlewareExtensions
{
    public static IApplicationBuilder UseAppAccessGuard(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AppAccessMiddleware>();
    }
}
