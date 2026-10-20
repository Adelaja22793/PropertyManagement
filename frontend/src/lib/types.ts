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

// Phase 3: properties, units, owners, tenants.

export const PropertyTypes = ['Residential', 'Commercial', 'Mixed'] as const
export type PropertyType = (typeof PropertyTypes)[number]
export const PropertyStatuses = ['Active', 'UnderRenovation', 'Archived'] as const
export type PropertyStatus = (typeof PropertyStatuses)[number]

export interface OwnershipShare {
  ownerId: string
  ownerName: string
  sharePercent: number
  effectiveFrom: string
  effectiveTo: string | null
}

export interface Ownership {
  current: OwnershipShare[]
  history: OwnershipShare[]
}

export interface PropertySummary {
  id: string
  code: string
  name: string
  type: PropertyType
  addressLine: string
  city: string | null
  state: string
  branchId: string | null
  branchName: string | null
  managerUserId: string | null
  status: PropertyStatus
  unitCount: number
  occupiedUnitCount: number
}

export interface PropertyDetail extends PropertySummary {
  description: string | null
  localGovernmentArea: string | null
  managerName: string | null
  amenities: string | null
  createdAtUtc: string
  archivedAtUtc: string | null
  currentOwners: OwnershipShare[]
}

export const UnitTypes = ['Flat', 'Room', 'Shop', 'Office', 'Warehouse', 'Duplex', 'House', 'Other'] as const
export type UnitType = (typeof UnitTypes)[number]
export const BillingFrequencies = ['Annual', 'Biannual', 'Quarterly', 'Monthly'] as const
export type BillingFrequency = (typeof BillingFrequencies)[number]
export const OccupancyStatuses = ['Vacant', 'Occupied', 'Reserved', 'Unavailable'] as const
export type OccupancyStatus = (typeof OccupancyStatuses)[number]

export interface Unit {
  id: string
  propertyId: string
  propertyCode: string
  code: string
  type: UnitType
  floor: string | null
  bedrooms: number | null
  bathrooms: number | null
  sizeSquareMetres: number | null
  defaultRent: number
  currency: string
  billingFrequency: BillingFrequency
  serviceChargeSharePercent: number | null
  occupancyStatus: OccupancyStatus
  amenities: string | null
  notes: string | null
  isArchived: boolean
}

export const PartyTypes = ['Individual', 'Company'] as const
export type PartyType = (typeof PartyTypes)[number]
export const ContactChannels = ['Email', 'Sms', 'WhatsApp', 'Phone'] as const
export type ContactChannel = (typeof ContactChannels)[number]

export interface OwnerSummary {
  id: string
  type: PartyType
  name: string
  email: string | null
  phone: string | null
  propertyCount: number
  hasApprovedBankAccount: boolean
  hasPendingBankAccount: boolean
  hasPortalAccess: boolean
}

export interface OwnedProperty {
  propertyId: string
  code: string
  name: string
  sharePercent: number
  effectiveFrom: string
}

export interface OwnerDetail {
  id: string
  type: PartyType
  name: string
  email: string | null
  phone: string | null
  address: string | null
  taxIdentificationNumber: string | null
  preferredChannel: ContactChannel
  notes: string | null
  createdAtUtc: string
  properties: OwnedProperty[]
  hasPortalAccess: boolean
}

export type BankAccountStatus = 'PendingApproval' | 'Approved' | 'Rejected' | 'Replaced'

export interface BankAccount {
  id: string
  bankName: string
  accountNumber: string
  accountName: string
  status: BankAccountStatus
  requestedByUserId: string
  requestedByName: string | null
  requestedAtUtc: string
  decidedByUserId: string | null
  decidedByName: string | null
  decidedAtUtc: string | null
  decisionNote: string | null
}

export const FeeBases = ['PercentOfRentCollected', 'PercentOfRentInvoiced', 'FixedAmountPerMonth'] as const
export type FeeBasis = (typeof FeeBases)[number]
export const PayoutFrequencies = ['OnReceipt', 'Monthly', 'Quarterly'] as const
export type PayoutFrequency = (typeof PayoutFrequencies)[number]
export const AdvanceRentReleases = ['OnReceipt', 'ByPeriod'] as const
export type AdvanceRentRelease = (typeof AdvanceRentReleases)[number]

export interface Agreement {
  id: string
  ownerId: string
  propertyId: string | null
  propertyName: string | null
  feeBasis: FeeBasis
  feeValue: number
  lettingFeePercent: number | null
  payoutFrequency: PayoutFrequency
  advanceRentRelease: AdvanceRentRelease
  reserveAmount: number
  startDate: string
  endDate: string | null
  notes: string | null
}

export const IdentityDocumentTypes = ['Nin', 'InternationalPassport', 'DriversLicence', 'VotersCard'] as const
export type IdentityDocumentType = (typeof IdentityDocumentTypes)[number]
export const VerificationStatuses = ['Unverified', 'Verified', 'Failed'] as const
export type VerificationStatus = (typeof VerificationStatuses)[number]

export interface KycSummary {
  idType: IdentityDocumentType | null
  idNumberLast4: string | null
  bvnLast4: string | null
  status: VerificationStatus
  verifiedAtUtc: string | null
  verifiedByName: string | null
}

export interface KycDetail {
  idType: IdentityDocumentType | null
  idNumber: string | null
  bvn: string | null
  status: VerificationStatus
  verifiedAtUtc: string | null
  verifiedByName: string | null
}

export interface TenantSummary {
  id: string
  type: PartyType
  name: string
  email: string | null
  phone: string | null
  employer: string | null
  kycStatus: VerificationStatus
  guarantorCount: number
  hasPortalAccess: boolean
  createdAtUtc: string
}

export interface TenantDetail {
  id: string
  type: PartyType
  name: string
  email: string | null
  phone: string | null
  alternatePhone: string | null
  address: string | null
  employer: string | null
  occupation: string | null
  emergencyContactName: string | null
  emergencyContactPhone: string | null
  companyRegistrationNumber: string | null
  taxIdentificationNumber: string | null
  notes: string | null
  createdAtUtc: string
  kyc: KycSummary
  hasPortalAccess: boolean
}

export interface Guarantor {
  id: string
  tenantId: string
  fullName: string
  phone: string | null
  email: string | null
  address: string | null
  relationship: string | null
  employer: string | null
  occupation: string | null
  signedFormReceived: boolean
  createdAtUtc: string
}

export const ScreeningCheckTypes = ['Employer', 'PreviousLandlord', 'PersonalReference', 'Identity', 'Other'] as const
export type ScreeningCheckType = (typeof ScreeningCheckTypes)[number]
export const ScreeningOutcomes = ['Pending', 'Passed', 'Concern', 'Failed'] as const
export type ScreeningOutcome = (typeof ScreeningOutcomes)[number]

export interface ScreeningCheck {
  id: string
  type: ScreeningCheckType
  outcome: ScreeningOutcome
  notes: string | null
  checkedByUserId: string
  checkedByName: string | null
  checkedAtUtc: string
}

export const LeaseStatuses = ['Draft', 'Active', 'Renewed', 'Ended', 'Terminated', 'Cancelled'] as const
export type LeaseStatus = (typeof LeaseStatuses)[number]
export const LeaseStages = ['Draft', 'Upcoming', 'Active', 'Expiring', 'HoldingOver', 'Renewed', 'Ended', 'Terminated', 'Cancelled'] as const
export type LeaseStage = (typeof LeaseStages)[number]
export const LeaseChargeTypes = ['ServiceCharge', 'AgencyFee', 'LegalFee', 'CautionDeposit', 'StampDuty', 'Other'] as const
export type LeaseChargeType = (typeof LeaseChargeTypes)[number]
export const ChargeRecurrences = ['OneOff', 'EachPeriod'] as const
export type ChargeRecurrence = (typeof ChargeRecurrences)[number]

export interface LeaseSummary {
  id: string
  reference: string
  propertyId: string
  propertyCode: string
  propertyName: string
  unitId: string
  unitCode: string
  primaryTenantId: string | null
  primaryTenantName: string | null
  tenantCount: number
  startDate: string
  endDate: string
  rentAmount: number
  currency: string
  billingFrequency: BillingFrequency
  status: LeaseStatus
  stage: LeaseStage
  daysToExpiry: number | null
  renewalLeaseId: string | null
}

export interface LeaseCharge {
  id?: string
  type: LeaseChargeType
  description: string | null
  amount: number
  recurrence: ChargeRecurrence
}

export interface LinkedLease {
  id: string
  reference: string
  status: LeaseStatus
  startDate: string
  endDate: string
  rentAmount: number
}

export interface LeaseDetail {
  id: string
  reference: string
  propertyId: string
  propertyCode: string
  propertyName: string
  unitId: string
  unitCode: string
  status: LeaseStatus
  stage: LeaseStage
  daysToExpiry: number | null
  startDate: string
  endDate: string
  rentAmount: number
  currency: string
  billingFrequency: BillingFrequency
  dueDaysBeforePeriod: number
  graceDays: number
  noticePeriodDays: number | null
  holdoverRentAmount: number | null
  renewalTerms: string | null
  notes: string | null
  tenants: { tenantId: string; name: string; phone: string | null; email: string | null; isPrimary: boolean }[]
  guarantors: { guarantorId: string; tenantId: string; fullName: string; phone: string | null; signedFormReceived: boolean }[]
  charges: LeaseCharge[]
  previousLease: LinkedLease | null
  renewalLease: LinkedLease | null
  createdAtUtc: string
  activatedAtUtc: string | null
  activatedByName: string | null
  moveOutDate: string | null
  endReason: string | null
}

export const NoticeTypes = ['NoticeToQuit', 'OwnersIntentionToRecoverPossession', 'RentReview', 'Other'] as const
export type NoticeType = (typeof NoticeTypes)[number]
export const ServiceMethods = ['PersonalService', 'PastedOnPremises', 'RegisteredPost', 'Courier', 'Email'] as const
export type ServiceMethod = (typeof ServiceMethods)[number]

export interface LeaseNotice {
  id: string
  type: NoticeType
  servedOn: string
  method: ServiceMethod
  servedBy: string | null
  expiresOn: string
  notes: string | null
  status: 'Served' | 'Withdrawn'
  withdrawnReason: string | null
  recordedByName: string | null
  recordedAtUtc: string
  hasExpired: boolean
  daysLeft: number
}

export interface NoticeDefault {
  type: NoticeType
  servedOn: string
  days: number
  expiresOn: string
  basis: string
}

export const ItemConditions = ['Good', 'Fair', 'Poor', 'Damaged', 'Missing'] as const
export type ItemCondition = (typeof ItemConditions)[number]
export type InspectionType = 'MoveIn' | 'MoveOut'

export interface InspectionItem {
  area: string
  condition: ItemCondition
  notes: string | null
  proposedDeduction: number | null
}

export interface LeaseInspection {
  id: string
  type: InspectionType
  inspectedOn: string
  tenantPresent: boolean
  tenantAgreed: boolean
  summary: string | null
  inspectedByName: string | null
  updatedAtUtc: string
  items: InspectionItem[]
  totalProposedDeduction: number
}

export interface LeaseInspections {
  moveIn: LeaseInspection | null
  moveOut: LeaseInspection | null
  cautionDeposit: number | null
}
