import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ChangePasswordDialog from '@/components/ChangePasswordDialog.vue'
import * as authApi from '@/api/auth'

beforeEach(() => vi.restoreAllMocks())

async function openDialog() {
  const w = mount(ChangePasswordDialog, { props: { open: false }, attachTo: document.body })
  await w.setProps({ open: true })
  return w
}

function inputs() {
  return Array.from(document.body.querySelectorAll<HTMLInputElement>('input[type="password"]'))
}

async function fill(values: string[]) {
  inputs().forEach((input, i) => {
    input.value = values[i]!
    input.dispatchEvent(new Event('input'))
  })
  await flushPromises()
}

function submitButton() {
  return Array.from(document.body.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Change password')!
}

describe('ChangePasswordDialog', () => {
  it('keeps the submit button disabled until the new password is long enough and repeated', async () => {
    const w = await openDialog()
    await fill(['old-password', 'short', 'short'])
    expect(submitButton().disabled).toBe(true)
    await fill(['old-password', 'new-password-1', 'new-password-2'])
    expect(submitButton().disabled).toBe(true)
    await fill(['old-password', 'new-password-1', 'new-password-1'])
    expect(submitButton().disabled).toBe(false)
    w.unmount()
  })

  it('changes the password and emits changed', async () => {
    const spy = vi.spyOn(authApi, 'changePassword').mockResolvedValueOnce({ ok: true })
    const w = await openDialog()
    await fill(['old-password', 'new-password-1', 'new-password-1'])
    submitButton().click()
    await flushPromises()
    expect(spy).toHaveBeenCalledWith('old-password', 'new-password-1')
    expect(w.emitted('changed')).toHaveLength(1)
    w.unmount()
  })

  it('shows the server error and stays open', async () => {
    vi.spyOn(authApi, 'changePassword').mockResolvedValueOnce({ ok: false, error: 'The current password is incorrect.' })
    const w = await openDialog()
    await fill(['wrong', 'new-password-1', 'new-password-1'])
    submitButton().click()
    await flushPromises()
    expect(document.body.textContent).toContain('The current password is incorrect.')
    expect(w.emitted('changed')).toBeUndefined()
    w.unmount()
  })
})
