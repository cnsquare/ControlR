using ControlR.Web.Client.Services;

namespace ControlR.Web.Client.Tests;

public class CsvBuilderTests
{
  [Theory]
  [InlineData("=1+1", "'=1+1")]
  [InlineData("+cmd", "'+cmd")]
  [InlineData("-2", "'-2")]
  [InlineData("@SUM(A1)", "'@SUM(A1)")]
  public void Build_CellThatASpreadsheetWouldRun_IsNeutralized(string value, string expected)
  {
    var csv = CsvBuilder.Build(["A"], [[value]]);

    Assert.Equal($"A\r\n{expected}\r\n", csv);
  }

  [Fact]
  public void Build_NullCell_IsEmpty()
  {
    var csv = CsvBuilder.Build(["A", "B"], [[null, "x"]]);

    Assert.Equal("A,B\r\n,x\r\n", csv);
  }

  [Fact]
  public void Build_ValueWithCommaQuoteAndNewline_IsQuoted()
  {
    var csv = CsvBuilder.Build(["A"], [["one, \"two\"\nthree"]]);

    Assert.Equal("A\r\n\"one, \"\"two\"\"\nthree\"\r\n", csv);
  }
}
