using System.Text;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Reports;

namespace PropertyManagement.Api.Controllers;

/// <summary>
/// Operational reports (design 6.14). Each report checks its own permission, the same one as the screen it
/// summarises; <c>format=csv</c> downloads the same rows as a spreadsheet file.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
public sealed class ReportsController(ReportService reports, IClock clock) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<ReportDefinition> List() => reports.Available();

    [HttpGet("{key}")]
    public async Task<IActionResult> Run(string key, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? propertyId,
        [FromQuery] Guid? branchId, [FromQuery] string? format, CancellationToken ct)
    {
        var query = new ReportQuery(from, to, propertyId, branchId);
        var table = await reports.RunAsync(key, query, ct);
        if (!string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)) return Ok(table);

        await reports.RecordExportAsync(table, query, ct);
        var file = $"{table.Key}-{clock.TodayInNigeria():yyyy-MM-dd}.csv";
        // A byte-order mark so Excel reads the naira sign and accented names correctly.
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportCsv.Write(table))).ToArray(), "text/csv; charset=utf-8", file);
    }
}
