import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { Inbox } from 'lucide-vue-next'
import EmptyState from '@/components/ui/EmptyState.vue'

describe('EmptyState', () => {
  it('renders the title', () => {
    const w = mount(EmptyState, { props: { title: 'No jobs yet' } })
    expect(w.text()).toContain('No jobs yet')
  })

  it('renders an optional description', () => {
    const w = mount(EmptyState, { props: { title: 'No jobs yet', description: 'Create one to get started.' } })
    expect(w.text()).toContain('Create one to get started.')
  })

  it('omits the description when not given', () => {
    const w = mount(EmptyState, { props: { title: 'No jobs yet' } })
    expect(w.findAll('p').length).toBe(1)
  })

  it('renders an optional icon', () => {
    const w = mount(EmptyState, { props: { title: 'No jobs yet', icon: Inbox } })
    expect(w.find('svg').exists()).toBe(true)
  })

  it('renders default slot content (e.g. a call-to-action button)', () => {
    const w = mount(EmptyState, { props: { title: 'No jobs yet' }, slots: { default: '<button>+ New</button>' } })
    expect(w.text()).toContain('+ New')
  })
})
