using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ControlR.Web.Server.Services.Alerts;

public interface IWebhookSender
{
  Task<bool> Send(string url, string title, IReadOnlyList<string> lines, CancellationToken cancellationToken);
}

public sealed class WebhookSender : IWebhookSender, IDisposable
{
  private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

  private readonly IOptionsMonitor<AppOptions> _appOptions;
  private readonly HttpClient _client;
  private readonly ILogger<WebhookSender> _logger;

  public WebhookSender(IOptionsMonitor<AppOptions> appOptions, ILogger<WebhookSender> logger)
  {
    _appOptions = appOptions;
    _logger = logger;

    // Addresses are checked while connecting, not when the URL is saved, because a hostname can
    // resolve somewhere else later. Redirects are off because they would skip the check.
    _client = new HttpClient(new SocketsHttpHandler
    {
      AllowAutoRedirect = false,
      ConnectCallback = Connect,
    })
    {
      Timeout = _timeout,
    };
  }

  public void Dispose()
  {
    _client.Dispose();
  }

  public async Task<bool> Send(string url, string title, IReadOnlyList<string> lines, CancellationToken cancellationToken)
  {
    if (!WebhookTargetPolicy.IsAllowedUrl(url, _appOptions.CurrentValue.AllowPrivateWebhookTargets))
    {
      _logger.LogWarning("Refused to send an alert to a webhook target that is not allowed.");
      return false;
    }

    var text = string.Join('\n', [title, .. lines]);

    try
    {
      // Slack and Teams read "text" and Discord reads "content". Anything else can use the rest.
      // Buffered so the request has a Content-Length. Not every receiver accepts a chunked body.
      using var body = new StringContent(
        JsonSerializer.Serialize(new { text, content = text, title, lines }),
        Encoding.UTF8,
        "application/json");

      using var response = await _client.PostAsync(url, body, cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        _logger.LogWarning("An alert webhook answered {StatusCode}.", (int)response.StatusCode);
      }

      return response.IsSuccessStatusCode;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
      // The URL is left out because it often contains a secret.
      _logger.LogWarning("Failed to send an alert webhook: {Reason}", ex.Message);
      return false;
    }
  }

  private async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
  {
    var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);

    var allowed = _appOptions.CurrentValue.AllowPrivateWebhookTargets
      ? addresses
      : [.. addresses.Where(WebhookTargetPolicy.IsAllowedAddress)];

    if (allowed.Length == 0)
    {
      throw new HttpRequestException("The webhook host does not resolve to an allowed address.");
    }

    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
    try
    {
      await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
      return new NetworkStream(socket, ownsSocket: true);
    }
    catch
    {
      socket.Dispose();
      throw;
    }
  }
}
