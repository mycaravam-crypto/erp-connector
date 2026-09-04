<script setup lang="ts">
import { ref, watch, nextTick, onBeforeUnmount, useId } from 'vue'
import { X } from 'lucide-vue-next'
import Icon from './Icon.vue'

withDefaults(defineProps<{ title?: string; closeOnBackdrop?: boolean }>(), { closeOnBackdrop: true })

const open = defineModel<boolean>('open', { default: false })

const dialogRef = ref<HTMLElement | null>(null)
const titleId = useId()
let previouslyFocused: HTMLElement | null = null

function focusableEls(): HTMLElement[] {
  if (!dialogRef.value) return []
  return Array.from(
    dialogRef.value.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ),
  )
}

function close() {
  open.value = false
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') {
    close()
    return
  }
  if (e.key !== 'Tab') return
  const els = focusableEls()
  if (els.length === 0) return
  const first = els[0]!
  const last = els[els.length - 1]!
  if (e.shiftKey && document.activeElement === first) {
    e.preventDefault()
    last.focus()
  } else if (!e.shiftKey && document.activeElement === last) {
    e.preventDefault()
    first.focus()
  }
}

watch(open, async (isOpen) => {
  if (isOpen) {
    previouslyFocused = document.activeElement as HTMLElement | null
    await nextTick()
    focusableEls()[0]?.focus()
  } else {
    previouslyFocused?.focus()
  }
})

onBeforeUnmount(() => {
  if (open.value) previouslyFocused?.focus()
})
</script>

<template>
  <div v-if="open" class="fixed inset-0 z-50 flex items-center justify-center p-4">
    <div class="absolute inset-0 bg-black/50" @click="closeOnBackdrop && close()" />
    <div
      ref="dialogRef"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="title ? titleId : undefined"
      class="relative w-full max-w-md max-h-[90vh] overflow-y-auto rounded-lg border border-border bg-surface-elevated shadow-lg"
      @keydown="onKeydown"
    >
      <div v-if="title" class="flex items-center justify-between gap-4 border-b border-border px-5 py-4">
        <h2 :id="titleId" class="m-0 text-base font-semibold text-text-primary">{{ title }}</h2>
        <button
          type="button"
          class="shrink-0 rounded-md p-1 text-text-muted cursor-pointer hover:bg-surface hover:text-text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus"
          aria-label="Close"
          @click="close"
        >
          <Icon :icon="X" :size="16" />
        </button>
      </div>
      <div class="px-5 py-4">
        <slot />
      </div>
      <div v-if="$slots.footer" class="flex items-center justify-end gap-2 border-t border-border px-5 py-4">
        <slot name="footer" />
      </div>
    </div>
  </div>
</template>
