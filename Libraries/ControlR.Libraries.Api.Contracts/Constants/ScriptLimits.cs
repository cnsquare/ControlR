namespace ControlR.Libraries.Api.Contracts.Constants;

public static class ScriptLimits
{
  public const int ContentPreviewLength = 120;
  public const int DefaultTimeoutSeconds = 300;
  public const int HistoryDefaultCount = 50;
  public const int HistoryMaxCount = 100;
  public const int MaxContentLength = 256 * 1024;

  // Stdout and stderr travel in one hub message, and the server rejects messages
  // over 100,000 bytes. Two full streams must fit with room for the envelope.
  public const int MaxOutputBytesPerStream = 40 * 1024;
  public const int MaxTimeoutSeconds = 3600;
  public const int MinTimeoutSeconds = 1;
}
