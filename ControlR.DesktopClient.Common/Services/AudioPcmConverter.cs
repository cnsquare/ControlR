using System.Buffers.Binary;
using ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

namespace ControlR.DesktopClient.Common.Services;

/// <summary>
/// Turns captured 32-bit float audio into the compact PCM sent to the viewer.
/// </summary>
public static class AudioPcmConverter
{
  private const int BytesPerFloat = sizeof(float);
  private const int HighSampleRateThreshold = 44_100;
  private const int MaxOutputChannels = 2;

  /// <summary>
  /// Returns null when the input holds no whole frame or is pure silence, so idle sessions send nothing.
  /// </summary>
  public static AudioChunkDto? ConvertFloat32(ReadOnlySpan<byte> source, int sampleRate, int channels)
  {
    if (channels < 1 || sampleRate < 1)
    {
      return null;
    }

    var outputChannels = Math.Min(channels, MaxOutputChannels);

    // Halving the rate halves the bandwidth. Averaging each pair of frames rather than
    // dropping one acts as a basic low-pass filter, which keeps aliasing down.
    var framesPerOutput = sampleRate >= HighSampleRateThreshold ? 2 : 1;

    var sourceFrameBytes = channels * BytesPerFloat;
    var outputFrames = source.Length / sourceFrameBytes / framesPerOutput;
    if (outputFrames == 0)
    {
      return null;
    }

    var pcm = new byte[outputFrames * outputChannels * sizeof(short)];
    var hasSound = false;

    for (var frame = 0; frame < outputFrames; frame++)
    {
      for (var channel = 0; channel < outputChannels; channel++)
      {
        var sum = 0f;
        for (var offset = 0; offset < framesPerOutput; offset++)
        {
          var position = ((frame * framesPerOutput + offset) * channels + channel) * BytesPerFloat;
          sum += BinaryPrimitives.ReadSingleLittleEndian(source.Slice(position, BytesPerFloat));
        }

        var sample = ToInt16(sum / framesPerOutput);
        hasSound |= sample != 0;

        var destination = (frame * outputChannels + channel) * sizeof(short);
        BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(destination, sizeof(short)), sample);
      }
    }

    return hasSound
      ? new AudioChunkDto(pcm, sampleRate / framesPerOutput, outputChannels)
      : null;
  }

  private static short ToInt16(float sample)
  {
    // Float audio can overshoot full scale, and NaN must not reach the cast.
    var clamped = float.IsNaN(sample) ? 0f : Math.Clamp(sample, -1f, 1f);
    return (short)MathF.Round(clamped * short.MaxValue);
  }
}
