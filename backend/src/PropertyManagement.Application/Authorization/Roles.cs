namespace PropertyManagement.Application.Authorization;

/// <summary>
/// Default roles from section 5 of the design. Phase 1 keeps role definitions in code;
/// organization-defined custom roles come later and will be stored per organization.
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
            Permissions.Maintenance.View, Permissions.Maintenance.Manage, Permissions.Listings.Manage,
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
            Permissions.Organization.View, Permissions.Properties.View, Permissions.Listings.Manage,
            Permissions.Documents.View, Permissions.Documents.Upload,
        ],
        [MaintenanceStaff] = [Permissions.Organization.View, Permissions.Maintenance.UpdateAssigned],
        // Portal roles see only their own records through owner/tenant links, never staff screens.
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
        OrganizationAdmin, Accountant, FinanceApprover,
    };

    /// <summary>Roles that see only the properties assigned to them, unless another role gives wider access.</summary>
    public static readonly IReadOnlySet<string> AssignedPropertyRoles = new HashSet<string> { PropertyManager };

    public static bool Exists(string role) => RolePermissions.ContainsKey(role);

    public static IReadOnlySet<string> PermissionsFor(IEnumerable<string> roles) =>
        roles.Where(RolePermissions.ContainsKey).SelectMany(r => RolePermissions[r]).ToHashSet();
}
