import { todayInLagos } from '../../lib/format'

/** File sizes as people read them: 820 KB, 2.4 MB. */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** Whether a document has lapsed or lapses within the warning window. */
export function expiryState(expiresOn: string | null, warnDays = 60): 'expired' | 'soon' | 'ok' | null {
  if (!expiresOn) return null
  const today = todayInLagos()
  if (expiresOn < today) return 'expired'
  const warn = new Date(`${today}T00:00:00Z`)
  warn.setUTCDate(warn.getUTCDate() + warnDays)
  return expiresOn <= warn.toISOString().slice(0, 10) ? 'soon' : 'ok'
}

export const acceptedFiles = '.pdf,.jpg,.jpeg,.png,.webp,.docx,.xlsx,application/pdf,image/jpeg,image/png,image/webp'
export const acceptedPhotos = '.jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp'
export const maxUploadBytes = 10 * 1024 * 1024
