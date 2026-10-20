import type { BillingFrequency, UnitType } from '../../lib/types'

const wholeNaira = new Intl.NumberFormat('en-NG', { style: 'currency', currency: 'NGN', maximumFractionDigits: 0 })

const per: Record<BillingFrequency, string> = {
  Annual: 'per year',
  Biannual: 'every 6 months',
  Quarterly: 'per quarter',
  Monthly: 'per month',
}

/** Asking rent the way Nigerian listings show it, e.g. "₦4,500,000 per year". */
export function rentLabel(amount: number, frequency: BillingFrequency): string {
  return `${wholeNaira.format(amount)} ${per[frequency]}`
}

/** A short description of the space, e.g. "2-bedroom flat · 2 bathrooms". */
export function unitSummary(type: UnitType, bedrooms: number | null, bathrooms: number | null): string {
  const kind = type === 'Other' ? 'Space' : type
  const parts = [bedrooms ? `${bedrooms}-bedroom ${kind.toLowerCase()}` : kind]
  if (bathrooms) parts.push(`${bathrooms} ${bathrooms === 1 ? 'bathroom' : 'bathrooms'}`)
  return parts.join(' · ')
}

/** The public page for one listing. */
export const publicListingPath = (code: string) => `/listing/${code}`
/** A company's public page of everything it has to let. */
export const publicCompanyPath = (slug: string) => `/to-let/${slug}`

// Lagos keeps West Africa Time (UTC+1) all year, so the offset is fixed.
const LAGOS_OFFSET = '+01:00'

/** A date-time typed in Lagos time ("2026-10-24T14:30") as a UTC ISO string for the API. */
export function lagosInputToUtc(value: string): string {
  return new Date(`${value}:00${LAGOS_OFFSET}`).toISOString()
}

/** A UTC timestamp as the value of a date-time input in Lagos time. */
export function utcToLagosInput(iso: string): string {
  const lagos = new Date(new Date(iso.endsWith('Z') ? iso : `${iso}Z`).getTime() + 60 * 60 * 1000)
  return lagos.toISOString().slice(0, 16)
}
