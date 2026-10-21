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
  /** Set when one of the member's roles needs two-step sign-in: when that started. */
  twoFactorRequiredSinceUtc: string | null
  /** Set when this is platform support with an approved grant: read-only access ends at this time. */
  supportAccessUntilUtc?: string | null
  /** Names of the member's custom roles here, by key. */
  roleNames?: Record<string, string> | null
  /** Lifecycle dates for banners. Null for support access. */
  subscription?: SubscriptionSummary | null
}

export type SupportGrantStatus = 'Pending' | 'Active' | 'Declined' | 'Revoked' | 'Expired'

/** Read-only access for platform support, approved by the organization's admin (design 4.7). */
export interface SupportGrant {
  id: string
  organizationId: string
  organizationName: string
  requestedByName: string
  requestedByEmail: string
  reason: string
  hours: number
  status: SupportGrantStatus
  requestedAtUtc: string
  decidedByName: string | null
  decidedAtUtc: string | null
  expiresAtUtc: string | null
  revokedAtUtc: string | null
}

export interface Me {
  userId: string
  email: string
  fullName: string
  isPlatformAdmin: boolean
  organizations: OrganizationSummary[]
  twoFactorEnabled: boolean
  /** Days a member with such a role has to turn two-step sign-in on. */
  twoFactorGraceDays: number
}

/** The password was right; the account needs a code next. */
export interface TwoFactorChallenge {
  requiresTwoFactor: true
}

export interface TwoFactorStatus {
  enabled: boolean
  required: boolean
  recoveryCodesLeft: number
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
  repairApprovalLimit: number | null
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
export const LeaseStages = [
  'Draft',
  'Upcoming',
  'Active',
  'Expiring',
  'HoldingOver',
  'Renewed',
  'Ended',
  'Terminated',
  'Cancelled',
] as const
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

export interface NoticePeriodRule {
  id: string
  state: string | null
  type: NoticeType
  frequency: BillingFrequency | null
  days: number
  basis: string | null
}

export interface LeaseSettings {
  staffReminderDays: number[]
  tenantRemindersEnabled: boolean
  tenantReminderDays: number[]
  noticePeriods: NoticePeriodRule[]
  builtInNoticePeriods: { type: NoticeType; frequency: BillingFrequency | null; days: number }[]
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

// Maintenance (Phase 5)

export const MaintenanceCategories = [
  'Plumbing',
  'Electrical',
  'Carpentry',
  'Painting',
  'Roofing',
  'Appliances',
  'Generator',
  'WaterSupply',
  'Security',
  'Cleaning',
  'PestControl',
  'Structural',
  'Other',
] as const
export type MaintenanceCategory = (typeof MaintenanceCategories)[number]
export const MaintenanceUrgencies = ['Emergency', 'High', 'Normal', 'Low'] as const
export type MaintenanceUrgency = (typeof MaintenanceUrgencies)[number]
export const MaintenanceStatuses = [
  'Submitted',
  'UnderReview',
  'Approved',
  'Assigned',
  'InProgress',
  'AwaitingParts',
  'Completed',
  'Rejected',
  'Cancelled',
] as const
export type MaintenanceStatus = (typeof MaintenanceStatuses)[number]
export type MaintenanceUpdateKind = 'Created' | 'Comment' | 'StatusChanged' | 'Assigned' | 'Estimated' | 'OwnerApproved' | 'Feedback'

export interface Vendor {
  id: string
  name: string
  trade: string | null
  contactName: string | null
  phone: string | null
  email: string | null
  address: string | null
  notes: string | null
  isActive: boolean
  openJobs: number
  createdAtUtc: string
}

export interface MaintenanceSummary {
  id: string
  reference: string
  title: string
  category: MaintenanceCategory
  urgency: MaintenanceUrgency
  status: MaintenanceStatus
  propertyId: string
  propertyCode: string
  propertyName: string
  unitId: string | null
  unitCode: string | null
  assignedToName: string | null
  vendorName: string | null
  estimateAmount: number | null
  awaitingOwnerApproval: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface MaintenanceUpdate {
  id: string
  kind: MaintenanceUpdateKind
  text: string | null
  fromStatus: MaintenanceStatus | null
  status: MaintenanceStatus
  byName: string | null
  atUtc: string
}

export interface MaintenanceDetail {
  id: string
  reference: string
  title: string
  description: string | null
  category: MaintenanceCategory
  urgency: MaintenanceUrgency
  status: MaintenanceStatus
  propertyId: string
  propertyCode: string
  propertyName: string
  unitId: string | null
  unitCode: string | null
  tenantId: string | null
  tenantName: string | null
  reportedBy: string | null
  assignedUserId: string | null
  assignedToName: string | null
  vendorId: string | null
  vendorName: string | null
  vendorPhone: string | null
  estimateAmount: number | null
  estimateNotes: string | null
  approvalLimit: number | null
  needsOwnerApproval: boolean
  ownerApprovalNote: string | null
  ownerApprovalRecordedAtUtc: string | null
  approvedByName: string | null
  approvedAtUtc: string | null
  completedOn: string | null
  actualCost: number | null
  completionNotes: string | null
  closedReason: string | null
  tenantRating: number | null
  tenantFeedback: string | null
  createdByName: string | null
  createdAtUtc: string
  updatedAtUtc: string
  updates: MaintenanceUpdate[]
  can: { manage: boolean; work: boolean }
}

export interface Assignee {
  userId: string
  fullName: string
  email: string
}

// Portals (Phase 6)

export interface PortalLeaseBrief {
  reference: string
  tenantName: string | null
  rentAmount: number
  currency: string
  billingFrequency: BillingFrequency
  startDate: string
  endDate: string
  stage: string
}

export interface PortalProperty {
  id: string
  code: string
  name: string
  addressLine: string
  city: string | null
  state: string
  sharePercent: number
  units: { code: string; type: string; bedrooms: number | null; occupancyStatus: OccupancyStatus; lease: PortalLeaseBrief | null }[]
}

export interface OwnerPortal {
  ownerName: string
  organizationName: string
  properties: PortalProperty[]
  openMaintenance: number
  awaitingYourApproval: number
}

export interface PortalLease {
  id: string
  reference: string
  propertyName: string
  addressLine: string
  city: string | null
  state: string
  unitId: string
  unitCode: string
  status: string
  stage: string
  startDate: string
  endDate: string
  rentAmount: number
  currency: string
  billingFrequency: BillingFrequency
  dueDaysBeforePeriod: number
  graceDays: number
  noticePeriodDays: number | null
  renewalTerms: string | null
  moveOutDate: string | null
  tenants: string[]
  charges: { type: string; description: string | null; amount: number; recurrence: string }[]
  notices: { type: string; servedOn: string; expiresOn: string; notes: string | null }[]
}

export interface TenantPortal {
  tenantName: string
  organizationName: string
  leases: PortalLease[]
}

export interface PortalRequestSummary {
  id: string
  reference: string
  title: string
  category: MaintenanceCategory
  urgency: MaintenanceUrgency
  status: MaintenanceStatus
  propertyName: string
  unitCode: string | null
  estimateAmount: number | null
  awaitingOwnerApproval: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface PortalRequestDetail {
  id: string
  reference: string
  title: string
  description: string | null
  category: MaintenanceCategory
  urgency: MaintenanceUrgency
  status: MaintenanceStatus
  propertyName: string
  unitCode: string | null
  vendorName: string | null
  estimateAmount: number | null
  estimateNotes: string | null
  approvalLimit: number | null
  awaitingOwnerApproval: boolean
  ownerApprovalNote: string | null
  completedOn: string | null
  actualCost: number | null
  completionNotes: string | null
  closedReason: string | null
  createdAtUtc: string
  updates: { kind: MaintenanceUpdateKind; text: string | null; status: MaintenanceStatus; byYou: boolean; atUtc: string }[]
}

export type DocumentSubject = 'Property' | 'Owner' | 'Tenant' | 'Lease' | 'Maintenance'
export const DocumentCategories = [
  'Photo',
  'Agreement',
  'Identity',
  'Insurance',
  'Permit',
  'Certificate',
  'Inspection',
  'Quote',
  'Receipt',
  'Notice',
  'Correspondence',
  'Other',
] as const
export type DocumentCategory = (typeof DocumentCategories)[number]

export interface DocumentItem {
  id: string
  subject: DocumentSubject
  subjectId: string
  category: DocumentCategory
  title: string
  fileName: string
  contentType: string
  sizeBytes: number
  expiresOn: string | null
  sharedWithOwner: boolean
  sharedWithTenant: boolean
  source: 'Staff' | 'OwnerPortal' | 'TenantPortal'
  uploadedBy: string | null
  createdAtUtc: string
}

export interface DocumentList {
  items: DocumentItem[]
  canUpload: boolean
  canEdit: boolean
  canDelete: boolean
  canViewIdentity: boolean
}

export interface ExpiringDocument {
  id: string
  title: string
  category: DocumentCategory
  expiresOn: string
  subject: DocumentSubject
  subjectId: string
  subjectLabel: string
}

export interface PortalDocument {
  id: string
  title: string
  category: DocumentCategory
  fileName: string
  contentType: string
  sizeBytes: number
  subject: DocumentSubject
  subjectId: string
  subjectLabel: string
  expiresOn: string | null
  byYou: boolean
  createdAtUtc: string
}

// Phase 8: listings, enquiries and viewings.

export const ListingStatuses = ['Draft', 'Published', 'Closed'] as const
export type ListingStatus = (typeof ListingStatuses)[number]
export type ListingCloseReason = 'Let' | 'Withdrawn'

export interface ListingSummary {
  id: string
  publicCode: string
  headline: string
  status: ListingStatus
  closeReason: ListingCloseReason | null
  propertyId: string
  propertyName: string
  unitId: string
  unitCode: string
  askingRent: number
  currency: string
  billingFrequency: BillingFrequency
  photoCount: number
  openLeads: number
  agentName: string | null
  publishedAtUtc: string | null
  updatedAtUtc: string
}

export interface ListingDetail {
  id: string
  publicCode: string
  organizationSlug: string
  headline: string
  description: string | null
  askingRent: number
  currency: string
  billingFrequency: BillingFrequency
  availableFrom: string | null
  features: string | null
  showStreetAddress: boolean
  contactName: string | null
  contactPhone: string | null
  agentUserId: string | null
  agentName: string | null
  status: ListingStatus
  closeReason: ListingCloseReason | null
  publishedAtUtc: string | null
  closedAtUtc: string | null
  propertyId: string
  propertyName: string
  propertyAddress: string
  unitId: string
  unitCode: string
  unitType: UnitType
  bedrooms: number | null
  bathrooms: number | null
  unitOccupancy: OccupancyStatus
  photos: { documentId: string; title: string }[]
  openLeads: number
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ListingPhotoOption {
  documentId: string
  title: string
  createdAtUtc: string
}

export interface ListingAgent {
  userId: string
  fullName: string
  email: string
}

export const LeadStatuses = ['New', 'Contacted', 'ViewingBooked', 'Applied', 'Won', 'Lost'] as const
export type LeadStatus = (typeof LeadStatuses)[number]
export const LeadSources = ['Website', 'Phone', 'WalkIn', 'Referral', 'Other'] as const
export type LeadSource = (typeof LeadSources)[number]
export type ViewingStatus = 'Scheduled' | 'Completed' | 'NoShow' | 'Cancelled'

export interface LeadSummary {
  id: string
  reference: string
  name: string
  phone: string | null
  email: string | null
  source: LeadSource
  status: LeadStatus
  listingId: string
  listingHeadline: string
  propertyName: string
  unitCode: string
  nextViewingAtUtc: string | null
  createdAtUtc: string
  updatedAtUtc: string
}

export interface Viewing {
  id: string
  scheduledAtUtc: string
  agentUserId: string | null
  agentName: string | null
  notes: string | null
  status: ViewingStatus
  outcome: string | null
}

export interface LeadDetail {
  id: string
  reference: string
  name: string
  phone: string | null
  email: string | null
  message: string | null
  source: LeadSource
  status: LeadStatus
  notes: string | null
  listingId: string
  listingHeadline: string
  propertyName: string
  unitCode: string
  viewings: Viewing[]
  createdAtUtc: string
  updatedAtUtc: string
}

export interface PublicListingSummary {
  code: string
  headline: string
  askingRent: number
  currency: string
  billingFrequency: BillingFrequency
  unitType: UnitType
  bedrooms: number | null
  bathrooms: number | null
  area: string
  availableFrom: string | null
  photoUrl: string | null
}

export interface PublicOrganization {
  name: string
  slug: string
  contactPhone: string | null
  contactEmail: string | null
  listings: PublicListingSummary[]
}

export interface PublicListing {
  code: string
  headline: string
  description: string | null
  askingRent: number
  currency: string
  billingFrequency: BillingFrequency
  availableFrom: string | null
  unitType: UnitType
  bedrooms: number | null
  bathrooms: number | null
  sizeSquareMetres: number | null
  features: string[]
  streetAddress: string | null
  area: string
  photoUrls: string[]
  organizationName: string
  organizationSlug: string
  contactName: string | null
  contactPhone: string | null
}

// Phase 9: reports.

export type ColumnKind = 'Text' | 'Number' | 'Money' | 'Percent' | 'Date' | 'DateTime'
export type ReportCell = string | number | null

export interface ReportDefinition {
  key: string
  title: string
  description: string
  usesDates: boolean
  usesProperty: boolean
  usesBranch: boolean
}

export interface ReportTable {
  key: string
  title: string
  description: string
  from: string | null
  to: string | null
  facts: { label: string; value: string }[]
  columns: { key: string; label: string; kind: ColumnKind }[]
  rows: ReportCell[][]
  totals: ReportCell[] | null
  truncated: boolean
  generatedAtUtc: string
}

export interface Role {
  key: string
  /** Set for a custom role. */
  id: string | null
  name: string
  description: string | null
  isCustom: boolean
  permissions: string[]
  requiresTwoFactor: boolean
  memberCount: number
}

export interface PermissionGroup {
  group: string
  permissions: string[]
}

export type SubscriptionStatus = 'Trial' | 'Active' | 'PastDue' | 'Suspended' | 'Cancelled'

export interface SubscriptionSummary {
  status: SubscriptionStatus
  trialEndsAtUtc: string | null
  currentPeriodEndsAtUtc: string | null
  /** When the company becomes read-only; set while past due. */
  suspendsAtUtc: string | null
  /** Until when admins can download the data; set once cancelled. */
  exportUntilUtc: string | null
}

export interface Subscription extends SubscriptionSummary {
  planCode: string
  planName: string
  maxUnits: number
  maxUsers: number
  unitsUsed: number
  staffUsed: number
}

export interface Plan {
  code: string
  name: string
  maxUnits: number
  maxUsers: number
}

export interface PlatformOrganization {
  id: string
  name: string
  slug: string
  type: OrganizationType
  status: OrganizationStatus
  createdAtUtc: string
  planCode: string
  planName: string
  maxUnits: number
  maxUsers: number
  subscription: SubscriptionSummary
  units: number
  staff: number
}
