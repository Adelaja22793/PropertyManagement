import { z } from 'zod'

/** Matches the API's password policy: at least 10 characters with a lowercase letter and a digit. */
export const passwordRule = z
  .string()
  .min(10, 'Use at least 10 characters.')
  .regex(/[a-z]/, 'Include a lowercase letter.')
  .regex(/[0-9]/, 'Include a number.')

/** An optional text field: blank is fine. */
export const optionalText = (max: number) => z.string().trim().max(max, `Use at most ${max} characters.`)

/** An optional email: blank or a valid address. */
export const optionalEmail = z.union([z.literal(''), z.email('Enter a valid email address.')])

/** An optional non-negative number typed as text (blank allowed). */
export const optionalAmount = z
  .string()
  .trim()
  .regex(/^(\d+(\.\d{1,2})?)?$/, 'Enter a number with up to two decimal places.')

/** A required non-negative number typed as text. */
export const requiredAmount = z
  .string()
  .trim()
  .regex(/^\d+(\.\d{1,2})?$/, 'Enter a number with up to two decimal places.')

/** An optional whole number typed as text. */
export const optionalWhole = z.string().trim().regex(/^\d*$/, 'Enter a whole number.')

/** Trims a text value and turns blank into null for the API. */
export function blankToNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? ''
  return trimmed === '' ? null : trimmed
}

/** Turns a number typed as text into a number, or null when blank. */
export function numberOrNull(value: string | null | undefined): number | null {
  const trimmed = value?.trim() ?? ''
  return trimmed === '' ? null : Number(trimmed)
}
