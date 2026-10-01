import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import ConnectorInstanceInfo from '@/components/ConnectorInstanceInfo.vue'

const instance = {
  application: 'x5-connector',
  version: '1.0.25',
  instanceId: '3f2c0a1e-5b6d-4c7e-8f90-123456789abc',
}

describe('ConnectorInstanceInfo', () => {
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
})
