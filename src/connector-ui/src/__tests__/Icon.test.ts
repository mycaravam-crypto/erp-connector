import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { Check } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

describe('Icon', () => {
  it('renders the given icon component, hidden from assistive tech by default', () => {
    const w = mount(Icon, { props: { icon: Check } })
    const svg = w.find('svg')
    expect(svg.exists()).toBe(true)
    expect(svg.attributes('aria-hidden')).toBe('true')
  })

  it('defaults to size 20 and accepts 16/24', () => {
    expect(mount(Icon, { props: { icon: Check } }).find('svg').attributes('width')).toBe('20')
    expect(mount(Icon, { props: { icon: Check, size: 16 } }).find('svg').attributes('width')).toBe('16')
    expect(mount(Icon, { props: { icon: Check, size: 24 } }).find('svg').attributes('width')).toBe('24')
  })
})
