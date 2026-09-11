<script setup lang="ts">
import { X } from 'lucide-vue-next'
import Icon from './Icon.vue'
import { useToasts } from '@/composables/useToasts'

const { toasts, dismiss } = useToasts()

const bgClasses: Record<string, string> = {
  success: 'bg-success-bg border-success/25',
  warning: 'bg-warning-bg border-warning/25',
  danger: 'bg-danger-bg border-danger/25',
  info: 'bg-info-bg border-info/25',
}
</script>

<template>
  <div class="fixed bottom-4 right-4 z-[100] flex flex-col gap-2 w-full max-w-sm" aria-live="polite" aria-atomic="false">
    <TransitionGroup name="toast">
      <div
        v-for="toast in toasts"
        :key="toast.id"
        class="flex items-start gap-3 rounded-lg border px-4 py-3 text-sm shadow-lg text-text-primary"
        :class="bgClasses[toast.variant]"
        role="status"
      >
        <p class="min-w-0 flex-1 m-0 break-words">{{ toast.message }}</p>
        <button
          type="button"
          class="shrink-0 rounded-md p-0.5 text-text-muted cursor-pointer hover:bg-surface hover:text-text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus"
          aria-label="Dismiss"
          @click="dismiss(toast.id)"
        >
          <Icon :icon="X" :size="16" />
        </button>
      </div>
    </TransitionGroup>
  </div>
</template>

<style scoped>
.toast-enter-active,
.toast-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}
.toast-enter-from,
.toast-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
