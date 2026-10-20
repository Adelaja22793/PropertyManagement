/** Formats a UTC timestamp in Lagos time, Nigerian day-first style (design 12). */
export function formatDate(iso: string): string {
  return new Intl.DateTimeFormat('en-NG', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    timeZone: 'Africa/Lagos',
  }).format(new Date(iso.endsWith('Z') ? iso : `${iso}Z`))
}

const naira = new Intl.NumberFormat('en-NG', { style: 'currency', currency: 'NGN', minimumFractionDigits: 2 })

/** Formats an amount in naira, e.g. ₦1,500,000.00. */
export function formatNaira(amount: number | null | undefined): string {
  return amount === null || amount === undefined ? '–' : naira.format(amount)
}

/** Formats a calendar date ('YYYY-MM-DD') day-first, e.g. 01 Mar 2026. */
export function formatDay(day: string | null | undefined): string {
  if (!day) return '–'
  return new Intl.DateTimeFormat('en-NG', { day: '2-digit', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(
    new Date(`${day}T00:00:00Z`),
  )
}

/** Today's date in Lagos as 'YYYY-MM-DD'. */
export function todayInLagos(): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: 'Africa/Lagos' }).format(new Date())
}

const labels: Record<string, string> = {
  Nin: 'NIN',
  InternationalPassport: 'International passport',
  DriversLicence: "Driver's licence",
  VotersCard: "Voter's card",
  Sms: 'SMS',
  WhatsApp: 'WhatsApp',
  PendingApproval: 'Pending approval',
  PercentOfRentCollected: '% of rent collected',
  PercentOfRentInvoiced: '% of rent invoiced',
  FixedAmountPerMonth: 'Fixed amount per month',
  OnReceipt: 'On receipt',
  ByPeriod: 'By period',
  Biannual: 'Twice a year',
  HoldingOver: 'Holding over',
  OneOff: 'One-off (move-in)',
  EachPeriod: 'Every billing period',
  OwnersIntentionToRecoverPossession: "Owner's intention to recover possession (7-day notice)",
  PastedOnPremises: 'Pasted on the premises',
  MoveIn: 'Move-in',
  MoveOut: 'Move-out',
  PestControl: 'Pest control',
  WaterSupply: 'Water supply',
}

/** A readable label for an API enum value such as 'UnderRenovation' → 'Under renovation'. */
export function enumLabel(value: string | null | undefined): string {
  if (!value) return '–'
  if (labels[value]) return labels[value]
  const words = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}
