import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ConfirmAction from '@/components/ui/ConfirmAction.vue'

describe('ConfirmAction', () => {
  it('renders the trigger slot when not confirming', () => {
    const w = mount(ConfirmAction, {
      props: { confirming: false },
      slots: { trigger: '<button>Delete</button>' },
    })
    expect(w.text()).toBe('Delete')
  })

  it('opens via the scoped trigger slot prop', async () => {
    const w = mount(ConfirmAction, {
      props: { confirming: false },
      slots: { trigger: '<template #trigger="{ open }"><button @click="open">Delete</button></template>' },
    })
    await w.find('button').trigger('click')
    expect(w.emitted('update:confirming')?.[0]).toEqual([true])
  })

  it('renders confirm/cancel buttons when confirming (button variant)', () => {
    const w = mount(ConfirmAction, { props: { confirming: true, variant: 'button' } })
    expect(w.text()).toContain('Delete permanently?')
    const buttons = w.findAll('button')
    expect(buttons.some((b) => b.text() === 'Confirm')).toBe(true)
    expect(buttons.some((b) => b.text() === 'Cancel')).toBe(true)
  })

  it('renders confirm/cancel as bare links (link variant)', () => {
    const w = mount(ConfirmAction, { props: { confirming: true, variant: 'link' } })
    expect(w.text()).not.toContain('Delete permanently?')
    const buttons = w.findAll('button')
    expect(buttons.map((b) => b.text())).toEqual(['Confirm', 'Cancel'])
  })

  it('emits confirm when the confirm button is clicked', async () => {
    const w = mount(ConfirmAction, { props: { confirming: true, variant: 'link' } })
    await w.findAll('button')[0]!.trigger('click')
    expect(w.emitted('confirm')).toBeTruthy()
  })

  it('emits update:confirming false when cancelled', async () => {
    const w = mount(ConfirmAction, { props: { confirming: true, variant: 'link' } })
    await w.findAll('button')[1]!.trigger('click')
    expect(w.emitted('update:confirming')?.[0]).toEqual([false])
  })

  it('uses custom labels and disables the confirm control while busy', () => {
    const w = mount(ConfirmAction, {
      props: { confirming: true, variant: 'link', busy: true, confirmLabel: 'Deleting…', cancelLabel: 'Nevermind' },
    })
    const buttons = w.findAll('button')
    expect(buttons[0]!.text()).toBe('Deleting…')
    expect(buttons[0]!.attributes('disabled')).toBeDefined()
    expect(buttons[1]!.text()).toBe('Nevermind')
  })
})
