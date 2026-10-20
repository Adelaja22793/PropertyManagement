using PropertyManagement.Application.Reports;
using PropertyManagement.Domain.Maintenance;

namespace PropertyManagement.UnitTests;

public class ReportTests
{
    [Theory]
    [InlineData("Lekki", "Lekki")]
    [InlineData("12, Admiralty Way", "\"12, Admiralty Way\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+2348030000000", "'+2348030000000")]
    [InlineData("@cmd", "'@cmd")]
    public void Csv_cells_are_quoted_and_formulas_kept_as_text(string input, string expected) =>
        Assert.Equal(expected, ReportCsv.Cell(input));

    [Fact]
    public void Csv_numbers_and_dates_use_a_fixed_format()
    {
        Assert.Equal("-5", ReportCsv.Cell(-5));
        Assert.Equal("4500000.50", ReportCsv.Cell(4500000.50m));
        Assert.Equal("2026-03-01", ReportCsv.Cell(new DateOnly(2026, 3, 1)));
        Assert.Equal("", ReportCsv.Cell(null));
    }

    [Fact]
    public void Csv_has_a_header_rows_and_totals()
    {
        var table = new ReportTable("x", "X", "", null, null, [],
            [new("a", "Property", ColumnKind.Text), new("b", "Units", ColumnKind.Number)],
            [["Bourdillon Court", 4]], ["Total", 4], false, DateTime.UtcNow);
        Assert.Equal("Property,Units\r\nBourdillon Court,4\r\nTotal,4\r\n", ReportCsv.Write(table));
    }

    [Fact]
    public void Enum_labels_read_as_words() =>
        Assert.Equal("Water supply", ReportService.Label(MaintenanceCategory.WaterSupply));
}
