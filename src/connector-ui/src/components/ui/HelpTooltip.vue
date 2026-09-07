<script setup lang="ts">
// A small "?" affordance for explaining a non-obvious field, section, or concept in place —
// click (or Enter/Space) to open a short explanation, Escape or an outside click to dismiss.
// Content is plain semantic markup in the default slot (p/strong/code/ul); :slotted() below gives
// it consistent typography so call sites don't have to repeat utility classes every time.
import { ref, useId, watch, onBeforeUnmount } from 'vue'
import { HelpCircle } from 'lucide-vue-next'
import Icon from './Icon.vue'

withDefaults(defineProps<{
  /** Accessible name for the trigger button — say what it explains, e.g. "About contract versions". */
  label?: string
  title?: string
  align?: 'left' | 'right' | 'center'
}>(), {
  label: 'Help',
  align: 'left',
})

const open = ref(false)
const panelId = useId()
const rootRef = ref<HTMLElement | null>(null)

function toggle() {
  open.value = !open.value
}
function close() {
  open.value = false
}
function onDocumentClick(e: MouseEvent) {
  if (rootRef.value && !rootRef.value.contains(e.target as Node)) close()
}
function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

watch(open, (isOpen) => {
  if (isOpen) {
    document.addEventListener('click', onDocumentClick, true)
    document.addEventListener('keydown', onKeydown)
  } else {
    document.removeEventListener('click', onDocumentClick, true)
    document.removeEventListener('keydown', onKeydown)
  }
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocumentClick, true)
  document.removeEventListener('keydown', onKeydown)
})

const alignClasses: Record<string, string> = {
  left: 'left-0',
  right: 'right-0',
  center: 'left-1/2 -translate-x-1/2',
}
</script>

<template>
  <span ref="rootRef" class="relative inline-flex align-middle">
    <button
      type="button"
      class="inline-flex items-center justify-center text-text-muted rounded-full cursor-pointer bg-transparent border-0 p-0 hover:text-brand focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus"
      :aria-label="label"
      :aria-expanded="open"
      :aria-controls="panelId"
      @click="toggle"
    >
      <Icon :icon="HelpCircle" :size="16" />
    </button>

    <div
      v-if="open"
      :id="panelId"
      role="tooltip"
      class="help-tooltip-panel absolute z-40 top-full mt-2 w-80 max-w-[calc(100vw-2rem)] rounded-lg border border-border bg-surface-elevated shadow-lg px-4 py-3 text-left text-xs leading-relaxed text-text-secondary normal-case font-normal"
      :class="alignClasses[align]"
    >
      <p v-if="title" class="m-0 mb-1.5 text-sm font-semibold text-text-primary">{{ title }}</p>
      <slot />
    </div>
  </span>
</template>

<style scoped>
.help-tooltip-panel :slotted(p) {
  margin: 0 0 0.5rem;
}
.help-tooltip-panel :slotted(p:last-child) {
  margin-bottom: 0;
}
.help-tooltip-panel :slotted(strong) {
  color: var(--color-text-primary);
  font-weight: 600;
}
.help-tooltip-panel :slotted(code) {
  font-family: ui-monospace, monospace;
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 0.25rem;
  padding: 0.05rem 0.3rem;
  font-size: 0.7rem;
  color: var(--color-text-primary);
}
.help-tooltip-panel :slotted(ul) {
  margin: 0 0 0.5rem;
  padding-left: 1.1rem;
}
.help-tooltip-panel :slotted(ul:last-child) {
  margin-bottom: 0;
}
.help-tooltip-panel :slotted(li) {
  margin-bottom: 0.25rem;
}
.help-tooltip-panel :slotted(li:last-child) {
  margin-bottom: 0;
}
.help-tooltip-panel :slotted(a) {
  color: var(--color-brand);
}
</style>
