// Mirrors PropertyManagement.Application.Authorization. The server is the authority;
// the UI only uses these to hide what the user could not do anyway.
export const Permissions = {
  organizationView: 'Organization.View',
  settingsManage: 'Settings.Manage',
  branchesView: 'Branches.View',
  branchesManage: 'Branches.Manage',
  usersView: 'Users.View',
  usersManage: 'Users.Manage',
  dataExport: 'Data.Export',
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
  leasesView: 'Leases.View',
  leasesCreate: 'Leases.Create',
  leasesRenew: 'Leases.Renew',
  leasesTerminate: 'Leases.Terminate',
  leasesIssueNotice: 'Leases.IssueNotice',
  maintenanceView: 'Maintenance.View',
  maintenanceManage: 'Maintenance.Manage',
  maintenanceUpdateAssigned: 'Maintenance.UpdateAssigned',
  documentsView: 'Documents.View',
  documentsUpload: 'Documents.Upload',
  documentsDelete: 'Documents.Delete',
  listingsManage: 'Listings.Manage',
  leadsManage: 'Leads.Manage',
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

/** A role's name. Custom roles ("custom:…") are named by the organization, so pass their names when you have them. */
export function roleLabel(role: string, customNames?: Record<string, string> | null): string {
  if (role === 'Owner') return 'Property owner'
  if (role === 'PlatformSupport') return 'Platform support'
  if (role.startsWith('custom:')) return customNames?.[role] ?? 'Custom role'
  return StaffRoles.find((r) => r.value === role)?.label ?? role
}

export const permissionGroupLabels: Record<string, string> = {
  Organization: 'Company',
  Branches: 'Branches',
  Users: 'Team',
  Audit: 'Audit log',
  Data: 'Data',
  Properties: 'Properties and units',
  Owners: 'Owners',
  Tenants: 'Tenants',
  Leases: 'Leases',
  Finance: 'Finance (screens not built yet)',
  Maintenance: 'Maintenance',
  Documents: 'Documents',
  Listings: 'Listings',
}

export const permissionLabels: Record<string, string> = {
  'Organization.View': 'See the company and its dashboard',
  'Settings.Manage': 'Change company settings',
  'Branches.View': 'See branches',
  'Branches.Manage': 'Add and change branches',
  'Users.View': 'See the team and roles',
  'Users.Manage': 'Invite, change and remove team members and roles',
  'Audit.View': 'Read the audit log',
  'Data.Export': "Download the company's data or one person's data",
  'Properties.View': 'See properties and units',
  'Properties.Create': 'Add properties',
  'Properties.Update': 'Change properties and units',
  'Properties.Archive': 'Archive properties',
  'Owners.View': 'See owners',
  'Owners.Manage': 'Add and change owners, shares and agreements',
  'Owners.ApproveBankAccounts': "Approve owners' payout bank accounts",
  'Tenants.View': 'See tenants',
  'Tenants.Create': 'Add tenants',
  'Tenants.Update': 'Change tenants and guarantors',
  'Tenants.ViewKyc': 'See identity numbers (NIN, BVN)',
  'Leases.View': 'See leases',
  'Leases.Create': 'Draft and start leases',
  'Leases.Renew': 'Renew leases',
  'Leases.Terminate': 'End leases',
  'Leases.IssueNotice': 'Record notices',
  'Invoices.View': 'See invoices',
  'Invoices.Manage': 'Issue and change invoices',
  'Payments.View': 'See payments',
  'Payments.Record': 'Record payments',
  'Payouts.Create': 'Prepare owner payouts',
  'Payouts.Approve': 'Approve owner payouts',
  'Reports.View': 'See financial reports',
  'Maintenance.View': 'See repair requests',
  'Maintenance.Manage': 'Handle repair requests and vendors',
  'Maintenance.UpdateAssigned': 'Update repair jobs given to them',
  'Documents.View': 'See documents',
  'Documents.Upload': 'Upload documents',
  'Documents.Delete': 'Delete documents',
  'Listings.Manage': 'Advertise vacant units',
  'Leads.Manage': 'Handle enquiries and viewings',
}

/** Mirrors Roles.TwoFactorPermissions: a role with any of these needs two-step sign-in. */
export const twoFactorPermissions = [
  'Audit.View',
  'Branches.Manage',
  'Data.Export',
  'Invoices.Manage',
  'Owners.ApproveBankAccounts',
  'Payments.Record',
  'Payouts.Approve',
  'Payouts.Create',
  'Settings.Manage',
  'Tenants.ViewKyc',
  'Users.Manage',
]
