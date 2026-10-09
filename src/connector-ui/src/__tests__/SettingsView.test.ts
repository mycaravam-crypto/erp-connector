import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import SettingsView from '@/views/SettingsView.vue'
import * as schedulerApi from '@/api/scheduler'
import * as brandingApi from '@/api/branding'
import * as instanceApi from '@/api/instance'
import * as usersApi from '@/api/users'
import * as permissionsApi from '@/api/permissions'
import { signInAs } from './signInAs'

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(schedulerApi, 'getSchedulerConfig').mockResolvedValue({ scheduledTimeUtc: '06:00', retentionDays: 30, format: 'xlsx' })
  vi.spyOn(schedulerApi, 'getGdprDeniedFields').mockResolvedValue({ fields: [] } as unknown as Awaited<ReturnType<typeof schedulerApi.getGdprDeniedFields>>)
  vi.spyOn(brandingApi, 'getBranding').mockResolvedValue({ appName: null, logoDataUrl: null, faviconDataUrl: null, backgroundImageDataUrl: null })
  vi.spyOn(instanceApi, 'getConnectorInstance').mockResolvedValue({ instanceId: 'abc', retiredInstanceIds: [] } as unknown as Awaited<ReturnType<typeof instanceApi.getConnectorInstance>>)
  vi.spyOn(usersApi, 'listUsers').mockResolvedValue([])
  vi.spyOn(permissionsApi, 'getRolePermissions').mockResolvedValue({ catalogue: [], userPermissions: [] })
})

describe('SettingsView', () => {
  it('shows a User only the sections they hold, and loads nothing else', async () => {
    await signInAs('User', ['settings.scheduler'])
    const w = mount(SettingsView)
    await flushPromises()

    expect(w.text()).toContain('Export Scheduler')
    expect(w.text()).not.toContain('GDPR')
    expect(w.text()).not.toContain('Users')
    expect(brandingApi.getBranding).not.toHaveBeenCalled()
    expect(instanceApi.getConnectorInstance).not.toHaveBeenCalled()
    expect(usersApi.listUsers).not.toHaveBeenCalled()
  })

  it('shows an Admin the Users and Permissions sections', async () => {
    await signInAs('Admin')
    const w = mount(SettingsView)
    await flushPromises()

    const headings = w.findAll('h2').map((h) => h.text())
    expect(headings).toContain('Users')
    expect(headings).toContain('Permissions')
  })
})
