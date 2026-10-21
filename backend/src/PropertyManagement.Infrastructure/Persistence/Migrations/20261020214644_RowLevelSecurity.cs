using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropertyManagement.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// SQL Server Row-Level Security as the last isolation layer (design 4.4). While a connection's
    /// SESSION_CONTEXT('OrganizationId') is set, rows of every other organization are invisible and can't be written.
    /// With it cleared (sign-in, background work between organizations, deliberate cross-organization lookups) the
    /// policy lets everything through, and the application's own checks apply.
    /// </summary>
    public partial class RowLevelSecurity : Migration
    {
        /// <summary>Every table with an OrganizationId owned by one organization. A test fails if a new one is missing.</summary>
        public static readonly string[] Tables =
        [
            "Branches", "Documents", "Guarantors", "InspectionItems", "Leads", "LeaseCharges", "LeaseGuarantors",
            "LeaseInspections", "LeaseNotices", "LeaseReminders", "Leases", "LeaseTenants", "ListingPhotos", "Listings",
            "MaintenanceRequests", "MaintenanceUpdates", "ManagementAgreements", "MembershipBranchScopes",
            "OrganizationMembershipRoles", "OrganizationMemberships", "OrganizationSubscriptions", "OwnerBankAccounts",
            "Owners", "OwnerUserLinks", "Properties", "PropertyOwners", "PropertyUnits", "TenantKyc", "Tenants",
            "TenantScreeningChecks", "TenantUserLinks", "UserInvitations", "Vendors", "Viewings",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA rls;");
            migrationBuilder.Sql("""
                CREATE FUNCTION rls.fn_OrganizationAccess(@OrganizationId uniqueidentifier)
                RETURNS TABLE
                WITH SCHEMABINDING
                AS RETURN
                    SELECT 1 AS Allowed
                    WHERE SESSION_CONTEXT(N'OrganizationId') IS NULL
                       OR @OrganizationId = CAST(SESSION_CONTEXT(N'OrganizationId') AS uniqueidentifier);
                """);
            migrationBuilder.Sql(CreatePolicySql(Tables));
        }

        /// <summary>
        /// The policy over <paramref name="tables"/>. A later migration that adds an organization-owned table drops the
        /// policy and runs this again with the longer list.
        /// </summary>
        public static string CreatePolicySql(IEnumerable<string> tables)
        {
            var predicates = tables.SelectMany(t => new[]
            {
                $"ADD FILTER PREDICATE rls.fn_OrganizationAccess(OrganizationId) ON dbo.[{t}]",
                $"ADD BLOCK PREDICATE rls.fn_OrganizationAccess(OrganizationId) ON dbo.[{t}] AFTER INSERT",
                $"ADD BLOCK PREDICATE rls.fn_OrganizationAccess(OrganizationId) ON dbo.[{t}] AFTER UPDATE",
            });
            return $"CREATE SECURITY POLICY rls.OrganizationIsolation\n{string.Join(",\n", predicates)}\nWITH (STATE = ON);";
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SECURITY POLICY IF EXISTS rls.OrganizationIsolation;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rls.fn_OrganizationAccess;");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS rls;");
        }
    }
}
