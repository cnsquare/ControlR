namespace ControlR.DesktopClient.Common.Services.Encoders;

public static class Vp9Bitstream
{
  /// <summary>
  /// Reads the start of a frame's uncompressed header: a 2-bit marker, the profile bits, then
  /// show_existing_frame and frame_type, where a frame_type of 0 is a key frame.
  /// </summary>
  public static bool IsKeyFrame(ReadOnlySpan<byte> frame)
  {
    if (frame.IsEmpty || frame[0] >> 6 != 0b10)
    {
      return false;
    }

    var header = frame[0];
    var profile = ((header >> 5) & 1) | (((header >> 4) & 1) << 1);

    // Profile 3 has one reserved bit before the flags.
    var showExistingBit = profile == 3 ? 2 : 3;

    var showsExistingFrame = ((header >> showExistingBit) & 1) == 1;
    var frameType = (header >> (showExistingBit - 1)) & 1;
    return !showsExistingFrame && frameType == 0;
  }
}
