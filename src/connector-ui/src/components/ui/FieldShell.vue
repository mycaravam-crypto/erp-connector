<script setup lang="ts">
defineProps<{
  id: string
  helpId: string
  errorId: string
  label?: string
  required?: boolean
  helpText?: string
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
