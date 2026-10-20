namespace PropertyManagement.Application.Abstractions;

public static class ClockExtensions
{
    /// <summary>Nigeria is UTC+1 all year (no daylight saving), so a fixed offset gives the local date.</summary>
    private static readonly TimeSpan LagosOffset = TimeSpan.FromHours(1);

    /// <summary>Today's date in Nigeria. Use it for business dates such as which ownership shares are current.</summary>
    public static DateOnly TodayInNigeria(this IClock clock) => DateOnly.FromDateTime(clock.UtcNow + LagosOffset);
}
