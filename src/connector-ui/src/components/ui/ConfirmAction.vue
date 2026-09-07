<script setup lang="ts">
import Button from '@/components/ui/Button.vue'

// De-duplicates the "click Delete → inline Confirm/Cancel" pattern repeated across run-controls panels
// and definition list rows. Renders as a fragment (no wrapping element) so it drops straight into
// whatever flex row the trigger button already lived in — `confirming` swaps the trigger slot for a
// prompt + confirm/cancel pair in place, matching how each call site already laid things out.
withDefaults(
  defineProps<{
    busy?: boolean
    prompt?: string
    confirmLabel?: string
    cancelLabel?: string
    /** 'button' renders confirm/cancel as full Button components with a text prompt (toolbar-style
     * actions); 'link' renders them as bare text links (table-row actions). */
    variant?: 'button' | 'link'
  }>(),
  { busy: false, prompt: 'Delete permanently?', confirmLabel: 'Confirm', cancelLabel: 'Cancel', variant: 'button' },
)

const emit = defineEmits<{ confirm: [] }>()
const confirming = defineModel<boolean>('confirming', { default: false })

function open() {
  confirming.value = true
}

function cancel() {
  confirming.value = false
}
</script>

<template>
  <slot v-if="!confirming" name="trigger" :open="open" />
  <template v-else-if="variant === 'link'">
    <button
      type="button"
      class="text-danger text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline disabled:opacity-50"
      :disabled="busy"
      @click="emit('confirm')"
    >{{ confirmLabel }}</button>
    <button
      type="button"
      class="text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
      @click="cancel"
    >{{ cancelLabel }}</button>
  </template>
  <template v-else>
    <span class="text-sm text-danger">{{ prompt }}</span>
    <Button variant="danger" :loading="busy" @click="emit('confirm')">{{ confirmLabel }}</Button>
    <Button variant="secondary" :disabled="busy" @click="cancel">{{ cancelLabel }}</Button>
  </template>
</template>
