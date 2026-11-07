using System.Buffers.Binary;
using ControlR.DesktopClient.Common.Services.Encoders;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace ControlR.DesktopClient.Common.Tests;

public class Vp9EncoderTests
{
  private static CancellationToken Ct => TestContext.Current.CancellationToken;

  [Theory]
  [InlineData(100, "8")]
  [InlineData(75, "22")]
  [InlineData(1, "62")]
  public void BuildArguments_MapsQualityToCrf(int quality, string expectedCrf)
  {
    var arguments = Vp9Encoder.BuildArguments(new(1920, 1080, quality, null, SKColorType.Bgra8888));

    Assert.Equal(expectedCrf, ValueOf(arguments, "-crf"));
    Assert.Equal("0", ValueOf(arguments, "-b:v"));
    Assert.Equal("bgra", ValueOf(arguments, "-pixel_format"));
    Assert.Equal("1920x1080", ValueOf(arguments, "-video_size"));
  }

  [Fact]
  public void BuildArguments_MaxBandwidthSet_CapsTheBitrate()
  {
    var arguments = Vp9Encoder.BuildArguments(new(640, 480, 50, 2.5, SKColorType.Rgba8888));

    Assert.Equal("2500k", ValueOf(arguments, "-b:v"));
    Assert.Equal("rgba", ValueOf(arguments, "-pixel_format"));
  }

  [Fact]
  public void BuildArguments_NeverHoldsFramesBack()
  {
    var arguments = Vp9Encoder.BuildArguments(new(640, 480, 50, null, SKColorType.Bgra8888));

    Assert.Equal("0", ValueOf(arguments, "-lag-in-frames"));
    Assert.Equal("0", ValueOf(arguments, "-auto-alt-ref"));
    Assert.Equal("ivf", ValueOf(arguments, "-f", last: true));
  }

  [Theory]
  [InlineData(0x82, true)]
  [InlineData(0x86, false)]
  [InlineData(0x8A, false)]
  [InlineData(0x00, false)]
  public void IsKeyFrame_ReadsTheFrameHeader(byte header, bool expected)
  {
    Assert.Equal(expected, Vp9Bitstream.IsKeyFrame([header, 0x49]));
  }

  [Fact]
  public void IsKeyFrame_EmptyFrame_IsFalse()
  {
    Assert.False(Vp9Bitstream.IsKeyFrame([]));
  }

  [Fact]
  public async Task ReadFrames_SplitsFramesAndStopsAtTheEnd()
  {
    using var stream = new MemoryStream(Ivf([1, 2, 3], [4]));

    var frames = await Collect(stream);

    Assert.Equal(2, frames.Count);
    Assert.Equal([1, 2, 3], frames[0]);
    Assert.Equal([4], frames[1]);
  }

  [Fact]
  public async Task ReadFrames_EmptyStream_YieldsNothing()
  {
    using var stream = new MemoryStream();

    Assert.Empty(await Collect(stream));
  }

  [Fact]
  public async Task ReadFrames_NotIvf_Throws()
  {
    using var stream = new MemoryStream(new byte[64]);

    await Assert.ThrowsAsync<InvalidDataException>(() => Collect(stream));
  }

  [Fact]
  public async Task ReadFrames_FrameClaimingAHugeLength_Throws()
  {
    var data = Ivf();
    var header = new byte[12];
    BinaryPrimitives.WriteUInt32LittleEndian(header, uint.MaxValue);
    using var stream = new MemoryStream([.. data, .. header]);

    await Assert.ThrowsAsync<InvalidDataException>(() => Collect(stream));
  }

  [Fact]
  public async Task Encoder_RealFfmpeg_StartsWithAKeyFrameAndRestartsWithOne()
  {
    var locator = new FfmpegLocator(NullLogger<FfmpegLocator>.Instance);
    if (locator.FindVp9Capable() is null)
    {
      Assert.Skip("ffmpeg with VP9 support is not installed.");
    }

    using var encoder = new Vp9Encoder(locator, NullLogger<Vp9Encoder>.Instance);
    using var bitmap = new SKBitmap(new SKImageInfo(321, 241, SKColorType.Bgra8888, SKAlphaType.Premul));
    var settings = new StreamEncoderSettings(bitmap.Width, bitmap.Height, 50, null, bitmap.ColorType);

    encoder.Start(settings);
    Assert.True(encoder.IsRunning);

    bitmap.Erase(SKColors.Red);
    await encoder.EncodeFrame(bitmap, Ct);
    Assert.True((await encoder.Frames.ReadAsync(Ct)).IsKeyFrame);

    bitmap.Erase(SKColors.Blue);
    await encoder.EncodeFrame(bitmap, Ct);
    Assert.False((await encoder.Frames.ReadAsync(Ct)).IsKeyFrame);

    encoder.Start(settings);

    bitmap.Erase(SKColors.Green);
    await encoder.EncodeFrame(bitmap, Ct);
    Assert.True((await encoder.Frames.ReadAsync(Ct)).IsKeyFrame);
  }

  private static async Task<List<byte[]>> Collect(Stream stream)
  {
    var frames = new List<byte[]>();
    await foreach (var frame in IvfReader.ReadFrames(stream, Ct))
    {
      frames.Add(frame);
    }

    return frames;
  }

  private static byte[] Ivf(params byte[][] frames)
  {
    using var stream = new MemoryStream();
    var header = new byte[32];
    "DKIF"u8.CopyTo(header);
    BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 32);
    stream.Write(header);

    foreach (var frame in frames)
    {
      var frameHeader = new byte[12];
      BinaryPrimitives.WriteUInt32LittleEndian(frameHeader, (uint)frame.Length);
      stream.Write(frameHeader);
      stream.Write(frame);
    }

    return stream.ToArray();
  }

  private static string ValueOf(IReadOnlyList<string> arguments, string name, bool last = false)
  {
    var list = arguments.ToList();
    var index = last ? list.LastIndexOf(name) : list.IndexOf(name);
    return list[index + 1];
  }
}
