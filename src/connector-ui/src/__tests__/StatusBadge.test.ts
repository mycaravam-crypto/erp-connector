import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from '@/components/StatusBadge.vue'

describe('StatusBadge', () => {
  it.each([
    ['Pending',   'bg-warning-bg',      'text-warning'],
    ['Released',  'bg-success-bg',      'text-success'],
    ['Failed',    'bg-danger-bg',       'text-danger'],
    ['Skipped',   'bg-surface-elevated', 'text-text-muted'],
    ['Delivered', 'bg-info-bg',         'text-info'],
    ['Unknown',   'bg-surface-elevated', 'text-text-secondary'],
  ])('renders %s with correct token classes', (status, bg, text) => {
    const w = mount(StatusBadge, { props: { status } })
    const span = w.find('span')
    expect(span.classes()).toContain(bg)
    expect(span.classes()).toContain(text)
  })

  it('renders the status text', () => {
    const w = mount(StatusBadge, { props: { status: 'Pending' } })
    expect(w.text()).toBe('Pending')
  })

  it('is case-insensitive for status matching', () => {
    const w = mount(StatusBadge, { props: { status: 'RELEASED' } })
    expect(w.find('span').classes()).toContain('bg-success-bg')
  })
})
