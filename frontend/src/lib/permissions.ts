// Mirrors PropertyManagement.Application.Authorization. The server is the authority;
// the UI only uses these to hide what the user could not do anyway.
export const Permissions = {
  organizationView: 'Organization.View',
  settingsManage: 'Settings.Manage',
  branchesView: 'Branches.View',
  branchesManage: 'Branches.Manage',
  usersView: 'Users.View',
  usersManage: 'Users.Manage',
  auditView: 'Audit.View',
  propertiesView: 'Properties.View',
  propertiesCreate: 'Properties.Create',
  propertiesUpdate: 'Properties.Update',
  propertiesArchive: 'Properties.Archive',
  ownersView: 'Owners.View',
  ownersManage: 'Owners.Manage',
  ownersApproveBankAccounts: 'Owners.ApproveBankAccounts',
  tenantsView: 'Tenants.View',
  tenantsCreate: 'Tenants.Create',
  tenantsUpdate: 'Tenants.Update',
  tenantsViewKyc: 'Tenants.ViewKyc',
} as const

/** Roles given only to landlords and tenants through a portal invitation. They have no staff permissions. */
export const PortalRoles = ['Owner', 'Tenant'] as const

export const StaffRoles = [
  { value: 'OrganizationAdmin', label: 'Organization admin' },
  { value: 'BranchManager', label: 'Branch manager' },
  { value: 'PropertyManager', label: 'Property manager' },
  { value: 'Accountant', label: 'Accountant' },
  { value: 'FinanceApprover', label: 'Finance approver' },
  { value: 'Agent', label: 'Agent' },
  { value: 'MaintenanceStaff', label: 'Maintenance staff' },
] as const

export function roleLabel(role: string): string {
  if (role === 'Owner') return 'Property owner'
  return StaffRoles.find((r) => r.value === role)?.label ?? role
}
