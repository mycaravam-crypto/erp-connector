<script setup lang="ts">
import { useId } from 'vue'
import FieldShell from './FieldShell.vue'

withDefaults(
  defineProps<{
    label?: string
    helpText?: string
    error?: string
    disabled?: boolean
    required?: boolean
  }>(),
  { disabled: false, required: false },
)

const model = defineModel<string>({ default: '' })

const id = useId()
const helpId = useId()
const errorId = useId()
</script>

<template>
  <FieldShell
    :id="id"
    :help-id="helpId"
    :error-id="errorId"
    :label="label"
    :required="required"
    :help-text="helpText"
    :error="error"
  >
    <select
      :id="id"
      v-model="model"
      :disabled="disabled"
      :required="required"
      :aria-invalid="!!error || undefined"
      :aria-describedby="error ? errorId : helpText ? helpId : undefined"
      class="px-2.5 py-1.5 rounded-md text-sm bg-surface text-text-primary border outline-none transition-colors duration-fast disabled:opacity-50 disabled:cursor-not-allowed focus:ring-2 focus:ring-focus"
      :class="error ? 'border-danger' : 'border-border-strong focus:border-brand'"
    >
      <slot />
    </select>
  </FieldShell>
</template>
