import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Select from '@/components/ui/Select.vue'

function mountSelect(props = {}) {
  return mount(Select, {
    props,
    slots: {
      default: '<option value="a">A</option><option value="b">B</option>',
    },
  })
}

describe('Select', () => {
  it('links the label to the select via id/for', () => {
    const w = mountSelect({ label: 'Format' })
    expect(w.find('label').attributes('for')).toBe(w.find('select').attributes('id'))
  })

  it('renders slotted options', () => {
    const w = mountSelect()
    expect(w.findAll('option')).toHaveLength(2)
  })

  it('emits update:modelValue on change', async () => {
    const w = mountSelect({ modelValue: 'a' })
    await w.find('select').setValue('b')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['b'])
  })

  it('shows the error and marks aria-invalid', () => {
    const w = mountSelect({ error: 'Pick one' })
    expect(w.text()).toContain('Pick one')
    expect(w.find('select').attributes('aria-invalid')).toBe('true')
  })

  it('disables the select when disabled', () => {
    const w = mountSelect({ disabled: true })
    expect(w.find('select').attributes('disabled')).toBeDefined()
  })
})
