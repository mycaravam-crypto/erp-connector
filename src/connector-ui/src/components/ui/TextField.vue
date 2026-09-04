<script setup lang="ts">
import { useId, computed } from 'vue'
import FieldShell from './FieldShell.vue'

const props = withDefaults(
  defineProps<{
    id?: string
    label?: string
    helpText?: string
    error?: string
    placeholder?: string
    disabled?: boolean
    required?: boolean
    rows?: number
    maxlength?: number
  }>(),
  { disabled: false, required: false, rows: 3 },
)

const model = defineModel<string>({ default: '' })

const autoId = useId()
const id = computed(() => props.id ?? autoId)
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
    <textarea
      :id="id"
      v-model="model"
      :rows="rows"
      :maxlength="maxlength"
      :placeholder="placeholder"
      :disabled="disabled"
      :required="required"
      :aria-invalid="!!error || undefined"
      :aria-describedby="error ? errorId : helpText ? helpId : undefined"
      class="px-2.5 py-1.5 rounded-md text-sm bg-surface text-text-primary border outline-none transition-colors duration-fast placeholder:text-text-muted disabled:opacity-50 disabled:cursor-not-allowed focus:ring-2 focus:ring-focus resize-y"
      :class="error ? 'border-danger' : 'border-border-strong focus:border-brand'"
    />
  </FieldShell>
</template>
