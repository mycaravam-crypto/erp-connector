<script setup lang="ts">
import { useId, computed } from 'vue'
import FieldShell from './FieldShell.vue'

// Labelled native select bound with v-model, built on FieldShell; the options go in the default slot. Wires
// aria-invalid and aria-describedby like Input.
const props = withDefaults(
  defineProps<{
    /** Id for the select; generated when omitted. */
    id?: string
    /** Label shown above the select. */
    label?: string
    /** Hint shown under the select when there is no error. */
    helpText?: string
    /** Error message shown under the select; also marks it aria-invalid. */
    error?: string
    /** Disables the select. */
    disabled?: boolean
    /** Marks the field required, natively and with a * on the label. */
    required?: boolean
  }>(),
  { disabled: false, required: false },
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
