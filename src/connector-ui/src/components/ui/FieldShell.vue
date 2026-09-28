<script setup lang="ts">
// Layout shared by Input and Select: label (with a required marker), the control in the default slot, then
// either the error or the help text underneath. The ids let the control reference its label, help and error
// for accessibility.
defineProps<{
  /** Id of the control in the slot; the label's `for` points at it. */
  id: string
  /** Id given to the help text, for the control's aria-describedby. */
  helpId: string
  /** Id given to the error message, for the control's aria-describedby. */
  errorId: string
  /** Label text; omitted when not set. */
  label?: string
  /** Adds a required marker (*) to the label. */
  required?: boolean
  /** Hint shown under the control when there is no error. */
  helpText?: string
  /** Error message shown under the control, replacing the help text. */
  error?: string
}>()
</script>

<template>
  <div class="flex flex-col gap-1">
    <label v-if="label" :for="id" class="text-sm font-semibold text-text-primary">
      {{ label }}<span v-if="required" class="text-danger" aria-hidden="true"> *</span>
    </label>
    <slot />
    <p v-if="error" :id="errorId" class="text-sm text-danger m-0">{{ error }}</p>
    <p v-else-if="helpText" :id="helpId" class="text-sm text-text-muted m-0">{{ helpText }}</p>
  </div>
</template>
