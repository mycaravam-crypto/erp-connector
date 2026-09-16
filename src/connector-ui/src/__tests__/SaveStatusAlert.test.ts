import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SaveStatusAlert from '@/components/ui/SaveStatusAlert.vue'

describe('SaveStatusAlert', () => {
  it('renders nothing when idle', () => {
    const w = mount(SaveStatusAlert, { props: { status: 'idle', message: '' } })
    expect(w.text()).toBe('')
  })

  it('renders a success alert with the message', () => {
    const w = mount(SaveStatusAlert, { props: { status: 'ok', message: 'Saved.' } })
    expect(w.text()).toContain('Saved.')
  })

  it('renders a danger alert with the message', () => {
    const w = mount(SaveStatusAlert, { props: { status: 'error', message: 'Nope.' } })
    expect(w.text()).toContain('Nope.')
  })
})
