import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import * as exportsApi from '@/api/exports'
import * as authApi from '@/api/auth'

beforeEach(() => vi.restoreAllMocks())

function mountDialog(seqNo = 3) {
  vi.spyOn(authApi, 'getUsername').mockReturnValue('alice')
  return mount(ReleaseDialog, { props: { seqNo } })
}

describe('ReleaseDialog', () => {
  it('renders approver input and shows current operator from JWT', () => {
    const w = mountDialog()
    expect(w.find('#approver').exists()).toBe(true)
    expect(w.text()).toContain('alice')
  })

  it('submit button is disabled when approver field is empty', () => {
    const w = mountDialog()
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })

  it('shows same-user error when approver matches the current operator', async () => {
    const w = mountDialog()
    await w.find('#approver').setValue('alice')
    expect(w.text()).toContain('must be different')
    expect(w.find('button').attributes('disabled')).toBeDefined()
  })

  it('enables submit when approver differs from operator', async () => {
    const w = mountDialog()
    await w.find('#approver').setValue('bob')
    expect(w.find('button').attributes('disabled')).toBeUndefined()
  })

  it('calls releaseExport with approver and emits released on success', async () => {
    vi.spyOn(exportsApi, 'releaseExport').mockResolvedValueOnce({ ok: true, status: 200, message: '' })
    const w = mountDialog(3)
    await w.find('#approver').setValue('bob')
    await w.find('button').trigger('click')
    await w.vm.$nextTick()
    expect(exportsApi.releaseExport).toHaveBeenCalledWith(3, { approver: 'bob' })
    expect(w.emitted('released')).toBeTruthy()
  })

  it('shows server error message on failure', async () => {
    vi.spyOn(exportsApi, 'releaseExport').mockResolvedValueOnce({ ok: false, status: 409, message: 'Already released' })
    const w = mountDialog(3)
    await w.find('#approver').setValue('bob')
    await w.find('button').trigger('click')
    await w.vm.$nextTick()
    expect(w.text()).toContain('Already released')
  })
})
