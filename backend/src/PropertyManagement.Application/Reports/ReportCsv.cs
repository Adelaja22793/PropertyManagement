using System.Globalization;
using System.Text;

namespace PropertyManagement.Application.Reports;

/// <summary>Writes a report as CSV (RFC 4180).</summary>
public static class ReportCsv
{
    public static string Write(ReportTable table)
    {
        var sb = new StringBuilder();
        Line(sb, table.Columns.Select(c => (object?)c.Label));
        foreach (var row in table.Rows) Line(sb, row);
        if (table.Totals is not null) Line(sb, table.Totals);
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, IEnumerable<object?> cells)
    {
        sb.AppendJoin(',', cells.Select(Cell));
        sb.Append("\r\n");
    }

    public static string Cell(object? value)
    {
        var text = value switch
        {
            null => "",
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime t => t.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        // Text that a spreadsheet would run as a formula is prefixed so it stays text (CSV injection).
        if (value is string && text.Length > 0 && "=+-@\t\r".Contains(text[0])) text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }
}
