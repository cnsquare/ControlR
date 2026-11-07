using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace ControlR.DesktopClient.Common.Services.Encoders;

/// <summary>
/// Splits ffmpeg's IVF output into frames. IVF is used because it marks where each frame ends,
/// which a browser decoder needs and a WebM stream does not provide without a parser.
/// </summary>
public static class IvfReader
{
  private const int FileHeaderLength = 32;
  private const int FrameHeaderLength = 12;
  private const int MaxFrameLength = 64 * 1024 * 1024;

  public static async IAsyncEnumerable<byte[]> ReadFrames(
    Stream stream,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var fileHeader = new byte[FileHeaderLength];
    var read = await stream.ReadAtLeastAsync(fileHeader, FileHeaderLength, throwOnEndOfStream: false, cancellationToken);
    if (read == 0)
    {
      yield break;
    }

    if (read < FileHeaderLength || !fileHeader.AsSpan(0, 4).SequenceEqual("DKIF"u8))
    {
      throw new InvalidDataException("The stream is not IVF.");
    }

    var declaredLength = BinaryPrimitives.ReadUInt16LittleEndian(fileHeader.AsSpan(6, 2));
    if (declaredLength > FileHeaderLength)
    {
      await stream.ReadExactlyAsync(new byte[declaredLength - FileHeaderLength], cancellationToken);
    }

    var frameHeader = new byte[FrameHeaderLength];
    while (true)
    {
      read = await stream.ReadAtLeastAsync(frameHeader, FrameHeaderLength, throwOnEndOfStream: false, cancellationToken);
      if (read < FrameHeaderLength)
      {
        yield break;
      }

      var length = BinaryPrimitives.ReadUInt32LittleEndian(frameHeader);
      if (length > MaxFrameLength)
      {
        throw new InvalidDataException($"An IVF frame claims {length} bytes.");
      }

      var frame = new byte[length];
      await stream.ReadExactlyAsync(frame, cancellationToken);
      yield return frame;
    }
  }
}
