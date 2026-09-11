import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import ToastHost from '@/components/ui/ToastHost.vue'
import { useToasts } from '@/composables/useToasts'

beforeEach(() => useToasts().clear())

describe('ToastHost', () => {
  it('renders nothing when there are no toasts', () => {
    const w = mount(ToastHost)
    expect(w.findAll('[role="status"]')).toHaveLength(0)
  })

  it('renders a pushed toast with its message and variant styling', () => {
    useToasts().success('Export definition saved.')
    const w = mount(ToastHost)
    expect(w.text()).toContain('Export definition saved.')
    expect(w.find('[role="status"]').classes()).toContain('bg-success-bg')
  })

  it('renders multiple toasts stacked', () => {
    const { success, error } = useToasts()
    success('Saved.')
    error('Failed.')
    const w = mount(ToastHost)
    expect(w.findAll('[role="status"]')).toHaveLength(2)
  })

  it('dismisses a toast when its close button is clicked', async () => {
    useToasts().success('Saved.')
    const w = mount(ToastHost)
    await w.find('button[aria-label="Dismiss"]').trigger('click')
    expect(useToasts().toasts.value).toHaveLength(0)
  })
})
