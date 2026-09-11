/** Formats a backend timestamp for display, treating an offset-less ISO string as UTC (the
 * backend sometimes serializes DateTimeKind.Unspecified values without a 'Z' or offset suffix).
 * Detects an existing 'Z' or +/-HH:mm offset rather than a bare `.includes('Z')` check, since that
 * naive check mistakes a `+00:00`-style offset for "no timezone" and appends a second one,
 * producing "Invalid Date". */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—'
  const hasTimezone = /(Z|[+-]\d{2}:\d{2})$/.test(iso)
  return new Date(hasTimezone ? iso : iso + 'Z').toLocaleString()
}
