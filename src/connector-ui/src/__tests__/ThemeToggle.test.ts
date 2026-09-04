import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.classList.remove('dark')
  vi.resetModules()
  window.matchMedia = vi.fn().mockImplementation((query: string) => ({
    matches: false,
    media: query,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  })) as unknown as typeof window.matchMedia
})

describe('ThemeToggle', () => {
  it('renders Light/Auto/Dark options with Auto pressed by default', async () => {
    const ThemeToggle = (await import('@/components/ThemeToggle.vue')).default
    const w = mount(ThemeToggle)
    const buttons = w.findAll('button')
    expect(buttons.map((b) => b.text())).toEqual(['Light', 'Auto', 'Dark'])
    expect(buttons[1]!.attributes('aria-pressed')).toBe('true')
  })

  it('clicking Dark applies the dark class and marks Dark as pressed', async () => {
    const ThemeToggle = (await import('@/components/ThemeToggle.vue')).default
    const w = mount(ThemeToggle)
    await w.findAll('button')[2]!.trigger('click')

    expect(document.documentElement.classList.contains('dark')).toBe(true)
    expect(w.findAll('button')[2]!.attributes('aria-pressed')).toBe('true')
    expect(w.findAll('button')[1]!.attributes('aria-pressed')).toBe('false')
  })
})
