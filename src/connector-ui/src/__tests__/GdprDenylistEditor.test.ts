import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import GdprDenylistEditor from '@/components/GdprDenylistEditor.vue'
import * as schedulerApi from '@/api/scheduler'
import { useToasts } from '@/composables/useToasts'

beforeEach(() => {
  vi.restoreAllMocks()
  useToasts().clear()
})

describe('GdprDenylistEditor', () => {
  it('renders the initial denied fields', () => {
    const w = mount(GdprDenylistEditor, { props: { initialFields: ['technician_name', 'notes'] } })
    expect(w.text()).toContain('technician_name')
    expect(w.text()).toContain('notes')
  })

  it('shows a placeholder when there are no fields', () => {
    const w = mount(GdprDenylistEditor, { props: { initialFields: [] } })
    expect(w.text()).toContain('No fields configured.')
  })

  it('adds a trimmed field and clears the input', async () => {
    const w = mount(GdprDenylistEditor, { props: { initialFields: [] } })
    await w.find('input').setValue('  new_field  ')
    const addButton = w.findAll('button').find((b) => b.text() === 'Add')!
    await addButton.trigger('click')
    expect(w.text()).toContain('new_field')
    expect((w.find('input').element as HTMLInputElement).value).toBe('')
  })

  it('does not add a duplicate field', async () => {
    const w = mount(GdprDenylistEditor, { props: { initialFields: ['dup'] } })
    await w.find('input').setValue('dup')
    const addButton = w.findAll('button').find((b) => b.text() === 'Add')!
    await addButton.trigger('click')
    expect(w.findAll('span').filter((s) => s.text().includes('dup'))).toHaveLength(1)
  })

  it('removes a field when its remove button is clicked', async () => {
    const w = mount(GdprDenylistEditor, { props: { initialFields: ['gone'] } })
    await w.find('button[aria-label="Remove gone"]').trigger('click')
    expect(w.text()).not.toContain('gone')
  })

  it('saves the current field list', async () => {
    const spy = vi.spyOn(schedulerApi, 'saveGdprDeniedFields').mockResolvedValueOnce({ ok: true })
    const w = mount(GdprDenylistEditor, { props: { initialFields: ['a'] } })
    const saveButton = w.findAll('button').find((b) => b.text().includes('Save GDPR Denylist'))!
    await saveButton.trigger('click')
    expect(spy).toHaveBeenCalledWith(['a'])
  })

  it('pushes a success toast on save', async () => {
    vi.spyOn(schedulerApi, 'saveGdprDeniedFields').mockResolvedValueOnce({ ok: true })
    const w = mount(GdprDenylistEditor, { props: { initialFields: ['a'] } })
    const saveButton = w.findAll('button').find((b) => b.text().includes('Save GDPR Denylist'))!
    await saveButton.trigger('click')
    await w.vm.$nextTick()
    expect(useToasts().toasts.value.some((t) => t.variant === 'success')).toBe(true)
  })

  it('shows an error message when the save fails', async () => {
    vi.spyOn(schedulerApi, 'saveGdprDeniedFields').mockResolvedValueOnce({ ok: false, error: 'nope' })
    const w = mount(GdprDenylistEditor, { props: { initialFields: [] } })
    const saveButton = w.findAll('button').find((b) => b.text().includes('Save GDPR Denylist'))!
    await saveButton.trigger('click')
    await w.vm.$nextTick()
    await new Promise((r) => setTimeout(r, 0))
    expect(w.text()).toContain('nope')
  })
})
