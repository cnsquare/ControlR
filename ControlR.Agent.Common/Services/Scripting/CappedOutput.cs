using System.Text;

namespace ControlR.Agent.Common.Services.Scripting;

/// <summary>
/// Collects text up to a UTF-8 byte budget and records whether anything was dropped.
/// </summary>
internal sealed class CappedOutput(int maxBytes)
{
  private readonly StringBuilder _builder = new();
  private readonly Lock _lock = new();
  private readonly int _maxBytes = maxBytes;
  private int _byteCount;

  public bool IsTruncated { get; private set; }

  public void Append(ReadOnlySpan<char> text)
  {
    lock (_lock)
    {
      if (IsTruncated)
      {
        return;
      }

      var remaining = _maxBytes - _byteCount;
      var byteCount = Encoding.UTF8.GetByteCount(text);
      if (byteCount <= remaining)
      {
        _builder.Append(text);
        _byteCount += byteCount;
        return;
      }

      IsTruncated = true;

      var length = Math.Min(text.Length, remaining);
      while (length > 0 && Encoding.UTF8.GetByteCount(text[..length]) > remaining)
      {
        length--;
      }

      // Never leave half of a surrogate pair at the end.
      if (length > 0 && char.IsHighSurrogate(text[length - 1]))
      {
        length--;
      }

      _builder.Append(text[..length]);
      _byteCount += Encoding.UTF8.GetByteCount(text[..length]);
    }
  }

  public override string ToString()
  {
    lock (_lock)
    {
      return _builder.ToString();
    }
  }
}
