using PropertyManagement.Domain.Common;

namespace PropertyManagement.Domain.Leases;

/// <summary>Charge types from the finance design. Each decides who the money belongs to when Phase 4 invoices it.</summary>
public enum LeaseChargeType { ServiceCharge = 0, AgencyFee = 1, LegalFee = 2, CautionDeposit = 3, StampDuty = 4, Other = 5 }

public enum ChargeRecurrence
{
    /// <summary>Charged once, on the first invoice (move-in charges such as agency fee or deposit).</summary>
    OneOff = 0,
    /// <summary>Charged with the rent every billing period (such as service charge).</summary>
    EachPeriod = 1,
}

/// <summary>A tenant named on a lease. Exactly one is the primary tenant, who receives invoices and notices.</summary>
public class LeaseTenant : IOrganizationScoped
{
    private LeaseTenant() { }

    internal LeaseTenant(Guid organizationId, Guid leaseId, Guid tenantId, bool isPrimary)
    {
        OrganizationId = organizationId;
        LeaseId = leaseId;
        TenantId = tenantId;
        IsPrimary = isPrimary;
    }

    public Guid OrganizationId { get; private set; }
    public Guid LeaseId { get; private set; }
    public Guid TenantId { get; private set; }
    public bool IsPrimary { get; private set; }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;
}

/// <summary>A guarantor standing behind a lease. The guarantor must belong to one of the lease's tenants.</summary>
public class LeaseGuarantor : IOrganizationScoped
{
    private LeaseGuarantor() { }

    internal LeaseGuarantor(Guid organizationId, Guid leaseId, Guid guarantorId)
    {
        OrganizationId = organizationId;
        LeaseId = leaseId;
        GuarantorId = guarantorId;
    }

    public Guid OrganizationId { get; private set; }
    public Guid LeaseId { get; private set; }
    public Guid GuarantorId { get; private set; }
}

/// <summary>A charge on a lease besides the rent. Recorded as data now; Phase 4 invoices it.</summary>
public class LeaseCharge : IOrganizationScoped
{
    private LeaseCharge() { }

    internal LeaseCharge(Guid organizationId, Guid leaseId, LeaseChargeInput input)
    {
        Guard.Defined(input.Type, "Charge type");
        Guard.Defined(input.Recurrence, "Charge recurrence");
        if (input.Amount <= 0) throw new DomainException("Each charge must be more than zero.");
        if (decimal.Round(input.Amount, 2) != input.Amount) throw new DomainException("Charges can have at most two decimal places.");
        if (input.Type is LeaseChargeType.CautionDeposit or LeaseChargeType.AgencyFee or LeaseChargeType.LegalFee
                or LeaseChargeType.StampDuty && input.Recurrence != ChargeRecurrence.OneOff)
            throw new DomainException("Deposits, agency, legal and stamp duty charges are one-off.");
        Id = Guid.NewGuid();
        OrganizationId = organizationId;
        LeaseId = leaseId;
        Type = input.Type;
        Description = input.Type == LeaseChargeType.Other
            ? Guard.Required(input.Description, "Description of the other charge", 200)
            : Guard.Optional(input.Description, "Description", 200);
        Amount = input.Amount;
        Recurrence = input.Recurrence;
    }

    public Guid Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid LeaseId { get; private set; }
    public LeaseChargeType Type { get; private set; }
    public string? Description { get; private set; }
    public decimal Amount { get; private set; }
    public ChargeRecurrence Recurrence { get; private set; }
}
