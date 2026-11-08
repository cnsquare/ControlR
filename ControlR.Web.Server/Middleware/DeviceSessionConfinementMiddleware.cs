using ControlR.Web.Server.Authn;

namespace ControlR.Web.Server.Middleware;

/// <summary>
/// A logon-token guest (a no-login device-access invite) is signed in as the inviting user, so a full
/// application cookie would otherwise let them reach the whole app — the dashboard, Settings, and the
/// inviting user's own Account pages. This confines such a session to the device-access area (plus the
/// framework, API, hub, relay and static assets it needs) and redirects or refuses anything else.
/// </summary>
public class DeviceSessionConfinementMiddleware(RequestDelegate next)
{
  // Everything a device-access session legitimately needs. Anything else is a main-app page or an
  // Identity (/Account) page and is sent back to the device-access view.
  private static readonly string[] _allowedPrefixes =
  [
    "/device-access",
    "/_framework",
    "/_content",
    "/_blazor",
    "/api",
    "/hubs",
    "/relay",
    "/novnc",
    "/downloads",
    "/static",
    "/lib",
    "/images",
    "/css",
    "/js",
    "/fonts",
    "/health",
  ];

  private readonly RequestDelegate _next = next;

  public async Task Invoke(HttpContext context)
  {
    var user = context.User;
    if (user.Identity?.IsAuthenticated != true ||
        user.FindFirst(UserClaimTypes.AuthenticationMethod)?.Value != PrincipalClaimValues.LogonTokenMethod)
    {
      // Not a device-session guest — normal users are unaffected.
      await _next(context);
      return;
    }

    var path = context.Request.Path;
    if (IsAllowed(path))
    {
      await _next(context);
      return;
    }

    // A non-GET to a disallowed path (e.g. an Account management POST) must never run for a guest.
    if (!HttpMethods.IsGet(context.Request.Method))
    {
      context.Response.StatusCode = StatusCodes.Status403Forbidden;
      return;
    }

    // Send the guest back to their device-access view.
    var deviceId = user.FindFirst(UserClaimTypes.DeviceSessionScope)?.Value;
    var target = Guid.TryParse(deviceId, out _)
      ? $"/device-access/remote-control?deviceId={deviceId}"
      : "/device-access";
    context.Response.Redirect(target);
  }

  private static bool IsAllowed(PathString path)
  {
    if (!path.HasValue)
    {
      return false;
    }

    // Static files (favicon, manifest, service worker, WebAssembly payloads, appsettings, etc.).
    if (Path.HasExtension(path.Value))
    {
      return true;
    }

    foreach (var prefix in _allowedPrefixes)
    {
      if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }
}
