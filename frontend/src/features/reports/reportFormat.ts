import type { ColumnKind, ReportCell } from '../../lib/types'
import { formatDate, formatDay } from '../../lib/format'

const whole = new Intl.NumberFormat('en-NG', { maximumFractionDigits: 1 })
const naira = new Intl.NumberFormat('en-NG', { style: 'currency', currency: 'NGN', maximumFractionDigits: 0 })

/** A report cell as text for the screen. Empty cells show a dash. */
export function formatCell(value: ReportCell, kind: ColumnKind): string {
  if (value === null || value === '') return '–'
  switch (kind) {
    case 'Number':
      return typeof value === 'number' ? whole.format(value) : value
    case 'Money':
      return typeof value === 'number' ? naira.format(value) : value
    case 'Percent':
      return typeof value === 'number' ? `${whole.format(value)}%` : value
    case 'Date':
      return formatDay(String(value))
    case 'DateTime':
      return formatDate(String(value))
    default:
      return String(value)
  }
}

/** Numbers line up on the right. */
export const alignRight = (kind: ColumnKind) => kind === 'Number' || kind === 'Money' || kind === 'Percent'
