import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import * as exportsApi from '@/api/exports'

beforeEach(() => vi.restoreAllMocks())

function mountDialog(seqNo = 3) {
  return mount(ReleaseDialog, { props: { seqNo } })
}

describe('ReleaseDialog', () => {
  it('renders operator and approver inputs', () => {
    const w = mountDialog()
    expect(w.find('#operator').exists()).toBe(true)
    expect(w.find('#approver').exists()).toBe(true)
  })

  it('submit button is disabled when fields are empty', () => {
    const w = mountDialog()
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })

  it('shows same-user error when operator === approver', async () => {
    const w = mountDialog()
    await w.find('#operator').setValue('alice')
    await w.find('#approver').setValue('alice')
    expect(w.text()).toContain('must be different')
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })

  it('enables submit when operator ≠ approver', async () => {
    const w = mountDialog()
    await w.find('#operator').setValue('alice')
    await w.find('#approver').setValue('bob')
    expect(w.find('button').attributes('disabled')).toBeUndefined()
  })

  it('calls releaseExport and emits released on success', async () => {
    vi.spyOn(exportsApi, 'releaseExport').mockResolvedValueOnce({ ok: true, status: 200, message: '' })
    const w = mountDialog(3)
    await w.find('#operator').setValue('alice')
    await w.find('#approver').setValue('bob')
    await w.find('button').trigger('click')
    await w.vm.$nextTick()
    expect(exportsApi.releaseExport).toHaveBeenCalledWith(3, { operator: 'alice', approver: 'bob' })
    expect(w.emitted('released')).toBeTruthy()
  })

  it('shows server error message on failure', async () => {
    vi.spyOn(exportsApi, 'releaseExport').mockResolvedValueOnce({ ok: false, status: 409, message: 'Already released' })
    const w = mountDialog(3)
    await w.find('#operator').setValue('alice')
    await w.find('#approver').setValue('bob')
    await w.find('button').trigger('click')
    await w.vm.$nextTick()
    expect(w.text()).toContain('Already released')
  })
})
