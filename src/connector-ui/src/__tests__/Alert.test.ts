import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Alert from '@/components/ui/Alert.vue'

describe('Alert', () => {
  it('renders message content and defaults to the info variant', () => {
    const w = mount(Alert, { slots: { default: 'Saved.' } })
    expect(w.text()).toContain('Saved.')
    expect(w.classes()).toContain('bg-info-bg')
  })

  it.each(['success', 'warning', 'danger', 'info'] as const)('applies the %s variant background', (variant) => {
    const w = mount(Alert, { props: { variant } })
    expect(w.classes()).toContain(`bg-${variant}-bg`)
  })

  it('renders an optional title', () => {
    const w = mount(Alert, { props: { title: 'Heads up' }, slots: { default: 'Details' } })
    expect(w.text()).toContain('Heads up')
    expect(w.text()).toContain('Details')
  })

  it('renders an icon slot', () => {
    const w = mount(Alert, { slots: { icon: '<svg data-test="icon" />', default: 'Saved.' } })
    expect(w.find('[data-test="icon"]').exists()).toBe(true)
  })

  it('has role="alert"', () => {
    expect(mount(Alert).attributes('role')).toBe('alert')
  })
})
