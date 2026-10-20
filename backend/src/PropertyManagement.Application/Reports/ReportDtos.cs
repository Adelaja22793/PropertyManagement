namespace PropertyManagement.Application.Reports;

/// <summary>How a column's values are shown and exported.</summary>
public enum ColumnKind { Text, Number, Money, Percent, Date, DateTime }

public sealed record ReportColumn(string Key, string Label, ColumnKind Kind);

/// <summary>A headline figure shown above the table, such as "Occupancy 87%".</summary>
public sealed record ReportFact(string Label, string Value);

/// <summary>Which filters a report takes: a date range, a property and/or a branch.</summary>
public sealed record ReportDefinition(string Key, string Title, string Description, bool UsesDates, bool UsesProperty, bool UsesBranch);

public sealed record ReportQuery(DateOnly? From, DateOnly? To, Guid? PropertyId, Guid? BranchId);

/// <summary>
/// A report as a plain table, so the screen and the CSV download show exactly the same rows. Cells hold text,
/// numbers, dates or null, in column order.
/// </summary>
public sealed record ReportTable(
    string Key,
    string Title,
    string Description,
    DateOnly? From,
    DateOnly? To,
    IReadOnlyList<ReportFact> Facts,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals,
    bool Truncated,
    DateTime GeneratedAtUtc);
