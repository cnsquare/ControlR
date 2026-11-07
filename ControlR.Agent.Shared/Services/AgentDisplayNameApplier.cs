namespace ControlR.Agent.Shared.Services;

/// <summary>
/// Applies the name people see for the agent. The internal service name, folders and files are
/// never touched, because the agent finds itself and its updates through them.
/// </summary>
public interface IAgentDisplayNameApplier
{
  // A null name restores the default.
  Task Apply(string? displayName, CancellationToken cancellationToken);
}

internal class AgentDisplayNameApplierNoOp : IAgentDisplayNameApplier
{
  public Task Apply(string? displayName, CancellationToken cancellationToken) => Task.CompletedTask;
}
