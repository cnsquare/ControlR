using ControlR.Web.Server.Services.RemoteControl;

namespace ControlR.Web.Server.Tests;

public class RemoteControlSharedSessionManagerTests
{
  [Fact]
  public void StartSession_RegistersDistinctProducerAndViewerTokens()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var store = new RemoteControlSharedSessionStore();
    var manager = new RemoteControlSharedSessionManager(store, registry);

    var grant = manager.StartSession();

    Assert.NotEqual(Guid.Empty, grant.SessionId);
    Assert.False(string.IsNullOrWhiteSpace(grant.ProducerToken));
    Assert.False(string.IsNullOrWhiteSpace(grant.ViewerToken));
    Assert.NotEqual(grant.ProducerToken, grant.ViewerToken);

    // The returned tokens authorize their own role and not the other.
    Assert.True(store.ValidateProducer(grant.SessionId, grant.ProducerToken));
    Assert.True(store.ValidateViewer(grant.SessionId, grant.ViewerToken));
    Assert.False(store.ValidateViewer(grant.SessionId, grant.ProducerToken));
    Assert.False(store.ValidateProducer(grant.SessionId, grant.ViewerToken));
  }

  [Fact]
  public void StartSession_MintsAFreshSessionEachTime()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var store = new RemoteControlSharedSessionStore();
    var manager = new RemoteControlSharedSessionManager(store, registry);

    var first = manager.StartSession();
    var second = manager.StartSession();

    Assert.NotEqual(first.SessionId, second.SessionId);
    Assert.NotEqual(first.ProducerToken, second.ProducerToken);
    Assert.NotEqual(first.ViewerToken, second.ViewerToken);
  }

  [Fact]
  public void EndSession_RevokesTokensAndDropsTheLiveFanout()
  {
    using var registry = new RemoteControlFanoutRegistry();
    var store = new RemoteControlSharedSessionStore();
    var manager = new RemoteControlSharedSessionManager(store, registry);

    var grant = manager.StartSession();
    // Simulate a connected producer + viewer.
    var fanout = registry.GetOrCreate(grant.SessionId);
    var viewer = fanout.Subscribe(Guid.NewGuid());

    manager.EndSession(grant.SessionId);

    Assert.False(store.ValidateProducer(grant.SessionId, grant.ProducerToken));
    Assert.False(store.ValidateViewer(grant.SessionId, grant.ViewerToken));
    Assert.False(registry.TryGet(grant.SessionId, out _));
    Assert.True(viewer.Completion.IsCompleted);
  }
}
