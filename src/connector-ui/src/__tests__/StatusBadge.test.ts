import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from '@/components/StatusBadge.vue'

describe('StatusBadge', () => {
  it.each([
    ['Pending',   'bg-yellow-100', 'text-yellow-800'],
    ['Released',  'bg-green-100',  'text-green-800'],
    ['Failed',    'bg-red-100',    'text-red-800'],
    ['Skipped',   'bg-slate-100',  'text-slate-500'],
    ['Delivered', 'bg-blue-100',   'text-blue-800'],
    ['Unknown',   'bg-slate-100',  'text-slate-600'],
  ])('renders %s with correct color classes', (status, bg, text) => {
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
    expect(w.find('span').classes()).toContain('bg-green-100')
  })
})
