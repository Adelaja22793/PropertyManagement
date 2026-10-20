// Badge colours for statuses shown across the app.
type Tone = 'slate' | 'green' | 'amber' | 'red'

const tones: Record<string, Tone> = {
  Active: 'green',
  UnderRenovation: 'amber',
  Archived: 'slate',
  Vacant: 'amber',
  Occupied: 'green',
  Reserved: 'slate',
  Unavailable: 'slate',
  PendingApproval: 'amber',
  Approved: 'green',
  Rejected: 'red',
  Replaced: 'slate',
  Unverified: 'amber',
  Verified: 'green',
  Failed: 'red',
  Pending: 'amber',
  Passed: 'green',
  Concern: 'amber',
}

export function statusTone(status: string): Tone {
  return tones[status] ?? 'slate'
}
