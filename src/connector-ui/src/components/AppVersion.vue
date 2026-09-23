<script setup lang="ts">
import { ref } from 'vue'
import { getVersion } from '@/api/version'

// Comes from the running API, not baked into the bundle, so it shows what the container actually
// runs even if the browser still has an older index.html cached.
const version = ref<string | null>(null)
getVersion()
  .then((v) => (version.value = v))
  .catch(() => {
    // Cosmetic — never block the UI on it.
  })
</script>

<template>
  <span v-if="version" class="text-xs text-text-secondary" data-testid="app-version">v{{ version }}</span>
</template>
