using System.Runtime.CompilerServices;
using ControlR.Libraries.Messenger.Extensions;

namespace ControlR.Libraries.Shared.Tests;

// These pin the behaviour of the library this replaced, which was measured before it was removed.
public class WeakReferenceMessengerTests
{
  [Fact]
  public async Task Dispose_RemovesTheRegistration_AndCanBeRepeated()
  {
    var messenger = new WeakReferenceMessenger();
    var subscriber = new object();
    var token = messenger.Register<BaseMessage>(subscriber, (_, _) => Task.CompletedTask);

    Assert.True(messenger.IsRegistered<BaseMessage>(subscriber));

    token.Dispose();
    token.Dispose();

    Assert.False(messenger.IsRegistered<BaseMessage>(subscriber));
    Assert.Empty(await messenger.Send(new BaseMessage()));
  }

  [Fact]
  public void Dispose_OldToken_LeavesANewerRegistrationAlone()
  {
    var messenger = new WeakReferenceMessenger();
    var subscriber = new object();
    var oldToken = messenger.Register<BaseMessage>(subscriber, (_, _) => Task.CompletedTask);
    oldToken.Dispose();
    messenger.Register<BaseMessage>(subscriber, (_, _) => Task.CompletedTask);

    oldToken.Dispose();

    Assert.True(messenger.IsRegistered<BaseMessage>(subscriber));
  }

  [Fact]
  public void Register_SameSubscriberAndMessageTwice_Throws()
  {
    var messenger = new WeakReferenceMessenger();
    var subscriber = new object();
    messenger.Register<BaseMessage>(subscriber, (_, _) => Task.CompletedTask);

    Assert.Throws<InvalidOperationException>(
      () => messenger.Register<BaseMessage>(subscriber, (_, _) => Task.CompletedTask));
  }

  [Fact]
  public async Task Send_DerivedMessage_OnlyReachesAHandlerForTheTypeItWasSentAs()
  {
    var messenger = new WeakReferenceMessenger();
    var subscriber = new object();
    var hits = 0;
    messenger.Register<BaseMessage>(subscriber, (_, _) =>
    {
      hits++;
      return Task.CompletedTask;
    });

    await messenger.Send(new DerivedMessage());
    Assert.Equal(0, hits);

    await messenger.Send<BaseMessage>(new DerivedMessage());
    Assert.Equal(1, hits);
  }

  [Fact]
  public async Task Send_FailingHandlers_ReturnsTheirExceptionsAndRunsTheRest()
  {
    var messenger = new WeakReferenceMessenger();
    List<string> order = [];
    object first = new(), second = new(), third = new();

    messenger.Register<BaseMessage>(first, (_, _) =>
    {
      order.Add("sync");
      throw new InvalidOperationException();
    });
    messenger.Register<BaseMessage>(second, async (_, _) =>
    {
      order.Add("async");
      await Task.Yield();
      throw new ArgumentException("failed");
    });
    messenger.Register<BaseMessage>(third, (_, _) =>
    {
      order.Add("third");
      return Task.CompletedTask;
    });

    var exceptions = await messenger.Send(new BaseMessage());

    Assert.Equal(["sync", "async", "third"], order);
    Assert.Collection(
      exceptions,
      x => Assert.IsType<InvalidOperationException>(x),
      x => Assert.IsType<ArgumentException>(x));
    GC.KeepAlive(first);
    GC.KeepAlive(second);
    GC.KeepAlive(third);
  }

  [Fact]
  public async Task Send_HandlerUnregistersItself_IsNotCalledAgain()
  {
    var messenger = new WeakReferenceMessenger();
    var subscriber = new object();
    var calls = 0;
    messenger.Register<BaseMessage>(subscriber, (sender, _) =>
    {
      calls++;
      messenger.UnregisterAll(sender);
      return Task.CompletedTask;
    });

    await messenger.Send(new BaseMessage());
    await messenger.Send(new BaseMessage());

    Assert.Equal(1, calls);
  }

  [Fact]
  public async Task Send_RunsHandlersOneAtATime()
  {
    var messenger = new WeakReferenceMessenger();
    object first = new(), second = new();
    var running = 0;
    var mostRunning = 0;

    async Task Handler(object _, BaseMessage __)
    {
      mostRunning = Math.Max(mostRunning, Interlocked.Increment(ref running));
      await Task.Delay(20, TestContext.Current.CancellationToken);
      Interlocked.Decrement(ref running);
    }

    messenger.Register<BaseMessage>(first, Handler);
    messenger.Register<BaseMessage>(second, Handler);

    await messenger.Send(new BaseMessage());

    Assert.Equal(1, mostRunning);
    GC.KeepAlive(first);
    GC.KeepAlive(second);
  }

  [Fact]
  public async Task Send_SubscriberCollected_HandlerIsGone()
  {
    var messenger = new WeakReferenceMessenger();
    var hits = 0;
    RegisterTemporarySubscriber(messenger, () => hits++);

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    await messenger.Send(new BaseMessage());

    Assert.Equal(0, hits);
  }

  [Fact]
  public void Unregister_OnlyRemovesThatMessageType()
  {
    var messenger = new WeakReferenceMessenger();
    var subscriber = new object();
    messenger.Register<BaseMessage>(subscriber, (_, _) => Task.CompletedTask);
    messenger.Register<DerivedMessage>(subscriber, (_, _) => Task.CompletedTask);

    messenger.Unregister<BaseMessage>(subscriber);

    Assert.False(messenger.IsRegistered<BaseMessage>(subscriber));
    Assert.True(messenger.IsRegistered<DerivedMessage>(subscriber));
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private static void RegisterTemporarySubscriber(IMessenger messenger, Action onMessage)
  {
    messenger.Register<BaseMessage>(new object(), (_, _) =>
    {
      onMessage();
      return Task.CompletedTask;
    });
  }

  private class BaseMessage;

  private sealed class DerivedMessage : BaseMessage;
}
