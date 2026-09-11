import { describe, it, expect } from 'vitest'
import { formatDate } from '@/lib/dates'

describe('formatDate', () => {
  it('returns an em dash for null/undefined', () => {
    expect(formatDate(null)).toBe('—')
    expect(formatDate(undefined)).toBe('—')
  })

  it('does not produce Invalid Date for a bare timestamp with no timezone', () => {
    expect(formatDate('2026-06-28T10:44:28')).not.toBe('Invalid Date')
  })

  it('does not produce Invalid Date for a timestamp already carrying a +00:00 offset', () => {
    // .NET's "O" format round-trips DateTimeOffset.UtcNow as "...+00:00" rather than "...Z" —
    // a naive `iso.includes('Z')` check misreads that as timezone-less and appends a second 'Z'.
    expect(formatDate('2026-06-28T10:44:28.1234567+00:00')).not.toBe('Invalid Date')
  })

  it('does not produce Invalid Date for a timestamp with a non-UTC offset', () => {
    expect(formatDate('2026-06-28T10:44:28-05:00')).not.toBe('Invalid Date')
  })

  it('does not double-append Z for a timestamp already ending in Z', () => {
    expect(formatDate('2026-06-28T10:44:28Z')).not.toBe('Invalid Date')
  })
})
