namespace ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

/// <summary>
/// One encoded video frame. A decoder can only start from a key frame.
/// </summary>
[MessagePackObject]
public record VideoStreamPacketDto(
  [property: Key(0)]
  byte[] PacketData,
  [property: Key(1)]
  long Timestamp,
  [property: Key(2)]
  bool IsKeyFrame = false);
