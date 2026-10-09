import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import UsersSettings from '@/components/UsersSettings.vue'
import * as usersApi from '@/api/users'
import * as authApi from '@/api/auth'
import { useToasts } from '@/composables/useToasts'

const USERS = [
  { username: 'alice', role: 'Admin' as const, createdAt: '2026-10-01T00:00:00Z' },
  { username: 'carol', role: 'User' as const, createdAt: '2026-10-01T00:00:00Z' },
]

beforeEach(() => {
  vi.restoreAllMocks()
  useToasts().clear()
  vi.spyOn(authApi, 'getUsername').mockReturnValue('alice')
  vi.spyOn(usersApi, 'listUsers').mockImplementation(async () => USERS.map((u) => ({ ...u })))
})

function row(w: ReturnType<typeof mount>, username: string) {
  return w.findAll('tbody tr').find((r) => r.text().startsWith(username))!
}

describe('UsersSettings', () => {
  it('lists users and offers no role change or delete on your own row', async () => {
    const w = mount(UsersSettings)
    await flushPromises()

    expect(row(w, 'alice').text()).toContain('(you)')
    expect(row(w, 'alice').text()).not.toContain('Make User')
    expect(row(w, 'alice').text()).not.toContain('Delete')
    expect(row(w, 'carol').text()).toContain('Make Admin')
    expect(row(w, 'carol').text()).toContain('Delete')
  })

  it('adds a user and reloads the list', async () => {
    const create = vi
      .spyOn(usersApi, 'createUser')
      .mockResolvedValueOnce({ ok: true, data: { username: 'dave', role: 'User', createdAt: '' } })
    const w = mount(UsersSettings)
    await flushPromises()

    const inputs = w.findAll('form input')
    await inputs[0]!.setValue('dave')
    await inputs[1]!.setValue('dave-password-1')
    await w.find('form').trigger('submit')
    await flushPromises()

    expect(create).toHaveBeenCalledWith('dave', 'dave-password-1', 'User')
    expect(usersApi.listUsers).toHaveBeenCalledTimes(2)
  })

  it('switches a role', async () => {
    const update = vi
      .spyOn(usersApi, 'updateUser')
      .mockResolvedValueOnce({ ok: true, data: { username: 'carol', role: 'Admin', createdAt: '' } })
    const w = mount(UsersSettings)
    await flushPromises()

    await row(w, 'carol').findAll('button').find((b) => b.text() === 'Make Admin')!.trigger('click')
    await flushPromises()

    expect(update).toHaveBeenCalledWith('carol', { role: 'Admin' })
    expect(row(w, 'carol').text()).toContain('Make User')
  })

  it('shows the server refusal as an error toast', async () => {
    vi.spyOn(usersApi, 'updateUser').mockResolvedValueOnce({ ok: false, error: 'The last Admin can\'t be made a User.' })
    const w = mount(UsersSettings)
    await flushPromises()

    await row(w, 'carol').findAll('button').find((b) => b.text() === 'Make Admin')!.trigger('click')
    await flushPromises()

    expect(useToasts().toasts.value.map((t) => t.message)).toContain("The last Admin can't be made a User.")
  })
})
