<script setup lang="ts">
defineProps<{ status: string }>()
</script>

<template>
  <span :class="[
    'inline-block px-2 py-0.5 rounded-full text-xs font-semibold',
    status.toLowerCase() === 'pending' || status.toLowerCase() === 'running' ? 'bg-yellow-100 text-yellow-800' :
    status.toLowerCase() === 'released' || status.toLowerCase() === 'success' ? 'bg-green-100 text-green-800' :
    status.toLowerCase() === 'failed'    ? 'bg-red-100 text-red-800' :
    status.toLowerCase() === 'skipped'  ? 'bg-slate-100 text-slate-500' :
    status.toLowerCase() === 'delivered' ? 'bg-blue-100 text-blue-800' :
    'bg-slate-100 text-slate-600'
  ]">{{ status }}</span>
</template>
