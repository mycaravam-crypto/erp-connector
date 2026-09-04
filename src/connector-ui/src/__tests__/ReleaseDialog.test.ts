import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ReleaseDialog from '@/components/ReleaseDialog.vue'
import * as exportsApi from '@/api/exports'
import * as authApi from '@/api/auth'

beforeEach(() => vi.restoreAllMocks())

async function mountAndOpen(seqNo = 3) {
  vi.spyOn(authApi, 'getUsername').mockReturnValue('alice')
  const w = mount(ReleaseDialog, { props: { seqNo } })
  await w.find('button').trigger('click')
  return w
}

function confirmButton(w: Awaited<ReturnType<typeof mountAndOpen>>) {
  return w.findAll('button').find((b) => b.text().includes('Confirm'))!
}

describe('ReleaseDialog', () => {
  it('renders a trigger button, with the form hidden until opened', () => {
    vi.spyOn(authApi, 'getUsername').mockReturnValue('alice')
    const w = mount(ReleaseDialog, { props: { seqNo: 3 } })
    expect(w.find('input').exists()).toBe(false)
    expect(w.text()).toContain('Release Run')
  })

  it('opens the dialog and shows the approver input and current operator', async () => {
    const w = await mountAndOpen()
    expect(w.find('input').exists()).toBe(true)
    expect(w.text()).toContain('alice')
  })

  it('confirm button is disabled when approver field is empty', async () => {
    const w = await mountAndOpen()
    expect(confirmButton(w).attributes('disabled')).toBeDefined()
  })

  it('shows same-user error when approver matches the current operator', async () => {
    const w = await mountAndOpen()
    await w.find('input').setValue('alice')
    expect(w.text()).toContain('must be different')
    expect(confirmButton(w).attributes('disabled')).toBeDefined()
  })

  it('enables confirm when approver differs from operator', async () => {
    const w = await mountAndOpen()
    await w.find('input').setValue('bob')
    expect(confirmButton(w).attributes('disabled')).toBeUndefined()
  })

  it('calls releaseExport with approver and emits released on success', async () => {
    vi.spyOn(exportsApi, 'releaseExport').mockResolvedValueOnce({ ok: true, status: 200, message: '' })
    const w = await mountAndOpen(3)
    await w.find('input').setValue('bob')
    await confirmButton(w).trigger('click')
    await w.vm.$nextTick()
    expect(exportsApi.releaseExport).toHaveBeenCalledWith(3, { approver: 'bob' })
    expect(w.emitted('released')).toBeTruthy()
  })

  it('shows server error message on failure', async () => {
    vi.spyOn(exportsApi, 'releaseExport').mockResolvedValueOnce({ ok: false, status: 409, message: 'Already released' })
    const w = await mountAndOpen(3)
    await w.find('input').setValue('bob')
    await confirmButton(w).trigger('click')
    await w.vm.$nextTick()
    expect(w.text()).toContain('Already released')
  })
})
