import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import TextField from '@/components/ui/TextField.vue'

describe('TextField', () => {
  it('links the label to the textarea via id/for', () => {
    const w = mount(TextField, { props: { label: 'Notes' } })
    expect(w.find('label').attributes('for')).toBe(w.find('textarea').attributes('id'))
  })

  it('defaults to 3 rows and accepts a custom row count', () => {
    expect(mount(TextField).find('textarea').attributes('rows')).toBe('3')
    expect(mount(TextField, { props: { rows: 6 } }).find('textarea').attributes('rows')).toBe('6')
  })

  it('emits update:modelValue when typed into', async () => {
    const w = mount(TextField, { props: { modelValue: '' } })
    await w.find('textarea').setValue('hello world')
    expect(w.emitted('update:modelValue')?.[0]).toEqual(['hello world'])
  })

  it('shows the error instead of help text', () => {
    const w = mount(TextField, { props: { helpText: 'help', error: 'Required' } })
    expect(w.text()).toContain('Required')
    expect(w.text()).not.toContain('help')
  })
})
