<script setup lang="ts">
import { useRouter, type RouteLocationRaw } from 'vue-router'
import { ChevronLeft } from 'lucide-vue-next'
import Icon from './Icon.vue'
import Button from './Button.vue'

const props = defineProps<{ to: RouteLocationRaw }>()
const router = useRouter()
</script>

<template>
  <Button variant="ghost" class="mb-4" @click="router.push(props.to)">
    <template #icon><Icon :icon="ChevronLeft" :size="16" /></template>
    Back to list
  </Button>
</template>
