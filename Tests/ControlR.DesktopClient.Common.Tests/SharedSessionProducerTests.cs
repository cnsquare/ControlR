using ControlR.DesktopClient.Common.Services;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlR.DesktopClient.Common.Tests;

/// <summary>
/// The producer's most important contract is that it is inert until a shared session starts: the tee
/// call in the capture loop must be a safe no-op so the normal one-to-one remote-control path cannot
/// be disturbed. The live connect/pump path is validated against a running server on staging.
/// </summary>
public class SharedSessionProducerTests
{
  [Fact]
  public async Task IsActive_IsFalse_BeforeAnySessionStarts()
  {
    await using var producer = new SharedSessionProducer(NullLogger<SharedSessionProducer>.Instance);
    Assert.False(producer.IsActive);
  }

  [Fact]
  public async Task Publish_WhenIdle_IsANoOpAndNeverThrows()
  {
    await using var producer = new SharedSessionProducer(NullLogger<SharedSessionProducer>.Instance);

    var packet = new VideoStreamPacketDto([1, 2, 3], Timestamp: 1, IsKeyFrame: true);

    // Many publishes while idle must not throw, allocate a connection, or flip the active flag.
    for (var i = 0; i < 1000; i++)
    {
      producer.Publish(packet);
    }

    Assert.False(producer.IsActive);
  }

  [Fact]
  public async Task StopAsync_WhenNeverStarted_IsSafe()
  {
    var producer = new SharedSessionProducer(NullLogger<SharedSessionProducer>.Instance);

    // Stopping an idle producer, and disposing it, must be safe and idempotent.
    await producer.StopAsync();
    await producer.StopAsync();
    await producer.DisposeAsync();
  }
}
