<script setup lang="ts">
import { Check, X } from 'lucide-vue-next'
import Icon from './Icon.vue'
import Alert from './Alert.vue'

defineProps<{
  status: 'idle' | 'ok' | 'error'
  message: string
}>()
</script>

<template>
  <Alert v-if="status === 'ok'" variant="success" class="mt-4">
    <template #icon><Icon :icon="Check" :size="16" /></template>
    {{ message }}
  </Alert>
  <Alert v-else-if="status === 'error'" variant="danger" class="mt-4">
    <template #icon><Icon :icon="X" :size="16" /></template>
    {{ message }}
  </Alert>
</template>
