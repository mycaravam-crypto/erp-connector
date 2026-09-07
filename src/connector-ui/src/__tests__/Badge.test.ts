import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Badge from '@/components/ui/Badge.vue'

describe('Badge', () => {
  it('renders slot content and defaults to the neutral variant', () => {
    const w = mount(Badge, { slots: { default: '3 pending' } })
    expect(w.text()).toBe('3 pending')
    expect(w.classes()).toContain('bg-surface-elevated')
  })

  it.each(['neutral', 'success', 'warning', 'danger', 'info', 'brand'] as const)(
    'applies the %s variant background',
    (variant) => {
      const w = mount(Badge, { props: { variant } })
      expect(w.attributes('class')).toBeTruthy()
    },
  )

  it('applies the success variant classes', () => {
    const w = mount(Badge, { props: { variant: 'success' } })
    expect(w.classes()).toContain('bg-success-bg')
    expect(w.classes()).toContain('text-success')
  })
})
