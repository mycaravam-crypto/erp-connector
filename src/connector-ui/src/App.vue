<script setup lang="ts">
import { RouterView } from 'vue-router'
</script>

<template>
  <header class="app-header">
    <span class="app-title">Connector — Release UI</span>
  </header>
  <main>
    <RouterView />
  </main>
</template>

<style scoped>
.app-header {
  padding: 0.75rem 1.5rem;
  background: #1a1a2e;
  color: #e2e8f0;
  font-weight: 600;
  font-size: 1rem;
  letter-spacing: 0.02em;
}

main {
  padding: 1.5rem;
}
</style>
