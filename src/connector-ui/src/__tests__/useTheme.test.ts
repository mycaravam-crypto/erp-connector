import { describe, it, expect, beforeEach, vi } from 'vitest'
import { nextTick } from 'vue'

function mockMatchMedia(prefersDark: boolean) {
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: query === '(prefers-color-scheme: dark)' ? prefersDark : false,
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })) as unknown as typeof window.matchMedia
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  vi.resetModules()
})

describe('useTheme', () => {
  it('follows system preference by default when dark is preferred', async () => {
    mockMatchMedia(true)
    const { useTheme } = await import('@/composables/useTheme')
    useTheme()
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('follows system preference by default when light is preferred', async () => {
    mockMatchMedia(false)
    const { useTheme } = await import('@/composables/useTheme')
    useTheme()
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('setPreference overrides system preference, applies the class, and persists it', async () => {
    mockMatchMedia(false)
    const { useTheme } = await import('@/composables/useTheme')
    const { setPreference } = useTheme()

    setPreference('dark')
    await nextTick()

    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(localStorage.getItem('x5-connector-theme')).toBe('dark')
  })

  it('restores a persisted manual preference on load, ignoring system preference', async () => {
    localStorage.setItem('x5-connector-theme', 'dark')
    mockMatchMedia(false)
    const { useTheme } = await import('@/composables/useTheme')
    useTheme()
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('setPreference("system") clears the stored override', async () => {
    mockMatchMedia(false)
    const { useTheme } = await import('@/composables/useTheme')
    const { setPreference } = useTheme()

    setPreference('dark')
    await nextTick()
    setPreference('system')
    await nextTick()

    expect(localStorage.getItem('x5-connector-theme')).toBeNull()
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('toggle switches between light and dark', async () => {
    mockMatchMedia(false)
    const { useTheme } = await import('@/composables/useTheme')
    const { toggle } = useTheme()

    toggle()
    await nextTick()
    expect(document.documentElement.classList.contains('dark')).toBe(true)

    toggle()
    await nextTick()
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })
})
