using ControlR.Libraries.Branding;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using ControlR.Web.Server.Constants;

namespace ControlR.Web.Server.Api.V1;

/// <summary>
/// Sends a test email to the calling user, so an administrator can verify SMTP configuration.
/// The target address comes from the caller's own user record, never from the request.
/// </summary>
[Route(HttpConstants.V1.TestEmailEndpoint)]
[ApiController]
[Authorize(Policy = PolicyNames.RequireServerSettingsWrite)]
[ApiVersion(ApiVersions.V1)]
public class TestEmailController : ControllerBase
{
  [HttpPost]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
  [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
  public async Task<IActionResult> SendTestEmail(
    [FromServices] AppDb appDb,
    [FromServices] IControlrEmailSender emailSender)
  {
    if (!User.TryGetUserId(out var userId))
    {
      return Problem(
        detail: "User ID not found",
        statusCode: StatusCodes.Status400BadRequest,
        title: V1ProblemTitles.InvalidRequest);
    }

    var user = await appDb
      .Users
      .AsNoTracking()
      .Select(x => new { x.Email, x.Id })
      .FirstOrDefaultAsync(x => x.Id == userId);

    if (user?.Email is null)
    {
      return Problem(
        detail: "User email not found",
        statusCode: StatusCodes.Status400BadRequest,
        title: V1ProblemTitles.InvalidRequest);
    }

    var result = await emailSender.SendEmailWithResult(
      user.Email,
      $"{BrandingConstants.BrandName} Test Email",
      "<h1>Test Email</h1>" +
        "<p>This is a test email from your ControlR server.</p>");

    if (result.IsSuccess)
    {
      return Ok();
    }

    // Returns the kind of failure that occurred in the EmailSender.
    return result.ToActionResult();
  }
}
