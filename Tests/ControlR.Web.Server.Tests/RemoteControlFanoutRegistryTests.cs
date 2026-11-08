using ControlR.Web.Server.Services.RemoteControl;

namespace ControlR.Web.Server.Tests;

public class RemoteControlFanoutRegistryTests
{
  [Fact]
  public void GetOrCreate_IsIdempotentPerSession_AndDistinctAcrossSessions()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var sessionA = Guid.NewGuid();
    var sessionB = Guid.NewGuid();

    var a1 = registry.GetOrCreate(sessionA);
    var a2 = registry.GetOrCreate(sessionA);
    var b = registry.GetOrCreate(sessionB);

    Assert.Same(a1, a2);
    Assert.NotSame(a1, b);
    Assert.Equal(2, registry.ActiveSessionCount);
  }

  [Fact]
  public void TryGet_ReturnsCreatedFanout_AndFalseForUnknown()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var sessionId = Guid.NewGuid();
    var created = registry.GetOrCreate(sessionId);

    Assert.True(registry.TryGet(sessionId, out var found));
    Assert.Same(created, found);
    Assert.False(registry.TryGet(Guid.NewGuid(), out _));
  }

  [Fact]
  public void Remove_DisposesFanout_AndEndsViewers()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var sessionId = Guid.NewGuid();
    var fanout = registry.GetOrCreate(sessionId);
    var viewer = fanout.Subscribe(Guid.NewGuid());

    registry.Remove(sessionId);

    Assert.False(registry.TryGet(sessionId, out _));
    Assert.True(viewer.Completion.IsCompleted);
    Assert.Equal(0, registry.ActiveSessionCount);
  }

  [Fact]
  public void Remove_UnknownSession_IsNoOp()
  {
    using var registry = new RemoteControlFanoutRegistry();
    registry.Remove(Guid.NewGuid());
    Assert.Equal(0, registry.ActiveSessionCount);
  }

  [Fact]
  public void GetOrCreate_IsThreadSafe_ReturnsSingleInstance()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var sessionId = Guid.NewGuid();

    var results = new RemoteControlSessionFanout[32];
    Parallel.For(0, results.Length, i => results[i] = registry.GetOrCreate(sessionId));

    Assert.All(results, x => Assert.Same(results[0], x));
    Assert.Equal(1, registry.ActiveSessionCount);
  }

  [Fact]
  public void Dispose_RemovesAndDisposesAllSessions()
  {
    var registry = new RemoteControlFanoutRegistry();
    var viewer = registry.GetOrCreate(Guid.NewGuid()).Subscribe(Guid.NewGuid());
    registry.GetOrCreate(Guid.NewGuid());

    registry.Dispose();

    Assert.Equal(0, registry.ActiveSessionCount);
    Assert.True(viewer.Completion.IsCompleted);
  }
}
