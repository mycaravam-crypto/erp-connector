import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import PermissionsSettings from '@/components/PermissionsSettings.vue'
import * as permissionsApi from '@/api/permissions'
import * as usersApi from '@/api/users'
import type { PermissionGroup } from '@/api/permissions'

const CATALOGUE: PermissionGroup[] = [
  {
    key: 'exportJobs',
    label: 'Export Jobs',
    permissions: [
      { key: 'exportJobs.view', label: 'View', description: 'See export jobs.' },
      { key: 'exportJobs.create', label: 'Create', description: 'Create export jobs.' },
    ],
  },
  {
    key: 'managedExport',
    label: 'Managed Export',
    permissions: [
      { key: 'managedExport.view', label: 'View', description: 'See runs.' },
      { key: 'managedExport.release', label: 'Release', description: 'Release runs.' },
    ],
  },
]

function mockLoad(userPermissions: string[], admins = 2) {
  vi.spyOn(permissionsApi, 'getRolePermissions').mockResolvedValue({ catalogue: CATALOGUE, userPermissions })
  vi.spyOn(usersApi, 'listUsers').mockResolvedValue([
    ...Array.from({ length: admins }, (_, i) => ({ username: `admin${i}`, role: 'Admin' as const, createdAt: '' })),
    { username: 'carol', role: 'User' as const, createdAt: '' },
  ])
}

function checkbox(w: ReturnType<typeof mount>, label: string, group: string) {
  const card = w.findAll('h3').find((h) => h.text() === group)!.element.closest('div.grid > *')!
  const row = Array.from(card.querySelectorAll('label')).find((l) => l.textContent?.includes(label))!
  return row.querySelector('input') as HTMLInputElement
}

beforeEach(() => vi.restoreAllMocks())

describe('PermissionsSettings', () => {
  it('ticks View along with any other permission of the same menu item', async () => {
    mockLoad([])
    const w = mount(PermissionsSettings)
    await flushPromises()

    const create = checkbox(w, 'Create', 'Export Jobs')
    create.checked = true
    create.dispatchEvent(new Event('change'))
    await flushPromises()

    expect(checkbox(w, 'View', 'Export Jobs').checked).toBe(true)
  })

  it('clears the whole menu item when View is unticked', async () => {
    mockLoad(['exportJobs.view', 'exportJobs.create'])
    const w = mount(PermissionsSettings)
    await flushPromises()

    const view = checkbox(w, 'View', 'Export Jobs')
    view.checked = false
    view.dispatchEvent(new Event('change'))
    await flushPromises()

    expect(checkbox(w, 'Create', 'Export Jobs').checked).toBe(false)
  })

  it('saves the ticked set', async () => {
    mockLoad(['exportJobs.view'])
    const save = vi
      .spyOn(permissionsApi, 'saveRolePermissions')
      .mockResolvedValueOnce({ ok: true, data: { catalogue: CATALOGUE, userPermissions: ['exportJobs.view'] } })
    const w = mount(PermissionsSettings)
    await flushPromises()

    await w.findAll('button').find((b) => b.text() === 'Save Permissions')!.trigger('click')
    await flushPromises()

    expect(save).toHaveBeenCalledWith(['exportJobs.view'])
    expect(w.text()).toContain('Permissions saved.')
  })

  it('warns when nobody could ever release, and not when two Admins exist', async () => {
    mockLoad([], 1)
    const w = mount(PermissionsSettings)
    await flushPromises()
    expect(w.text()).toContain('Nobody can release Managed Export runs')

    vi.restoreAllMocks()
    mockLoad([], 2)
    const w2 = mount(PermissionsSettings)
    await flushPromises()
    expect(w2.text()).not.toContain('Nobody can release')
  })
})
