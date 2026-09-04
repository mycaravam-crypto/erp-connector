import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Card from '@/components/ui/Card.vue'

describe('Card', () => {
  it('renders default slot content', () => {
    const w = mount(Card, { slots: { default: 'Body' } })
    expect(w.text()).toContain('Body')
  })

  it('renders header and footer slots when provided', () => {
    const w = mount(Card, { slots: { header: 'Header', default: 'Body', footer: 'Footer' } })
    expect(w.text()).toContain('Header')
    expect(w.text()).toContain('Footer')
  })

  it('applies elevation only when elevated is set', () => {
    expect(mount(Card).classes()).not.toContain('shadow-md')
    expect(mount(Card, { props: { elevated: true } }).classes()).toContain('shadow-md')
  })
})
