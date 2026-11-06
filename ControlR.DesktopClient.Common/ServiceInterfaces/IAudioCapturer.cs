using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;
using ControlR.Libraries.Shared.Primitives;

namespace ControlR.DesktopClient.Common.ServiceInterfaces;

/// <summary>
/// Captures what the device is playing. Implementations must never capture a microphone.
/// </summary>
public interface IAudioCapturer
{
  Result Start(Action<AudioChunkDto> onChunk);
  void Stop();
}

internal sealed class UnsupportedAudioCapturer : IAudioCapturer
{
  public Result Start(Action<AudioChunkDto> onChunk) =>
    Result.Fail("Audio is not supported on this operating system.");

  public void Stop()
  {
  }
}
