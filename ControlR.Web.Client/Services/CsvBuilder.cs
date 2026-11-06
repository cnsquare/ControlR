using System.Text;

namespace ControlR.Web.Client.Services;

public static class CsvBuilder
{
  public static string Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
  {
    var builder = new StringBuilder();
    AppendRow(builder, headers);

    foreach (var row in rows)
    {
      AppendRow(builder, row);
    }

    return builder.ToString();
  }

  private static void AppendRow(StringBuilder builder, IEnumerable<string?> cells)
  {
    builder.AppendJoin(',', cells.Select(Escape));
    builder.Append("\r\n");
  }

  private static string Escape(string? value)
  {
    if (string.IsNullOrEmpty(value))
    {
      return string.Empty;
    }

    // Device names and script output are not trusted, and a spreadsheet runs a cell that
    // starts with one of these as a formula.
    if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
    {
      value = $"'{value}";
    }

    return value.IndexOfAny(['"', ',', '\r', '\n']) >= 0
      ? $"\"{value.Replace("\"", "\"\"")}\""
      : value;
  }
}
