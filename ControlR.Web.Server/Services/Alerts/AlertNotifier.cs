using System.Net;

namespace ControlR.Web.Server.Services.Alerts;

public interface IAlertNotifier
{
  /// <summary>
  /// Sends to the tenant's email address and webhook, whichever are set. Returns false if any of
  /// them failed. Never throws, because a notification problem must not stop alert processing.
  /// </summary>
  Task<bool> Notify(AlertSettings settings, string title, IReadOnlyList<string> lines, CancellationToken cancellationToken);
}

public class AlertNotifier(
  IControlrEmailSender emailSender,
  IWebhookSender webhookSender,
  ILogger<AlertNotifier> logger) : IAlertNotifier
{
  private readonly IControlrEmailSender _emailSender = emailSender;
  private readonly ILogger<AlertNotifier> _logger = logger;
  private readonly IWebhookSender _webhookSender = webhookSender;

  public async Task<bool> Notify(
    AlertSettings settings,
    string title,
    IReadOnlyList<string> lines,
    CancellationToken cancellationToken)
  {
    var succeeded = true;

    if (!string.IsNullOrWhiteSpace(settings.WebhookUrl))
    {
      succeeded &= await _webhookSender.Send(settings.WebhookUrl, title, lines, cancellationToken);
    }

    if (!string.IsNullOrWhiteSpace(settings.Email))
    {
      succeeded &= await TrySendEmail(settings.Email, title, lines);
    }

    return succeeded;
  }

  private async Task<bool> TrySendEmail(string address, string title, IReadOnlyList<string> lines)
  {
    // Encoded because a device chooses its own name.
    var body = string.Concat(lines.Select(x => $"<p>{WebUtility.HtmlEncode(x)}</p>"));

    try
    {
      var result = await _emailSender.SendEmailWithResult(address, title, body);
      if (!result.IsSuccess)
      {
        _logger.LogWarning("Failed to send an alert email.");
      }

      return result.IsSuccess;
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error while sending an alert email.");
      return false;
    }
  }
}
