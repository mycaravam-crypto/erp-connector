<script setup lang="ts">
import type { Component } from 'vue'

withDefaults(defineProps<{ icon: Component; size?: 16 | 20 | 24 }>(), { size: 20 })
</script>

<template>
  <component :is="icon" :size="size" :stroke-width="2" aria-hidden="true" class="shrink-0" />
</template>
