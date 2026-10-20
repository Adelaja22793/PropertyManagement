using System.Reflection;

namespace PropertyManagement.Application.Authorization;

/// <summary>Permission codes from section 5.1 of the design. Grouped by module.</summary>
public static class Permissions
{
    public static class Organization
    {
        public const string View = "Organization.View";
        public const string ManageSettings = "Settings.Manage";
    }

    public static class Branches
    {
        public const string View = "Branches.View";
        public const string Manage = "Branches.Manage";
    }

    public static class Users
    {
        public const string View = "Users.View";
        public const string Manage = "Users.Manage";
    }

    public static class Audit
    {
        public const string View = "Audit.View";
    }

    public static class Properties
    {
        public const string View = "Properties.View";
        public const string Create = "Properties.Create";
        public const string Update = "Properties.Update";
        public const string Archive = "Properties.Archive";
    }

    public static class Owners
    {
        public const string View = "Owners.View";
        public const string Manage = "Owners.Manage";
        /// <summary>Approve or reject owner payout bank accounts. Never by the person who added the account.</summary>
        public const string ApproveBankAccounts = "Owners.ApproveBankAccounts";
    }

    public static class Tenants
    {
        public const string View = "Tenants.View";
        public const string Create = "Tenants.Create";
        public const string Update = "Tenants.Update";
        public const string ViewKyc = "Tenants.ViewKyc";
    }

    public static class Leases
    {
        public const string View = "Leases.View";
        public const string Create = "Leases.Create";
        public const string Renew = "Leases.Renew";
        public const string Terminate = "Leases.Terminate";
        public const string IssueNotice = "Leases.IssueNotice";
    }

    public static class Finance
    {
        public const string InvoicesView = "Invoices.View";
        public const string InvoicesManage = "Invoices.Manage";
        public const string PaymentsView = "Payments.View";
        public const string PaymentsRecord = "Payments.Record";
        public const string PayoutsCreate = "Payouts.Create";
        public const string PayoutsApprove = "Payouts.Approve";
        public const string ReportsView = "Reports.View";
    }

    public static class Maintenance
    {
        public const string View = "Maintenance.View";
        public const string Manage = "Maintenance.Manage";
        public const string UpdateAssigned = "Maintenance.UpdateAssigned";
    }

    public static class Documents
    {
        public const string View = "Documents.View";
        public const string Upload = "Documents.Upload";
        public const string Delete = "Documents.Delete";
    }

    public static class Listings
    {
        public const string Manage = "Listings.Manage";
        /// <summary>Enquiries and viewings.</summary>
        public const string Leads = "Leads.Manage";
    }

    /// <summary>Every permission code defined above.</summary>
    public static readonly IReadOnlySet<string> All = typeof(Permissions)
        .GetNestedTypes()
        .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToHashSet();
}
