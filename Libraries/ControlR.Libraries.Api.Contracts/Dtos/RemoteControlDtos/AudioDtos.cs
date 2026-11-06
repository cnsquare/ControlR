namespace ControlR.Libraries.Api.Contracts.Dtos.RemoteControlDtos;

[MessagePackObject(keyAsPropertyName: true)]
public record RecordingNoticeDto(bool IsRecording);

[MessagePackObject(keyAsPropertyName: true)]
public record ToggleAudioDto(bool IsEnabled);

[MessagePackObject(keyAsPropertyName: true)]
public record AudioStateDto(bool IsSuccess, bool IsEnabled, string? Reason);

/// <summary>
/// A slice of the remote device's sound output as interleaved little-endian 16-bit PCM.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public record AudioChunkDto(byte[] Pcm, int SampleRate, int Channels);
