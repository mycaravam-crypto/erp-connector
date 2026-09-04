<script setup lang="ts">
import Alert from '@/components/ui/Alert.vue'

defineProps<{ issues: string[] }>()
</script>

<template>
  <Alert v-if="issues.length > 0" variant="danger" class="validation-alert py-2 px-3 text-xs">
    <ul class="list-disc pl-4 m-0">
      <li v-for="(issue, i) in issues" :key="i">{{ issue }}</li>
    </ul>
  </Alert>
</template>
