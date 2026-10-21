namespace PropertyManagement.Domain.Leases;

/// <summary>
/// How many leases hold a unit at once. A unit normally takes one lease at a time; a shared unit takes up to its
/// limit (design 6.5). Drafts and cancelled leases never count.
/// </summary>
public static class UnitCapacity
{
    /// <summary>
    /// The first day in [<paramref name="start"/>, <paramref name="end"/>] on which <paramref name="others"/> already
    /// hold the unit <paramref name="limit"/> times, with the leases holding it that day. Null when there is room on
    /// every day.
    /// </summary>
    public static (DateOnly Day, IReadOnlyList<Lease> Holding)? FirstFullDay(
        DateOnly start, DateOnly end, IEnumerable<Lease> others, int limit, DateOnly today)
    {
        var blocking = others.Where(o => o.BlocksUnitBetween(start, end, today)).ToList();
        if (blocking.Count < limit) return null;
        // The number of leases holding the unit only goes up on a day one of them starts, so those days are enough.
        var days = blocking.Select(o => o.StartDate).Where(d => d > start && d <= end).Append(start).Distinct().Order();
        foreach (var day in days)
        {
            var holding = blocking.Where(o => o.BlocksUnitBetween(day, day, today)).ToList();
            if (holding.Count >= limit) return (day, holding);
        }
        return null;
    }

    /// <summary>The most leases holding the unit on any one day from <paramref name="today"/> on.</summary>
    public static int MostAtOnceFrom(DateOnly today, IEnumerable<Lease> leases)
    {
        var current = leases.Where(l => l.BlocksUnitBetween(today, DateOnly.MaxValue, today)).ToList();
        return current.Select(l => l.StartDate > today ? l.StartDate : today).Distinct()
            .Select(day => current.Count(l => l.BlocksUnitBetween(day, day, today)))
            .DefaultIfEmpty(0).Max();
    }
}
