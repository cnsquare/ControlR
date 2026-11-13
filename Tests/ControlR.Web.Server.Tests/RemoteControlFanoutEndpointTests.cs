using System.Net.WebSockets;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Api.Contracts.RemoteControl;
using ControlR.Web.Server.Services.RemoteControl;
using ControlR.Web.Server.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace ControlR.Web.Server.Tests;

/// <summary>
/// Drives the shared remote-control fan-out WebSocket endpoint through a TestServer: a producer socket
/// publishes frames and a viewer socket receives them, proving the endpoint, auth, socket-to-stream
/// adapter and fan-out work together end to end in-process (no browser or desktop needed).
/// </summary>
public class RemoteControlFanoutEndpointTests(ITestOutputHelper testOutput)
{
  private readonly ITestOutputHelper _testOutput = testOutput;

  [Fact]
  public async Task Producer_Publishes_Viewer_Receives()
  {
    var ct = TestContext.Current.CancellationToken;
    using var testServer = await TestWebServerBuilder.CreateTestServer(_testOutput);

    var sessionId = Guid.NewGuid();
    testServer.Services.GetRequiredService<IRemoteControlSharedSessionStore>()
      .Register(sessionId, "producer-token", "viewer-token");

    // Producer connects first so the session's fan-out exists before the viewer joins.
    using var producerSocket = await ConnectAsync(testServer, sessionId, "producer", "producer-token", ct);
    using var viewerSocket = await ConnectAsync(testServer, sessionId, "viewer", "viewer-token", ct);

    var producer = new WebSocketStreamAdapter(producerSocket);
    await RemoteControlFanoutTransport.WriteFrameAsync(producer, Key(1), ct);
    await RemoteControlFanoutTransport.WriteFrameAsync(producer, Delta(2), ct);

    var viewer = new WebSocketStreamAdapter(viewerSocket);
    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    readTimeout.CancelAfter(TimeSpan.FromSeconds(15));

    var first = await RemoteControlFanoutTransport.ReadFrameAsync(viewer, readTimeout.Token);
    var second = await RemoteControlFanoutTransport.ReadFrameAsync(viewer, readTimeout.Token);

    Assert.NotNull(first);
    Assert.Equal(1, first!.Timestamp);
    Assert.True(first.IsKeyFrame);
    Assert.NotNull(second);
    Assert.Equal(2, second!.Timestamp);
  }

  [Fact]
  public async Task Viewer_WithProducerToken_IsRejected()
  {
    var ct = TestContext.Current.CancellationToken;
    using var testServer = await TestWebServerBuilder.CreateTestServer(_testOutput);

    var sessionId = Guid.NewGuid();
    testServer.Services.GetRequiredService<IRemoteControlSharedSessionStore>()
      .Register(sessionId, "producer-token", "viewer-token");

    // A viewer presenting the producer token (or any wrong token) must be refused the upgrade.
    await Assert.ThrowsAnyAsync<Exception>(
      async () => await ConnectAsync(testServer, sessionId, "viewer", "producer-token", ct));
  }

  [Fact]
  public async Task Connection_ToUnknownSession_IsRejected()
  {
    var ct = TestContext.Current.CancellationToken;
    using var testServer = await TestWebServerBuilder.CreateTestServer(_testOutput);

    await Assert.ThrowsAnyAsync<Exception>(
      async () => await ConnectAsync(testServer, Guid.NewGuid(), "producer", "nope", ct));
  }

  private static async Task<WebSocket> ConnectAsync(
    TestWebServer testServer,
    Guid sessionId,
    string role,
    string token,
    CancellationToken cancellationToken)
  {
    var client = testServer.TestServer.CreateWebSocketClient();
    var uri = new Uri(
      testServer.TestServer.BaseAddress,
      $"{RemoteControlFanoutMiddleware.Path}?sessionId={sessionId}&role={role}&accessToken={token}");
    return await client.ConnectAsync(uri, cancellationToken);
  }

  private static VideoStreamPacketDto Key(long ts) => new([(byte)ts], ts, IsKeyFrame: true);
  private static VideoStreamPacketDto Delta(long ts) => new([(byte)ts], ts, IsKeyFrame: false);
}
