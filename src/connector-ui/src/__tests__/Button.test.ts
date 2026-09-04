import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Button from '@/components/ui/Button.vue'

describe('Button', () => {
  it('renders slot content and defaults to the primary variant', () => {
    const w = mount(Button, { slots: { default: 'Save' } })
    expect(w.text()).toBe('Save')
    expect(w.classes()).toContain('bg-brand')
    expect(w.attributes('type')).toBe('button')
  })

  it.each(['primary', 'secondary', 'ghost', 'danger'] as const)('renders the %s variant', (variant) => {
    const w = mount(Button, { props: { variant }, slots: { default: 'Go' } })
    expect(w.html()).toBeTruthy()
  })

  it('is disabled and shows a spinner while loading', () => {
    const w = mount(Button, { props: { loading: true }, slots: { default: 'Save' } })
    expect(w.attributes('disabled')).toBeDefined()
    expect(w.find('.animate-spin').exists()).toBe(true)
  })

  it('is disabled when the disabled prop is set', () => {
    const w = mount(Button, { props: { disabled: true }, slots: { default: 'Save' } })
    expect(w.attributes('disabled')).toBeDefined()
  })

  it('emits click when enabled', async () => {
    const w = mount(Button, { slots: { default: 'Save' } })
    await w.trigger('click')
    expect(w.emitted('click')).toBeTruthy()
  })

  it('supports type="submit"', () => {
    const w = mount(Button, { props: { type: 'submit' }, slots: { default: 'Save' } })
    expect(w.attributes('type')).toBe('submit')
  })

  it('renders an icon slot', () => {
    const w = mount(Button, {
      slots: { default: 'Save', icon: '<svg data-test="icon" />' },
    })
    expect(w.find('[data-test="icon"]').exists()).toBe(true)
  })

  it('hides the icon slot while loading', () => {
    const w = mount(Button, {
      props: { loading: true },
      slots: { default: 'Save', icon: '<svg data-test="icon" />' },
    })
    expect(w.find('[data-test="icon"]').exists()).toBe(false)
  })
})
