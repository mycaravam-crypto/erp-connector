import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ConnectorInstanceInfo from '@/components/ConnectorInstanceInfo.vue'

const instance = {
  application: 'x5-connector',
  version: '1.0.25',
  instanceId: '3f2c0a1e-5b6d-4c7e-8f90-123456789abc',
}

describe('ConnectorInstanceInfo', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('shows the instance ID and the application version', () => {
    const w = mount(ConnectorInstanceInfo, { props: { instance } })
    expect(w.find('[data-testid="instance-id"]').text()).toBe(instance.instanceId)
    expect(w.text()).toContain('x5-connector 1.0.25')
  })

  it('copies the instance ID to the clipboard', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    const w = mount(ConnectorInstanceInfo, { props: { instance } })
    await w.find('[data-testid="instance-id"]').trigger('click')
    await flushPromises()
    expect(writeText).toHaveBeenCalledWith(instance.instanceId)
    expect(w.find('[data-testid="instance-id"]').attributes('title')).toBe('Copied!')
  })

  it('regenerates the instance ID after confirmation and emits the new identity', async () => {
    const regenerated = { ...instance, instanceId: '9a8b7c6d-0000-4000-8000-000000000001' }
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve(regenerated) })
    vi.stubGlobal('fetch', fetchMock)
    const w = mount(ConnectorInstanceInfo, { props: { instance } })

    await w.find('[data-testid="regenerate-instance-id"]').trigger('click')
    expect(fetchMock).not.toHaveBeenCalled()
    expect(w.text()).toContain('Paired connector instances will see a new producer ID')
    const confirm = w.findAll('button').find((b) => b.text() === 'Regenerate')!
    await confirm.trigger('click')
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/settings/instance/regenerate',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(w.emitted('update:instance')?.[0]).toEqual([regenerated])
  })

  it('does not call the API when the confirmation is cancelled', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const w = mount(ConnectorInstanceInfo, { props: { instance } })

    await w.find('[data-testid="regenerate-instance-id"]').trigger('click')
    await w.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')

    expect(fetchMock).not.toHaveBeenCalled()
    expect(w.find('[data-testid="regenerate-instance-id"]').exists()).toBe(true)
  })
})
