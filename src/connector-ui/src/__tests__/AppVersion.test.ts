import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import AppVersion from '@/components/AppVersion.vue'
import * as versionApi from '@/api/version'

beforeEach(() => vi.restoreAllMocks())

describe('AppVersion', () => {
  it('shows the version reported by the API', async () => {
    vi.spyOn(versionApi, 'getVersion').mockResolvedValueOnce('1.4.2')
    const w = mount(AppVersion)
    await flushPromises()
    expect(w.find('[data-testid="app-version"]').text()).toBe('v1.4.2')
  })

  it('renders nothing when the version cannot be loaded', async () => {
    vi.spyOn(versionApi, 'getVersion').mockRejectedValueOnce(new Error('down'))
    const w = mount(AppVersion)
    await flushPromises()
    expect(w.find('[data-testid="app-version"]').exists()).toBe(false)
  })
})
