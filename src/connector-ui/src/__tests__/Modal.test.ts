import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import Modal from '@/components/ui/Modal.vue'

describe('Modal', () => {
  it('does not render when closed', () => {
    const w = mount(Modal, { props: { open: false } })
    expect(w.find('[role="dialog"]').exists()).toBe(false)
  })

  it('renders content and title when open', () => {
    const w = mount(Modal, { props: { open: true, title: 'Confirm' }, slots: { default: 'Body text' } })
    expect(w.find('[role="dialog"]').exists()).toBe(true)
    expect(w.text()).toContain('Confirm')
    expect(w.text()).toContain('Body text')
  })

  it('renders footer slot actions', () => {
    const w = mount(Modal, { props: { open: true }, slots: { footer: '<button>OK</button>' } })
    expect(w.text()).toContain('OK')
  })

  it('closes on backdrop click by default', async () => {
    const w = mount(Modal, { props: { open: true } })
    await w.find('.absolute.inset-0').trigger('click')
    expect(w.emitted('update:open')?.[0]).toEqual([false])
    expect(w.find('[role="dialog"]').exists()).toBe(false)
  })

  it('does not close on backdrop click when closeOnBackdrop is false', async () => {
    const w = mount(Modal, { props: { open: true, closeOnBackdrop: false } })
    await w.find('.absolute.inset-0').trigger('click')
    expect(w.emitted('update:open')).toBeFalsy()
    expect(w.find('[role="dialog"]').exists()).toBe(true)
  })

  it('closes on Escape', async () => {
    const w = mount(Modal, { props: { open: true, title: 'Confirm' } })
    await w.find('[role="dialog"]').trigger('keydown', { key: 'Escape' })
    expect(w.emitted('update:open')?.[0]).toEqual([false])
  })

  it('closes via the close button when a title is set', async () => {
    const w = mount(Modal, { props: { open: true, title: 'Confirm' } })
    await w.find('button[aria-label="Close"]').trigger('click')
    expect(w.emitted('update:open')?.[0]).toEqual([false])
  })

  it('moves focus into the dialog when opened and returns it to the trigger on close', async () => {
    const host = document.createElement('div')
    document.body.appendChild(host)
    const trigger = document.createElement('button')
    trigger.textContent = 'Open'
    host.appendChild(trigger)
    trigger.focus()
    expect(document.activeElement).toBe(trigger)

    const w = mount(Modal, {
      attachTo: host,
      props: { open: false, title: 'Confirm' },
      slots: { footer: '<button id="confirm">Confirm</button>' },
    })

    await w.setProps({ open: true })
    await w.vm.$nextTick()
    await w.vm.$nextTick()
    expect(document.activeElement?.getAttribute('aria-label')).toBe('Close')

    await w.setProps({ open: false })
    await w.vm.$nextTick()
    expect(document.activeElement).toBe(trigger)

    w.unmount()
    host.remove()
  })
})
