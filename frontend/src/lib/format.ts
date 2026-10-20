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
