using System.Buffers.Binary;
using Bitbound.SimpleMessenger;
using ControlR.DesktopClient.Common.Messages;
using ControlR.DesktopClient.Common.ServiceInterfaces;
using ControlR.DesktopClient.Common.Services;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Shared.Primitives;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlR.DesktopClient.Tests;

public class AudioStreamingTests
{
  [Fact]
  public void ConvertFloat32_AboveTwoChannels_KeepsTheFirstTwo()
  {
    var source = Floats(0.5f, -0.5f, 0.9f, 0.9f);

    var chunk = AudioPcmConverter.ConvertFloat32(source, 16_000, 4);

    Assert.NotNull(chunk);
    Assert.Equal(2, chunk.Channels);
    Assert.Equal(4, chunk.Pcm.Length);
    Assert.Equal(16384, Sample(chunk, 0), tolerance: 1f);
    Assert.Equal(-16384, Sample(chunk, 1), tolerance: 1f);
  }

  [Fact]
  public void ConvertFloat32_HighSampleRate_HalvesTheRateByAveragingFrames()
  {
    var source = Floats(0.2f, 0.4f, 0.6f, 0.8f);

    var chunk = AudioPcmConverter.ConvertFloat32(source, 48_000, 1);

    Assert.NotNull(chunk);
    Assert.Equal(24_000, chunk.SampleRate);
    Assert.Equal(2, chunk.Pcm.Length / sizeof(short));
    Assert.Equal(0.3f * short.MaxValue, Sample(chunk, 0), tolerance: 1f);
    Assert.Equal(0.7f * short.MaxValue, Sample(chunk, 1), tolerance: 1f);
  }

  [Theory]
  [InlineData(0, 2)]
  [InlineData(48_000, 0)]
  public void ConvertFloat32_InvalidFormat_ReturnsNull(int sampleRate, int channels)
  {
    Assert.Null(AudioPcmConverter.ConvertFloat32(Floats(0.5f, 0.5f), sampleRate, channels));
  }

  [Fact]
  public void ConvertFloat32_LowSampleRate_KeepsTheRate()
  {
    var chunk = AudioPcmConverter.ConvertFloat32(Floats(0.5f), 16_000, 1);

    Assert.NotNull(chunk);
    Assert.Equal(16_000, chunk.SampleRate);
  }

  [Fact]
  public void ConvertFloat32_OutOfRangeAndInvalidSamples_AreClampedSafely()
  {
    var chunk = AudioPcmConverter.ConvertFloat32(Floats(2f, -2f, float.NaN, 0.5f), 16_000, 1);

    Assert.NotNull(chunk);
    Assert.Equal(short.MaxValue, Sample(chunk, 0));
    Assert.Equal(-short.MaxValue, Sample(chunk, 1));
    Assert.Equal(0, Sample(chunk, 2));
  }

  [Fact]
  public void ConvertFloat32_PartialFrame_IsIgnored()
  {
    Assert.Null(AudioPcmConverter.ConvertFloat32(new byte[3], 48_000, 2));
  }

  [Fact]
  public void ConvertFloat32_Silence_ReturnsNull()
  {
    Assert.Null(AudioPcmConverter.ConvertFloat32(new byte[4096], 48_000, 2));
  }

  [Fact]
  public async Task SetEnabled_CaptureFails_StaysDisabledAndReportsWhy()
  {
    var capturer = new FakeAudioCapturer { StartResult = Result.Fail("No audio device.") };
    await using var sut = CreateSut(capturer, new WeakReferenceMessenger());

    var result = sut.SetEnabled(true);

    Assert.False(result.IsSuccess);
    Assert.Equal("No audio device.", result.Reason);
    Assert.False(sut.IsEnabled);
  }

  [Fact]
  public async Task SetEnabled_FloodOfChunks_NeverBlocksTheCaptureThread()
  {
    var capturer = new FakeAudioCapturer();
    var messenger = new WeakReferenceMessenger();
    var release = new TaskCompletionSource();
    messenger.Register<SendRemoteControlDtoToViewerMessage>(this, (_, _) => release.Task);
    await using var sut = CreateSut(capturer, messenger);

    sut.SetEnabled(true);

    var flood = Task.Run(() =>
    {
      for (var i = 0; i < 10_000; i++)
      {
        capturer.Emit(new AudioChunkDto([1, 2], 24_000, 1));
      }
    }, TestContext.Current.CancellationToken);

    await flood.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    release.SetResult();
  }

  [Fact]
  public async Task SetEnabled_SendsCapturedAudioUntilStopped()
  {
    var capturer = new FakeAudioCapturer();
    var messenger = new WeakReferenceMessenger();
    var received = new TaskCompletionSource<RemoteControlDtoWrapper>();
    messenger.Register<SendRemoteControlDtoToViewerMessage>(this, (_, message) =>
    {
      received.TrySetResult(message.Wrapper);
      return Task.CompletedTask;
    });
    await using var sut = CreateSut(capturer, messenger);

    Assert.True(sut.SetEnabled(true).IsSuccess);
    capturer.Emit(new AudioChunkDto([1, 2], 24_000, 1));

    var wrapper = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    Assert.Equal(RemoteControlDtoType.AudioChunk, wrapper.DtoType);
    Assert.Equal(24_000, wrapper.GetPayload<AudioChunkDto>().SampleRate);

    Assert.True(sut.SetEnabled(false).IsSuccess);
    Assert.False(sut.IsEnabled);
    Assert.True(capturer.IsStopped);
  }

  [Fact]
  public async Task SetEnabled_SameStateTwice_StartsCaptureOnce()
  {
    var capturer = new FakeAudioCapturer();
    await using var sut = CreateSut(capturer, new WeakReferenceMessenger());

    sut.SetEnabled(true);
    sut.SetEnabled(true);

    Assert.Equal(1, capturer.StartCount);
  }

  [Fact]
  public async Task Dispose_StopsCapture()
  {
    var capturer = new FakeAudioCapturer();
    var sut = CreateSut(capturer, new WeakReferenceMessenger());
    sut.SetEnabled(true);

    await sut.DisposeAsync();

    Assert.True(capturer.IsStopped);
  }

  private static AudioStreamer CreateSut(IAudioCapturer capturer, IMessenger messenger) =>
    new(capturer, messenger, NullLogger<AudioStreamer>.Instance);

  private static byte[] Floats(params float[] samples)
  {
    var bytes = new byte[samples.Length * sizeof(float)];
    for (var i = 0; i < samples.Length; i++)
    {
      BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * sizeof(float)), samples[i]);
    }

    return bytes;
  }

  private static float Sample(AudioChunkDto chunk, int index) =>
    BinaryPrimitives.ReadInt16LittleEndian(chunk.Pcm.AsSpan(index * sizeof(short)));

  private sealed class FakeAudioCapturer : IAudioCapturer
  {
    private Action<AudioChunkDto>? _onChunk;

    public bool IsStopped { get; private set; }
    public int StartCount { get; private set; }
    public Result StartResult { get; init; } = Result.Ok();

    public void Emit(AudioChunkDto chunk) => _onChunk?.Invoke(chunk);

    public Result Start(Action<AudioChunkDto> onChunk)
    {
      StartCount++;
      _onChunk = onChunk;
      return StartResult;
    }

    public void Stop() => IsStopped = true;
  }
}
