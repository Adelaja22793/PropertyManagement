using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Abstractions;
using PropertyManagement.Application.Authorization;
using PropertyManagement.Application.Common;
using PropertyManagement.Application.Listings;
using PropertyManagement.Application.Properties;
using PropertyManagement.Domain.Auditing;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Leases;
using PropertyManagement.Domain.Listings;
using PropertyManagement.Domain.Maintenance;
using PropertyManagement.Domain.Properties;

namespace PropertyManagement.Application.Reports;

/// <summary>
/// The operational reports from design 6.14 that involve no money. Each report needs the same permission as the
/// screen it summarises and sees only the properties the member can see, so a report never shows more than the
/// screens would. Financial reports (rent roll, arrears, statements and so on) come with the finance work.
/// </summary>
public sealed partial class ReportService(
    IAppDbContext db,
    PropertyAccess access,
    ListingService listings,
    ICurrentOrganization currentOrganization,
    IUserDirectory users,
    IAuditLogger audit,
    IClock clock)
{
    /// <summary>Rows beyond this are left out; the report says so.</summary>
    public const int MaxRows = 5000;
    private const int MaxRangeDays = 366;

    private sealed record Report(ReportDefinition Definition, string Permission);

    private static readonly Report[] Reports =
    [
        new(new("occupancy", "Occupancy and vacancy", "Units let, vacant and unavailable in each property.", false, true, true),
            Permissions.Properties.View),
        new(new("units", "Property and unit register", "Every unit with its rent, who is in it and how long empty units have been vacant.", false, true, true),
            Permissions.Properties.View),
        new(new("lease-expiries", "Lease expiry and renewals", "Active leases ending in the period, with renewal and notice status.", true, true, true),
            Permissions.Leases.View),
        new(new("notices", "Notices served", "Notices served on tenants in the period.", true, true, true),
            Permissions.Leases.View),
        new(new("maintenance", "Maintenance", "Repairs opened and completed in the period, by category, and how long they took.", true, true, true),
            Permissions.Maintenance.View),
        new(new("letting", "Letting activity", "Enquiries, viewings and outcomes for each listing in the period.", true, true, true),
            Permissions.Listings.Leads),
        new(new("activity", "User activity", "Actions recorded and refused for each person in the period.", true, false, false),
            Permissions.Audit.View),
    ];

    private DateOnly Today => clock.TodayInNigeria();

    /// <summary>The reports this member may run.</summary>
    public IReadOnlyList<ReportDefinition> Available() =>
        Reports.Where(r => currentOrganization.HasPermission(r.Permission)).Select(r => r.Definition).ToList();

    public async Task<ReportTable> RunAsync(string key, ReportQuery query, CancellationToken ct)
    {
        var report = Reports.SingleOrDefault(r => r.Definition.Key == key) ?? throw new NotFoundException("Report");
        if (!currentOrganization.HasPermission(report.Permission)) throw new ForbiddenException("You can't run this report.");
        return key switch
        {
            "occupancy" => await OccupancyAsync(report.Definition, query, ct),
            "units" => await UnitsAsync(report.Definition, query, ct),
            "lease-expiries" => await LeaseExpiriesAsync(report.Definition, Range(query, forward: true), query, ct),
            "notices" => await NoticesAsync(report.Definition, Range(query, forward: false), query, ct),
            "maintenance" => await MaintenanceAsync(report.Definition, Range(query, forward: false), query, ct),
            "letting" => await LettingAsync(report.Definition, Range(query, forward: false), query, ct),
            "activity" => await ActivityAsync(report.Definition, Range(query, forward: false), ct),
            _ => throw new NotFoundException("Report"),
        };
    }

    /// <summary>Records a download. Exports are audited because they take data out of the system.</summary>
    public Task RecordExportAsync(ReportTable table, ReportQuery query, CancellationToken ct) =>
        audit.RecordNowAsync("report.exported", "Report", table.Key,
            details: new { query.From, query.To, query.PropertyId, query.BranchId, Rows = table.Rows.Count }, ct: ct);

    // Ranges default to the next 90 days for things ahead (expiries) and the last 90 days for things done.
    private (DateOnly From, DateOnly To) Range(ReportQuery q, bool forward)
    {
        var today = Today;
        var from = q.From ?? (forward ? today : (q.To ?? today).AddDays(-89));
        var to = q.To ?? (forward ? from.AddDays(89) : today);
        if (to < from) throw new DomainException("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber >= MaxRangeDays) throw new DomainException("Choose a period of a year or less.");
        return (from, to);
    }

    private IQueryable<Property> Properties(ReportQuery q)
    {
        var properties = access.VisibleProperties();
        if (q.PropertyId is { } p) properties = properties.Where(x => x.Id == p);
        if (q.BranchId is { } b) properties = properties.Where(x => x.BranchId == b);
        return properties;
    }

    private async Task<ReportTable> OccupancyAsync(ReportDefinition d, ReportQuery q, CancellationToken ct)
    {
        var rows = await Properties(q).Where(p => p.Status != PropertyStatus.Archived)
            .OrderBy(p => p.Name).Take(MaxRows + 1)
            .Select(p => new
            {
                p.Code, p.Name,
                Branch = db.Branches.Where(b => b.Id == p.BranchId).Select(b => b.Name).FirstOrDefault(),
                Units = db.Units.Where(u => u.PropertyId == p.Id && !u.IsArchived).Select(u => u.OccupancyStatus).ToList(),
            }).ToListAsync(ct);
        var truncated = rows.Count > MaxRows;
        rows = rows.Take(MaxRows).ToList();

        static int Count(IEnumerable<OccupancyStatus> s, OccupancyStatus x) => s.Count(u => u == x);
        static decimal? Rate(int occupied, int lettable) => lettable == 0 ? null : Math.Round(100m * occupied / lettable, 1);
        var table = rows.Select(r =>
        {
            var occupied = Count(r.Units, OccupancyStatus.Occupied);
            var unavailable = Count(r.Units, OccupancyStatus.Unavailable);
            return (IReadOnlyList<object?>)[r.Code, r.Name, r.Branch, r.Units.Count, occupied, Count(r.Units, OccupancyStatus.Reserved),
                Count(r.Units, OccupancyStatus.Vacant), unavailable, Rate(occupied, r.Units.Count - unavailable)];
        }).ToList();
        var all = rows.SelectMany(r => r.Units).ToList();
        var totalOccupied = Count(all, OccupancyStatus.Occupied);
        var totalUnavailable = Count(all, OccupancyStatus.Unavailable);
        var rate = Rate(totalOccupied, all.Count - totalUnavailable);
        return Table(d, null, null,
            [
                new("Occupancy", rate is null ? "–" : $"{rate}%"),
                new("Vacant units", Count(all, OccupancyStatus.Vacant).ToString(CultureInfo.InvariantCulture)),
                new("Properties", rows.Count.ToString(CultureInfo.InvariantCulture)),
            ],
            [
                Text("code", "Code"), Text("property", "Property"), Text("branch", "Branch"), Number("units", "Units"),
                Number("occupied", "Occupied"), Number("reserved", "Reserved"), Number("vacant", "Vacant"),
                Number("unavailable", "Unavailable"), new("occupancy", "Occupancy", ColumnKind.Percent),
            ],
            table,
            ["Total", null, null, all.Count, totalOccupied, Count(all, OccupancyStatus.Reserved), Count(all, OccupancyStatus.Vacant),
                totalUnavailable, rate],
            truncated);
    }

    private async Task<ReportTable> UnitsAsync(ReportDefinition d, ReportQuery q, CancellationToken ct)
    {
        var today = Today;
        var withLeases = currentOrganization.HasPermission(Permissions.Leases.View);
        var properties = Properties(q);
        var units = await db.Units.Where(u => !u.IsArchived && properties.Any(p => p.Id == u.PropertyId))
            .Join(db.Properties, u => u.PropertyId, p => p.Id, (u, p) => new { Unit = u, PropertyName = p.Name })
            .OrderBy(x => x.PropertyName).ThenBy(x => x.Unit.Code).Take(MaxRows + 1)
            .ToListAsync(ct);
        var truncated = units.Count > MaxRows;
        units = units.Take(MaxRows).ToList();
        var unitIds = units.Select(u => u.Unit.Id).ToList();

        var current = new Dictionary<Guid, (string Reference, string? Tenant, DateOnly EndDate)>();
        if (withLeases)
        {
            var leases = await db.Leases.Where(l => unitIds.Contains(l.UnitId) && l.Status == LeaseStatus.Active && l.StartDate <= today)
                .Select(l => new
                {
                    l.UnitId, l.Number, l.EndDate,
                    Tenant = l.Tenants.Where(t => t.IsPrimary).Select(t => db.Tenants.Where(x => x.Id == t.TenantId).Select(x => x.Name).First())
                        .FirstOrDefault(),
                }).ToListAsync(ct);
            foreach (var l in leases) current[l.UnitId] = (Lease.FormatReference(l.Number), l.Tenant, l.EndDate);
        }

        // An empty unit has been vacant since its last tenancy ended, or since it was added.
        var lastEnded = await db.Leases.Where(l => unitIds.Contains(l.UnitId) && (l.Status == LeaseStatus.Ended || l.Status == LeaseStatus.Terminated))
            .GroupBy(l => l.UnitId)
            .Select(g => new { UnitId = g.Key, Last = g.Max(l => l.MoveOutDate ?? l.EndDate) })
            .ToDictionaryAsync(x => x.UnitId, x => x.Last, ct);

        var rows = units.Select(x =>
        {
            var u = x.Unit;
            DateOnly? vacantSince = null;
            if (u.OccupancyStatus == OccupancyStatus.Vacant)
                vacantSince = lastEnded.TryGetValue(u.Id, out var ended) ? ended : DateOnly.FromDateTime(u.CreatedAtUtc.AddHours(1));
            current.TryGetValue(u.Id, out var lease);
            List<object?> row = [x.PropertyName, u.Code, Label(u.Type), u.Bedrooms, u.DefaultRent, Label(u.BillingFrequency), Label(u.OccupancyStatus)];
            if (withLeases) row.AddRange([lease.Reference, lease.Tenant, lease.Reference is null ? null : lease.EndDate]);
            row.AddRange([vacantSince, vacantSince is { } v ? Math.Max(0, today.DayNumber - v.DayNumber) : null]);
            return (IReadOnlyList<object?>)row;
        }).ToList();

        List<ReportColumn> columns =
        [
            Text("property", "Property"), Text("unit", "Unit"), Text("type", "Type"), Number("bedrooms", "Bedrooms"),
            Money("rent", "Default rent"), Text("frequency", "Paid"), Text("occupancy", "Occupancy"),
        ];
        if (withLeases) columns.AddRange([Text("lease", "Current lease"), Text("tenant", "Tenant"), Date("leaseEnds", "Lease ends")]);
        columns.AddRange([Date("vacantSince", "Vacant since"), Number("daysVacant", "Days vacant")]);

        var vacant = units.Count(u => u.Unit.OccupancyStatus == OccupancyStatus.Vacant);
        return Table(d, null, null,
            [new("Units", units.Count.ToString(CultureInfo.InvariantCulture)), new("Vacant", vacant.ToString(CultureInfo.InvariantCulture))],
            columns, rows, null, truncated);
    }

    private async Task<ReportTable> LeaseExpiriesAsync(ReportDefinition d, (DateOnly From, DateOnly To) range, ReportQuery q, CancellationToken ct)
    {
        var today = Today;
        var properties = Properties(q);
        var leases = await db.Leases
            .Where(l => l.Status == LeaseStatus.Active && l.EndDate >= range.From && l.EndDate <= range.To && properties.Any(p => p.Id == l.PropertyId))
            .OrderBy(l => l.EndDate).ThenBy(l => l.Number).Take(MaxRows + 1)
            .Select(l => new
            {
                l.Number, l.EndDate, l.RentAmount, l.BillingFrequency,
                Property = db.Properties.Where(p => p.Id == l.PropertyId).Select(p => p.Name).First(),
                Unit = db.Units.Where(u => u.Id == l.UnitId).Select(u => u.Code).First(),
                Tenant = l.Tenants.Where(t => t.IsPrimary).Select(t => db.Tenants.Where(x => x.Id == t.TenantId).Select(x => x.Name).First())
                    .FirstOrDefault(),
                Renewal = db.Leases.Where(r => r.PreviousLeaseId == l.Id && r.Status != LeaseStatus.Cancelled)
                    .Select(r => new { r.Number, r.Status }).FirstOrDefault(),
                Notice = db.LeaseNotices.Where(n => n.LeaseId == l.Id && n.Status == NoticeStatus.Served)
                    .OrderByDescending(n => n.ServedOn).Select(n => new { n.Type, n.ServedOn }).FirstOrDefault(),
            }).ToListAsync(ct);
        var truncated = leases.Count > MaxRows;
        leases = leases.Take(MaxRows).ToList();

        var rows = leases.Select(l => (IReadOnlyList<object?>)
        [
            Lease.FormatReference(l.Number), l.Property, l.Unit, l.Tenant, l.EndDate, l.EndDate.DayNumber - today.DayNumber,
            l.RentAmount, Label(l.BillingFrequency),
            l.Renewal is null ? "Not started" : $"{Lease.FormatReference(l.Renewal.Number)} ({Label(l.Renewal.Status)})",
            l.Notice is null ? null : $"{Label(l.Notice.Type)}, {l.Notice.ServedOn:dd MMM yyyy}",
        ]).ToList();
        var noRenewal = leases.Count(l => l.Renewal is null);
        return Table(d, range.From, range.To,
            [
                new("Leases ending", leases.Count.ToString(CultureInfo.InvariantCulture)),
                new("No renewal started", noRenewal.ToString(CultureInfo.InvariantCulture)),
                new("Ending within 30 days", leases.Count(l => l.EndDate.DayNumber - today.DayNumber <= 30).ToString(CultureInfo.InvariantCulture)),
            ],
            [
                Text("lease", "Lease"), Text("property", "Property"), Text("unit", "Unit"), Text("tenant", "Tenant"), Date("ends", "Ends"),
                Number("daysLeft", "Days left"), Money("rent", "Rent"), Text("frequency", "Paid"), Text("renewal", "Renewal"),
                Text("notice", "Latest notice"),
            ],
            rows, null, truncated);
    }

    private async Task<ReportTable> NoticesAsync(ReportDefinition d, (DateOnly From, DateOnly To) range, ReportQuery q, CancellationToken ct)
    {
        var properties = Properties(q);
        var notices = await db.LeaseNotices.Where(n => n.ServedOn >= range.From && n.ServedOn <= range.To)
            .Join(db.Leases.Where(l => properties.Any(p => p.Id == l.PropertyId)), n => n.LeaseId, l => l.Id, (n, l) => new { Notice = n, Lease = l })
            .OrderByDescending(x => x.Notice.ServedOn).Take(MaxRows + 1)
            .Select(x => new
            {
                x.Notice.ServedOn, x.Notice.Type, x.Notice.Method, x.Notice.ExpiresOn, x.Notice.Status, x.Lease.Number,
                Property = db.Properties.Where(p => p.Id == x.Lease.PropertyId).Select(p => p.Name).First(),
                Unit = db.Units.Where(u => u.Id == x.Lease.UnitId).Select(u => u.Code).First(),
                Tenant = x.Lease.Tenants.Where(t => t.IsPrimary)
                    .Select(t => db.Tenants.Where(y => y.Id == t.TenantId).Select(y => y.Name).First()).FirstOrDefault(),
            }).ToListAsync(ct);
        var truncated = notices.Count > MaxRows;
        notices = notices.Take(MaxRows).ToList();
        var rows = notices.Select(n => (IReadOnlyList<object?>)
        [
            n.ServedOn, Lease.FormatReference(n.Number), n.Property, n.Unit, n.Tenant, Label(n.Type), Label(n.Method), n.ExpiresOn, Label(n.Status),
        ]).ToList();
        var facts = notices.Where(n => n.Status == NoticeStatus.Served).GroupBy(n => n.Type).OrderByDescending(g => g.Count())
            .Select(g => new ReportFact(Label(g.Key), g.Count().ToString(CultureInfo.InvariantCulture))).ToList();
        facts.Insert(0, new ReportFact("Notices served", notices.Count(n => n.Status == NoticeStatus.Served).ToString(CultureInfo.InvariantCulture)));
        return Table(d, range.From, range.To, facts,
            [
                Date("servedOn", "Served on"), Text("lease", "Lease"), Text("property", "Property"), Text("unit", "Unit"),
                Text("tenant", "Tenant"), Text("type", "Notice"), Text("method", "How served"), Date("expiresOn", "Expires on"),
                Text("status", "Status"),
            ],
            rows, null, truncated);
    }

    private async Task<ReportTable> MaintenanceAsync(ReportDefinition d, (DateOnly From, DateOnly To) range, ReportQuery q, CancellationToken ct)
    {
        var today = Today;
        var properties = Properties(q);
        var fromUtc = range.From.ToDateTime(TimeOnly.MinValue).AddHours(-1);
        var toUtc = range.To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-1);
        var requests = await db.MaintenanceRequests
            .Where(r => properties.Any(p => p.Id == r.PropertyId))
            .Where(r => (r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc)
                        || (r.CompletedOn >= range.From && r.CompletedOn <= range.To)
                        || (r.Status != MaintenanceStatus.Completed && r.Status != MaintenanceStatus.Rejected && r.Status != MaintenanceStatus.Cancelled))
            .Select(r => new { r.Category, r.Urgency, r.Status, r.CreatedAtUtc, r.CompletedOn, r.TenantRating })
            .Take(MaxRows * 4).ToListAsync(ct);

        static DateOnly Day(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(1));
        bool Opened(DateTime created) => created >= fromUtc && created < toUtc;
        bool Completed(DateOnly? on) => on is { } c && c >= range.From && c <= range.To;
        static bool IsOpen(MaintenanceStatus s) => s is not (MaintenanceStatus.Completed or MaintenanceStatus.Rejected or MaintenanceStatus.Cancelled);
        static decimal? Average(IEnumerable<int> days) => days.Any() ? Math.Round((decimal)days.Average(), 1) : null;

        var rows = requests.GroupBy(r => r.Category).OrderBy(g => g.Key.ToString()).Select(g =>
        {
            var done = g.Where(r => Completed(r.CompletedOn) && r.Status == MaintenanceStatus.Completed).ToList();
            return (IReadOnlyList<object?>)
            [
                Label(g.Key), g.Count(r => Opened(r.CreatedAtUtc)), done.Count,
                Average(done.Select(r => Math.Max(0, r.CompletedOn!.Value.DayNumber - Day(r.CreatedAtUtc).DayNumber))),
                g.Count(r => IsOpen(r.Status)), g.Count(r => IsOpen(r.Status) && today.DayNumber - Day(r.CreatedAtUtc).DayNumber > 30),
            ];
        }).Where(r => r.Skip(1).Any(v => v is int n && n > 0)).ToList();

        var completed = requests.Where(r => Completed(r.CompletedOn) && r.Status == MaintenanceStatus.Completed).ToList();
        var ratings = requests.Where(r => Completed(r.CompletedOn) && r.TenantRating is not null).Select(r => r.TenantRating!.Value).ToList();
        var openNow = requests.Where(r => IsOpen(r.Status)).ToList();
        var averageDays = Average(completed.Select(r => Math.Max(0, r.CompletedOn!.Value.DayNumber - Day(r.CreatedAtUtc).DayNumber)));
        return Table(d, range.From, range.To,
            [
                new("Opened", requests.Count(r => Opened(r.CreatedAtUtc)).ToString(CultureInfo.InvariantCulture)),
                new("Completed", completed.Count.ToString(CultureInfo.InvariantCulture)),
                new("Average days to complete", averageDays?.ToString(CultureInfo.InvariantCulture) ?? "–"),
                new("Open now", openNow.Count.ToString(CultureInfo.InvariantCulture)),
                new("Open emergencies", openNow.Count(r => r.Urgency == MaintenanceUrgency.Emergency).ToString(CultureInfo.InvariantCulture)),
                new("Tenant rating", ratings.Count == 0 ? "–" : $"{Math.Round(ratings.Average(), 1).ToString(CultureInfo.InvariantCulture)} / 5"),
            ],
            [
                Text("category", "Category"), Number("opened", "Opened"), Number("completed", "Completed"),
                Number("averageDays", "Average days to complete"), Number("openNow", "Open now"), Number("openOver30", "Open over 30 days"),
            ],
            rows, null, false);
    }

    private async Task<ReportTable> LettingAsync(ReportDefinition d, (DateOnly From, DateOnly To) range, ReportQuery q, CancellationToken ct)
    {
        var today = Today;
        var properties = Properties(q);
        var fromUtc = range.From.ToDateTime(TimeOnly.MinValue).AddHours(-1);
        var toUtc = range.To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-1);
        var visible = listings.VisibleListings().Where(l => properties.Any(p => p.Id == l.PropertyId));
        var rows = await visible
            .Select(l => new
            {
                l.Headline, l.Status, l.CloseReason, l.PublishedAtUtc, l.ClosedAtUtc,
                Property = db.Properties.Where(p => p.Id == l.PropertyId).Select(p => p.Name).First(),
                Unit = db.Units.Where(u => u.Id == l.UnitId).Select(u => u.Code).First(),
                Leads = db.Leads.Where(x => x.ListingId == l.Id && x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc)
                    .Select(x => new { x.Status, x.Source }).ToList(),
                Viewings = db.Leads.Where(x => x.ListingId == l.Id).SelectMany(x => x.Viewings)
                    .Where(v => v.ScheduledAtUtc >= fromUtc && v.ScheduledAtUtc < toUtc).Select(v => v.Status).ToList(),
            })
            .Where(x => x.Leads.Any() || x.Viewings.Any() || x.Status != ListingStatus.Closed)
            .OrderBy(x => x.Property).ThenBy(x => x.Unit).Take(MaxRows + 1)
            .ToListAsync(ct);
        var truncated = rows.Count > MaxRows;
        rows = rows.Take(MaxRows).ToList();

        int? DaysListed(DateTime? published, DateTime? closed) =>
            published is { } p ? Math.Max(0, (closed is { } c ? DateOnly.FromDateTime(c.AddHours(1)) : today).DayNumber - DateOnly.FromDateTime(p.AddHours(1)).DayNumber) : null;
        var table = rows.Select(r => (IReadOnlyList<object?>)
        [
            r.Headline, r.Property, r.Unit, r.CloseReason is { } reason ? $"Closed ({Label(reason)})" : Label(r.Status),
            r.Leads.Count, r.Viewings.Count, r.Viewings.Count(v => v == ViewingStatus.Completed),
            r.Leads.Count(x => x.Status == LeadStatus.Won), r.Leads.Count(x => x.Status == LeadStatus.Lost), DaysListed(r.PublishedAtUtc, r.ClosedAtUtc),
        ]).ToList();
        var leads = rows.SelectMany(r => r.Leads).ToList();
        var viewings = rows.SelectMany(r => r.Viewings).ToList();
        var facts = new List<ReportFact>
        {
            new("Enquiries", leads.Count.ToString(CultureInfo.InvariantCulture)),
            new("Viewings", viewings.Count.ToString(CultureInfo.InvariantCulture)),
            new("Won", leads.Count(x => x.Status == LeadStatus.Won).ToString(CultureInfo.InvariantCulture)),
        };
        facts.AddRange(leads.GroupBy(x => x.Source).OrderByDescending(g => g.Count())
            .Select(g => new ReportFact($"From {Label(g.Key).ToLowerInvariant()}", g.Count().ToString(CultureInfo.InvariantCulture))));
        return Table(d, range.From, range.To, facts,
            [
                Text("listing", "Listing"), Text("property", "Property"), Text("unit", "Unit"), Text("status", "Status"),
                Number("enquiries", "Enquiries"), Number("viewings", "Viewings booked"), Number("viewed", "Viewed"), Number("won", "Won"),
                Number("lost", "Lost"), Number("daysListed", "Days listed"),
            ],
            table,
            ["Total", null, null, null, leads.Count, viewings.Count, viewings.Count(v => v == ViewingStatus.Completed),
                leads.Count(x => x.Status == LeadStatus.Won), leads.Count(x => x.Status == LeadStatus.Lost), null],
            truncated);
    }

    private async Task<ReportTable> ActivityAsync(ReportDefinition d, (DateOnly From, DateOnly To) range, CancellationToken ct)
    {
        // Audit logs are not query-filtered (some have no organization), so scope them explicitly.
        var organizationId = currentOrganization.RequireOrganizationId();
        var fromUtc = range.From.ToDateTime(TimeOnly.MinValue).AddHours(-1);
        var toUtc = range.To.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-1);
        var groups = await db.AuditLogs
            .Where(a => a.OrganizationId == organizationId && a.UserId != null && a.TimestampUtc >= fromUtc && a.TimestampUtc < toUtc)
            .GroupBy(a => a.UserId!.Value)
            .Select(g => new
            {
                UserId = g.Key,
                Denied = g.Count(a => a.Outcome != AuditOutcome.Success),
                Changes = g.Count(a => a.Outcome == AuditOutcome.Success),
                Last = g.Max(a => a.TimestampUtc),
                // Sign-ins aren't tied to one company, so activity here is counted in days with recorded actions.
                Days = g.Select(a => a.TimestampUtc.AddHours(1).Date).Distinct().Count(),
            }).ToListAsync(ct);
        var people = await users.GetAsync(groups.Select(g => g.UserId), ct);
        var rows = groups.OrderByDescending(g => g.Last).Take(MaxRows).Select(g => (IReadOnlyList<object?>)
        [
            people.GetValueOrDefault(g.UserId)?.FullName ?? "Former user", people.GetValueOrDefault(g.UserId)?.Email,
            g.Days, g.Changes, g.Denied, DateTime.SpecifyKind(g.Last, DateTimeKind.Utc),
        ]).ToList();
        return Table(d, range.From, range.To,
            [
                new("Active people", groups.Count.ToString(CultureInfo.InvariantCulture)),
                new("Refused actions", groups.Sum(g => g.Denied).ToString(CultureInfo.InvariantCulture)),
            ],
            [
                Text("name", "Name"), Text("email", "Email"), Number("activeDays", "Active days"), Number("changes", "Actions"),
                Number("denied", "Refused"), new("lastActive", "Last active", ColumnKind.DateTime),
            ],
            rows, null, groups.Count > MaxRows);
    }

    private ReportTable Table(ReportDefinition d, DateOnly? from, DateOnly? to, IReadOnlyList<ReportFact> facts, IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? totals, bool truncated) =>
        new(d.Key, d.Title, d.Description, from, to, facts, columns, rows, totals, truncated, clock.UtcNow);

    private static ReportColumn Text(string key, string label) => new(key, label, ColumnKind.Text);
    private static ReportColumn Number(string key, string label) => new(key, label, ColumnKind.Number);
    private static ReportColumn Money(string key, string label) => new(key, label, ColumnKind.Money);
    private static ReportColumn Date(string key, string label) => new(key, label, ColumnKind.Date);

    /// <summary>"WaterSupply" → "Water supply", so the screen and the CSV read the same.</summary>
    public static string Label<T>(T value) where T : struct, Enum
    {
        var name = value.ToString();
        var words = CamelCase().Replace(name, "$1 $2").ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex CamelCase();
}
