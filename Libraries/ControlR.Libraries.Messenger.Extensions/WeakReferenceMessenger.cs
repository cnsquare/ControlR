using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ControlR.Libraries.Messenger.Extensions;

public delegate Task RegistrationCallback<in TMessage>(object subscriber, TMessage message)
  where TMessage : class;

public interface IMessenger
{
  bool IsRegistered<TMessage>(object subscriber) where TMessage : class;

  /// <summary>
  /// Registers a handler for exactly this message type. Throws if the subscriber already has one.
  /// </summary>
  IDisposable Register<TMessage>(object subscriber, RegistrationCallback<TMessage> handler)
    where TMessage : class;

  /// <summary>
  /// Runs each handler in turn, in registration order. A failing handler does not stop the rest,
  /// and its exception is returned instead of thrown.
  /// </summary>
  Task<IImmutableList<Exception>> Send<TMessage>(TMessage message) where TMessage : class;

  void Unregister<TMessage>(object subscriber) where TMessage : class;

  void UnregisterAll(object subscriber);
}

/// <summary>
/// A messenger that does not keep its subscribers alive. A handler is dropped when its subscriber
/// is garbage collected, so forgetting to unregister cannot leak the subscriber.
/// </summary>
public sealed class WeakReferenceMessenger : IMessenger
{
  private readonly Lock _lock = new();
  private readonly Dictionary<Type, ConditionalWeakTable<object, Registration>> _registrations = [];

  private long _nextSequence;

  public static IMessenger Default { get; } = new WeakReferenceMessenger();

  public bool IsRegistered<TMessage>(object subscriber) where TMessage : class
  {
    using var scope = _lock.EnterScope();
    return _registrations.TryGetValue(typeof(TMessage), out var table) &&
      table.TryGetValue(subscriber, out _);
  }

  public IDisposable Register<TMessage>(object subscriber, RegistrationCallback<TMessage> handler)
    where TMessage : class
  {
    using var scope = _lock.EnterScope();

    if (!_registrations.TryGetValue(typeof(TMessage), out var table))
    {
      table = [];
      _registrations[typeof(TMessage)] = table;
    }

    var registration = new Registration(handler, _nextSequence++);
    if (!table.TryAdd(subscriber, registration))
    {
      throw new InvalidOperationException("Subscriber is already registered to the specified message.");
    }

    // Weak, because the token may be stored on something that outlives the subscriber.
    return new RegistrationToken<TMessage>(this, new WeakReference<object>(subscriber), registration);
  }

  public async Task<IImmutableList<Exception>> Send<TMessage>(TMessage message) where TMessage : class
  {
    var exceptions = ImmutableList.CreateBuilder<Exception>();

    foreach (var (subscriber, handler) in GetHandlers<TMessage>())
    {
      try
      {
        await handler(subscriber, message);
      }
      catch (Exception ex)
      {
        exceptions.Add(ex);
      }
    }

    return exceptions.ToImmutable();
  }

  public void Unregister<TMessage>(object subscriber) where TMessage : class
  {
    using var scope = _lock.EnterScope();

    if (_registrations.TryGetValue(typeof(TMessage), out var table))
    {
      table.Remove(subscriber);
    }
  }

  public void UnregisterAll(object subscriber)
  {
    using var scope = _lock.EnterScope();

    foreach (var table in _registrations.Values)
    {
      table.Remove(subscriber);
    }
  }

  private List<(object Subscriber, RegistrationCallback<TMessage> Handler)> GetHandlers<TMessage>()
    where TMessage : class
  {
    using var scope = _lock.EnterScope();

    if (!_registrations.TryGetValue(typeof(TMessage), out var table))
    {
      return [];
    }

    // Copied so a handler can register or unregister while the message is being delivered.
    return
    [
      .. table
        .OrderBy(x => x.Value.Sequence)
        .Select(x => (x.Key, (RegistrationCallback<TMessage>)x.Value.Handler))
    ];
  }

  private void Remove<TMessage>(object subscriber, Registration registration) where TMessage : class
  {
    using var scope = _lock.EnterScope();

    // The subscriber may have registered again since this token was issued.
    if (_registrations.TryGetValue(typeof(TMessage), out var table) &&
        table.TryGetValue(subscriber, out var current) &&
        ReferenceEquals(current, registration))
    {
      table.Remove(subscriber);
    }
  }

  private sealed record Registration(Delegate Handler, long Sequence);

  private sealed class RegistrationToken<TMessage>(
    WeakReferenceMessenger messenger,
    WeakReference<object> subscriber,
    Registration registration) : IDisposable
    where TMessage : class
  {
    public void Dispose()
    {
      if (subscriber.TryGetTarget(out var target))
      {
        messenger.Remove<TMessage>(target, registration);
      }
    }
  }
}
