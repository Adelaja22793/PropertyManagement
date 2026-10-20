namespace PropertyManagement.Domain.Platform;

/// <summary>A SaaS plan. Plans are platform-wide, not organization-scoped.</summary>
public class SubscriptionPlan
{
    private SubscriptionPlan() { }

    public SubscriptionPlan(Guid id, string code, string name, int maxUnits, int maxUsers,
        decimal monthlyPriceNgn, decimal annualPriceNgn, int trialDays)
    {
        Id = id;
        Code = code;
        Name = name;
        MaxUnits = maxUnits;
        MaxUsers = maxUsers;
        MonthlyPriceNgn = monthlyPriceNgn;
        AnnualPriceNgn = annualPriceNgn;
        TrialDays = trialDays;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int MaxUnits { get; private set; }
    public int MaxUsers { get; private set; }
    public decimal MonthlyPriceNgn { get; private set; }
    public decimal AnnualPriceNgn { get; private set; }
    public int TrialDays { get; private set; }
    public bool IsActive { get; private set; }
}
