import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SectionHeader from '@/components/ui/SectionHeader.vue'

describe('SectionHeader', () => {
  it('renders the title as an h2', () => {
    const w = mount(SectionHeader, { props: { title: 'Presets' } })
    expect(w.find('h2').text()).toBe('Presets')
  })

  it('renders trailing actions', () => {
    const w = mount(SectionHeader, {
      props: { title: 'Presets' },
      slots: { actions: '<button>Manage</button>' },
    })
    expect(w.text()).toContain('Manage')
  })
})
