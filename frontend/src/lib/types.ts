export type OrganizationType = 'ManagementCompany' | 'IndividualLandlord'
export type OrganizationStatus = 'Active' | 'Suspended' | 'Cancelled'

export interface OrganizationSummary {
  id: string
  name: string
  slug: string
  type: OrganizationType
  status: OrganizationStatus
  roles: string[]
  permissions: string[]
}

export interface Me {
  userId: string
  email: string
  fullName: string
  isPlatformAdmin: boolean
  organizations: OrganizationSummary[]
}

export interface OrganizationDetail {
  id: string
  name: string
  slug: string
  type: OrganizationType
  status: OrganizationStatus
  baseCurrency: string
  timeZone: string
  contactEmail: string | null
  contactPhone: string | null
  address: string | null
  taxIdentificationNumber: string | null
  vatNumber: string | null
  createdAtUtc: string
}

export interface Branch {
  id: string
  name: string
  address: string | null
  isActive: boolean
  createdAtUtc: string
}

export interface Member {
  membershipId: string
  userId: string
  email: string
  fullName: string
  status: 'Active' | 'Revoked'
  roles: string[]
  branchIds: string[]
  joinedAtUtc: string
}

export interface Invitation {
  id: string
  email: string
  role: string
  branchIds: string[]
  status: 'Pending' | 'Accepted' | 'Cancelled'
  createdAtUtc: string
  expiresAtUtc: string
}

export interface CreatedInvitation {
  invitation: Invitation
  acceptToken: string
}

export interface AuditLogEntry {
  id: string
  timestampUtc: string
  userId: string | null
  userEmail: string | null
  action: string
  resourceType: string | null
  resourceId: string | null
  outcome: 'Success' | 'Denied' | 'Failed'
  details: string | null
}

export interface Paged<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
}
