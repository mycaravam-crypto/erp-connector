<script setup lang="ts">
import { computed } from 'vue'
import { Check, X, Clock, Truck, Ban } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

const props = defineProps<{ status: string }>()

const icon = computed(() => {
  switch (props.status.toLowerCase()) {
    case 'pending':
    case 'running':
      return Clock
    case 'released':
    case 'success':
      return Check
    case 'failed':
      return X
    case 'skipped':
      return Ban
    case 'delivered':
      return Truck
    default:
      return null
  }
})
</script>

<template>
  <span :class="[
    'inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-semibold',
    status.toLowerCase() === 'pending' || status.toLowerCase() === 'running' ? 'bg-yellow-100 text-yellow-800' :
    status.toLowerCase() === 'released' || status.toLowerCase() === 'success' ? 'bg-green-100 text-green-800' :
    status.toLowerCase() === 'failed'    ? 'bg-red-100 text-red-800' :
    status.toLowerCase() === 'skipped'  ? 'bg-slate-100 text-slate-500' :
    status.toLowerCase() === 'delivered' ? 'bg-blue-100 text-blue-800' :
    'bg-slate-100 text-slate-600'
  ]"><Icon v-if="icon" :icon="icon" :size="16" />{{ status }}</span>
</template>
