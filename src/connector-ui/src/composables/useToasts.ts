import { ref } from 'vue'

export type ToastVariant = 'success' | 'danger' | 'info' | 'warning'

export interface Toast {
  id: number
  message: string
  variant: ToastVariant
}

const DEFAULT_DURATION_MS = 4000

const toasts = ref<Toast[]>([])
let nextId = 1

function dismissToast(id: number) {
  toasts.value = toasts.value.filter((t) => t.id !== id)
}

function clearToasts() {
  toasts.value = []
}

function pushToast(message: string, variant: ToastVariant = 'info', durationMs = DEFAULT_DURATION_MS) {
  const id = nextId++
  toasts.value = [...toasts.value, { id, message, variant }]
  if (durationMs > 0) {
    setTimeout(() => dismissToast(id), durationMs)
  }
  return id
}

/** App-wide toast queue for confirming saves/deletes/etc. — a reactive singleton in the same
 * module-scope-ref style as useTheme, rendered by the single <ToastHost/> mounted in App.vue. */
export function useToasts() {
  return {
    toasts,
    success: (message: string) => pushToast(message, 'success'),
    error: (message: string) => pushToast(message, 'danger'),
    info: (message: string) => pushToast(message, 'info'),
    warning: (message: string) => pushToast(message, 'warning'),
    dismiss: dismissToast,
    clear: clearToasts,
  }
}
