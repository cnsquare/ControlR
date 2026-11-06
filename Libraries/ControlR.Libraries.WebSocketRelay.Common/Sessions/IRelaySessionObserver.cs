namespace ControlR.Libraries.WebSocketRelay.Common.Sessions;

/// <summary>
/// Optional hook for hosts that need to know when a relay session is over.
/// </summary>
public interface IRelaySessionObserver
{
  /// <summary>
  /// Called once per peer, so a paired session reports twice.
  /// </summary>
  /// <param name="sessionId">The relay session.</param>
  /// <param name="wasConnected">False if this peer's partner never arrived.</param>
  Task OnPeerDisconnected(Guid sessionId, bool wasConnected);
}
