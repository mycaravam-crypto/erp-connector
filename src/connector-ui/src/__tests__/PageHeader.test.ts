import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PageHeader from '@/components/ui/PageHeader.vue'

describe('PageHeader', () => {
  it('renders the title as an h1', () => {
    const w = mount(PageHeader, { props: { title: 'Export Jobs' } })
    expect(w.find('h1').text()).toBe('Export Jobs')
  })

  it('renders an optional eyebrow above the title', () => {
    const w = mount(PageHeader, { props: { title: 'Export Jobs', eyebrow: 'Exports · Independent Jobs' } })
    expect(w.text()).toContain('Exports · Independent Jobs')
  })

  it('renders trailing actions', () => {
    const w = mount(PageHeader, {
      props: { title: 'Export Jobs' },
      slots: { actions: '<button>+ New</button>' },
    })
    expect(w.text()).toContain('+ New')
  })
})
