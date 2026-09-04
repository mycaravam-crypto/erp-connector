import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Input from '@/components/ui/Input.vue'

describe('Input', () => {
  it('links the label to the input via id/for', () => {
    const w = mount(Input, { props: { label: 'Username' } })
    const label = w.find('label')
    const input = w.find('input')
    expect(label.attributes('for')).toBe(input.attributes('id'))
  })

  it('emits update:modelValue when typed into', async () => {
    const w = mount(Input, { props: { modelValue: '' } })
    await w.find('input').setValue('hello')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['hello'])
  })

  it('shows a required marker', () => {
    const w = mount(Input, { props: { label: 'Username', required: true } })
    expect(w.find('label').text()).toContain('*')
  })

  it('shows help text when there is no error', () => {
    const w = mount(Input, { props: { helpText: 'Enter your username' } })
    expect(w.text()).toContain('Enter your username')
  })

  it('shows the error instead of help text, marks aria-invalid, and reddens the border', () => {
    const w = mount(Input, { props: { helpText: 'help', error: 'Required' } })
    expect(w.text()).toContain('Required')
    expect(w.text()).not.toContain('help')
    expect(w.find('input').attributes('aria-invalid')).toBe('true')
    expect(w.find('input').classes()).toContain('border-danger')
  })

  it('links aria-describedby to the error message', () => {
    const w = mount(Input, { props: { error: 'Required' } })
    const input = w.find('input')
    const errorEl = w.find('p')
    expect(input.attributes('aria-describedby')).toBe(errorEl.attributes('id'))
  })

  it('disables the input when disabled', () => {
    const w = mount(Input, { props: { disabled: true } })
    expect(w.find('input').attributes('disabled')).toBeDefined()
  })
})
