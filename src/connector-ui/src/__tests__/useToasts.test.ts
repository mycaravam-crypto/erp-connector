import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { useToasts } from '@/composables/useToasts'

beforeEach(() => {
  vi.useFakeTimers()
  useToasts().clear()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('useToasts', () => {
  it('starts empty', () => {
    expect(useToasts().toasts.value).toEqual([])
  })

  it('pushes a success toast', () => {
    const { toasts, success } = useToasts()
    success('Saved.')
    expect(toasts.value).toHaveLength(1)
    expect(toasts.value[0]).toMatchObject({ message: 'Saved.', variant: 'success' })
  })

  it('pushes error/info/warning toasts with the matching variant', () => {
    const { toasts, error, info, warning } = useToasts()
    error('Failed.')
    info('Heads up.')
    warning('Careful.')
    expect(toasts.value.map((t) => t.variant)).toEqual(['danger', 'info', 'warning'])
  })

  it('assigns each toast a unique id', () => {
    const { toasts, success } = useToasts()
    success('One')
    success('Two')
    expect(toasts.value[0]!.id).not.toBe(toasts.value[1]!.id)
  })

  it('auto-dismisses a toast after the default duration', () => {
    const { toasts, success } = useToasts()
    success('Saved.')
    expect(toasts.value).toHaveLength(1)
    vi.advanceTimersByTime(4000)
    expect(toasts.value).toHaveLength(0)
  })

  it('dismisses a toast on demand', () => {
    const { toasts, success, dismiss } = useToasts()
    const id = success('Saved.')
    dismiss(id)
    expect(toasts.value).toHaveLength(0)
  })

  it('clears every toast', () => {
    const { toasts, success, clear } = useToasts()
    success('One')
    success('Two')
    clear()
    expect(toasts.value).toHaveLength(0)
  })
})
