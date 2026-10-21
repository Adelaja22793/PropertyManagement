namespace PropertyManagement.Application.Authorization;

/// <summary>
/// Built-in roles from section 5 of the design, defined in code. Organizations can add their own roles from the
/// permission list (<see cref="Domain.Organizations.CustomRole"/>); members hold those by their "custom:" key.
/// </summary>
public static class Roles
{
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string BranchManager = "BranchManager";
    public const string PropertyManager = "PropertyManager";
    public const string Accountant = "Accountant";
    public const string FinanceApprover = "FinanceApprover";
    public const string Agent = "Agent";
    public const string MaintenanceStaff = "MaintenanceStaff";
    public const string Owner = "Owner";
    public const string Tenant = "Tenant";
    /// <summary>A platform support person with an approved, time-limited grant (design 4.7). Never given to a member.</summary>
    public const string PlatformSupport = "PlatformSupport";

    private static readonly Dictionary<string, HashSet<string>> RolePermissions = new()
    {
        [OrganizationAdmin] = [.. Permissions.All],
        [BranchManager] =
        [
            Permissions.Organization.View, Permissions.Branches.View, Permissions.Users.View,
            Permissions.Properties.View, Permissions.Properties.Create, Permissions.Properties.Update, Permissions.Properties.Archive,
            Permissions.Owners.View, Permissions.Owners.Manage,
            Permissions.Tenants.View, Permissions.Tenants.Create, Permissions.Tenants.Update,
            Permissions.Leases.View, Permissions.Leases.Create, Permissions.Leases.Renew, Permissions.Leases.Terminate, Permissions.Leases.IssueNotice,
            Permissions.Maintenance.View, Permissions.Maintenance.Manage, Permissions.Listings.Manage, Permissions.Listings.Leads,
            Permissions.Documents.View, Permissions.Documents.Upload, Permissions.Documents.Delete,
            Permissions.Finance.InvoicesView, Permissions.Finance.PaymentsView, Permissions.Finance.ReportsView,
        ],
        [PropertyManager] =
        [
            Permissions.Organization.View, Permissions.Branches.View,
            Permissions.Properties.View, Permissions.Properties.Update, Permissions.Owners.View,
            Permissions.Tenants.View, Permissions.Tenants.Create, Permissions.Tenants.Update,
            Permissions.Leases.View, Permissions.Leases.Create, Permissions.Leases.Renew, Permissions.Leases.IssueNotice,
            Permissions.Maintenance.View, Permissions.Maintenance.Manage,
            Permissions.Documents.View, Permissions.Documents.Upload,
            Permissions.Listings.Manage, Permissions.Listings.Leads,
            Permissions.Finance.InvoicesView, Permissions.Finance.PaymentsView,
        ],
        [Accountant] =
        [
            Permissions.Organization.View, Permissions.Branches.View,
            Permissions.Properties.View, Permissions.Tenants.View, Permissions.Leases.View,
            Permissions.Owners.View, Permissions.Owners.Manage,
            Permissions.Finance.InvoicesView, Permissions.Finance.InvoicesManage,
            Permissions.Finance.PaymentsView, Permissions.Finance.PaymentsRecord,
            Permissions.Finance.PayoutsCreate, Permissions.Finance.ReportsView, Permissions.Documents.View,
        ],
        [FinanceApprover] =
        [
            Permissions.Organization.View, Permissions.Branches.View,
            Permissions.Owners.View, Permissions.Owners.ApproveBankAccounts,
            Permissions.Finance.InvoicesView, Permissions.Finance.PaymentsView,
            Permissions.Finance.PayoutsApprove, Permissions.Finance.ReportsView, Permissions.Audit.View,
        ],
        [Agent] =
        [
            Permissions.Organization.View, Permissions.Properties.View, Permissions.Listings.Manage, Permissions.Listings.Leads,
            Permissions.Documents.View, Permissions.Documents.Upload,
        ],
        [MaintenanceStaff] = [Permissions.Organization.View, Permissions.Maintenance.UpdateAssigned],
        // Portal roles see only their own records through owner/tenant links, never staff screens.
        // Read-only, and without identity numbers: support sees what the screens show, nothing more.
        [PlatformSupport] =
        [
            Permissions.Organization.View, Permissions.Branches.View, Permissions.Users.View, Permissions.Audit.View,
            Permissions.Properties.View, Permissions.Owners.View, Permissions.Tenants.View, Permissions.Leases.View,
            Permissions.Maintenance.View, Permissions.Documents.View,
            Permissions.Finance.InvoicesView, Permissions.Finance.PaymentsView, Permissions.Finance.ReportsView,
        ],
        [Owner] = [],
        [Tenant] = [],
    };

    /// <summary>Roles staff can be invited into. Portal roles come only from an owner or tenant portal invitation.</summary>
    public static readonly IReadOnlySet<string> StaffRoles = new HashSet<string>
    {
        OrganizationAdmin, BranchManager, PropertyManager, Accountant, FinanceApprover, Agent, MaintenanceStaff,
    };

    /// <summary>Roles whose holders always see the whole organization, whatever their branch scope.</summary>
    public static readonly IReadOnlySet<string> OrganizationWideRoles = new HashSet<string>
    {
        OrganizationAdmin, Accountant, FinanceApprover, PlatformSupport,
    };

    /// <summary>Roles that see only the properties assigned to them, unless another role gives wider access.</summary>
    public static readonly IReadOnlySet<string> AssignedPropertyRoles = new HashSet<string> { PropertyManager };

    /// <summary>Display names of the built-in staff roles, in the order they are offered.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, string Description)> StaffRoleNames =
    [
        (OrganizationAdmin, "Organization admin", "Company settings, staff, roles, all properties and finance."),
        (BranchManager, "Branch manager", "Staff and properties within their branches."),
        (PropertyManager, "Property manager", "Units, tenants, leases, notices and repairs for the properties assigned to them."),
        (Accountant, "Accountant", "Invoices, payments, reconciliation, expenses, payouts and reports."),
        (FinanceApprover, "Finance approver", "Approves payouts, refunds, credit notes, write-offs and deposit deductions."),
        (Agent, "Agent", "Listings, enquiries and viewings for the listings they're in charge of."),
        (MaintenanceStaff, "Maintenance staff", "Updates the repair jobs given to them."),
    ];

    /// <summary>
    /// Permissions only roles with two-step sign-in hold among the built-in roles. A custom role with any of them needs
    /// two-step sign-in too, so a custom role can't be used to get admin or finance powers without it (design 11.1).
    /// </summary>
    public static readonly IReadOnlySet<string> TwoFactorPermissions = Permissions.All
        .Except(RolePermissions.Where(r => StaffRoles.Contains(r.Key) && !Domain.Organizations.OrganizationMembership.TwoFactorRoles.Contains(r.Key))
            .SelectMany(r => r.Value))
        .ToHashSet();

    public static bool Exists(string role) => RolePermissions.ContainsKey(role);

    /// <summary>A staff role: a built-in one, or one the organization defined.</summary>
    public static bool IsStaff(string role) => StaffRoles.Contains(role) || Domain.Organizations.CustomRole.IsCustomKey(role);

    public static IReadOnlySet<string> BuiltInPermissions(string role) =>
        RolePermissions.TryGetValue(role, out var granted) ? granted : new HashSet<string>();

    public static bool NeedsTwoFactor(IEnumerable<string> permissions) => permissions.Any(TwoFactorPermissions.Contains);

    /// <param name="custom">Permissions of the organization's custom roles, by key. Unknown keys grant nothing.</param>
    public static IReadOnlySet<string> PermissionsFor(IEnumerable<string> roles, IReadOnlyDictionary<string, IReadOnlySet<string>>? custom = null) =>
        roles.SelectMany<string, string>(r => RolePermissions.TryGetValue(r, out var granted) ? granted
                : custom is not null && custom.TryGetValue(r, out var defined) ? defined : Array.Empty<string>())
            .ToHashSet();
}
