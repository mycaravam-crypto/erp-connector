<script setup lang="ts">
import { useId, computed } from 'vue'
import FieldShell from './FieldShell.vue'

// Labelled text input bound with v-model (string or number), built on FieldShell. Wires aria-invalid and
// aria-describedby to the error or help text; the id is generated when not given.
const props = withDefaults(
  defineProps<{
    /** Id for the input; generated when omitted. */
    id?: string
    /** Label shown above the input. */
    label?: string
    /** Hint shown under the input when there is no error. */
    helpText?: string
    /** Error message shown under the input; also marks it aria-invalid. */
    error?: string
    /** Native input type; 'text' by default. */
    type?: string
    /** Placeholder text. */
    placeholder?: string
    /** Disables the input. */
    disabled?: boolean
    /** Marks the field required, natively and with a * on the label. */
    required?: boolean
    /** Native autocomplete hint, e.g. 'username'. */
    autocomplete?: string
    /** Maximum length in characters. */
    maxlength?: number
    /** Minimum value for number inputs. */
    min?: number
    /** Maximum value for number inputs. */
    max?: number
  }>(),
  { type: 'text', disabled: false, required: false },
)

const model = defineModel<string | number>({ default: '' })

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
    <input
      :id="id"
      v-model="model"
      :type="type"
      :placeholder="placeholder"
      :disabled="disabled"
      :required="required"
      :autocomplete="autocomplete"
      :maxlength="maxlength"
      :min="min"
      :max="max"
      :aria-invalid="!!error || undefined"
      :aria-describedby="error ? errorId : helpText ? helpId : undefined"
      class="px-2.5 py-1.5 rounded-md text-sm bg-surface text-text-primary border outline-none transition-colors duration-fast placeholder:text-text-muted disabled:opacity-50 disabled:cursor-not-allowed focus:ring-2 focus:ring-focus"
      :class="error ? 'border-danger' : 'border-border-strong focus:border-brand'"
    />
  </FieldShell>
</template>
