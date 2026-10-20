import { z } from 'zod'

/** Matches the API's password policy: at least 10 characters with a lowercase letter and a digit. */
export const passwordRule = z
  .string()
  .min(10, 'Use at least 10 characters.')
  .regex(/[a-z]/, 'Include a lowercase letter.')
  .regex(/[0-9]/, 'Include a number.')
