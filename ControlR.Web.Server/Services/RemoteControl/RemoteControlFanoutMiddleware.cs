using System.Net.WebSockets;
using ControlR.Libraries.Api.Contracts.RemoteControl;

namespace ControlR.Web.Server.Services.RemoteControl;

/// <summary>
/// The WebSocket endpoint for shared remote-control (fan-out) sessions. A desktop connects with
/// role=producer and the producer token to publish its one encoded stream; technicians connect with
/// role=viewer and the viewer token to watch it. The socket is validated against the
/// <see cref="IRemoteControlSharedSessionStore"/>, wrapped as a stream, and handed to the
/// <see cref="RemoteControlFanoutConnectionHandler"/>. Mapped under
/// <see cref="Path"/> with UseWebSockets by the host.
/// </summary>
public sealed class RemoteControlFanoutMiddleware(
  RequestDelegate next,
  ILogger<RemoteControlFanoutMiddleware> logger)
{
  public const string Path = RemoteControlFanoutPaths.Path;

  private const string ProducerRole = "producer";
  private const string ViewerRole = "viewer";

  private readonly ILogger<RemoteControlFanoutMiddleware> _logger = logger;
  private readonly RequestDelegate _next = next;

  public async Task InvokeAsync(
    HttpContext context,
    IRemoteControlSharedSessionStore sessionStore,
    RemoteControlFanoutConnectionHandler handler)
  {
    if (!context.WebSockets.IsWebSocketRequest)
    {
      await _next(context);
      return;
    }

    var query = context.Request.Query;
    if (!Guid.TryParse(query["sessionId"], out var sessionId) ||
        query["role"].ToString() is not { Length: > 0 } role ||
        query["accessToken"].ToString() is not { Length: > 0 } accessToken)
    {
      context.Response.StatusCode = StatusCodes.Status400BadRequest;
      return;
    }

    var isProducer = string.Equals(role, ProducerRole, StringComparison.OrdinalIgnoreCase);
    var isViewer = string.Equals(role, ViewerRole, StringComparison.OrdinalIgnoreCase);
    if (!isProducer && !isViewer)
    {
      context.Response.StatusCode = StatusCodes.Status400BadRequest;
      return;
    }

    var authorized = isProducer
      ? sessionStore.ValidateProducer(sessionId, accessToken)
      : sessionStore.ValidateViewer(sessionId, accessToken);

    if (!authorized)
    {
      _logger.LogWarning("Unauthorized fan-out {Role} connection for session {SessionId}.", role, sessionId);
      context.Response.StatusCode = StatusCodes.Status401Unauthorized;
      return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var transport = new WebSocketStreamAdapter(webSocket);

    try
    {
      if (isProducer)
      {
        await handler.HandleProducerAsync(transport, sessionId, context.RequestAborted);
      }
      else
      {
        await handler.HandleViewerAsync(transport, sessionId, Guid.NewGuid(), context.RequestAborted);
      }
    }
    catch (OperationCanceledException)
    {
      // Connection aborted; nothing to do.
    }
    catch (WebSocketException ex)
    {
      _logger.LogInformation(ex, "Fan-out {Role} websocket closed for session {SessionId}.", role, sessionId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error on fan-out {Role} connection for session {SessionId}.", role, sessionId);
    }
    finally
    {
      await CloseIfOpenAsync(webSocket);
    }
  }

  private static async Task CloseIfOpenAsync(WebSocket webSocket)
  {
    if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
    {
      try
      {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended.", cts.Token);
      }
      catch
      {
        // Best effort.
      }
    }
  }
}
