<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, RouterLink, useRouter, useRoute } from 'vue-router'
import { getUsername, clearSession, isLoggedIn } from '@/api/auth'

const router = useRouter()
const route = useRoute()
const loggedIn = computed(() => { void route.path; return isLoggedIn() })
const username = computed(() => { void route.path; return getUsername() })

function logout() {
  clearSession()
  router.push({ name: 'login' })
}

const steps = [
  { name: 'connect', label: 'Connect', num: 1 },
  { name: 'source-schema', label: 'Source Schema', num: 2 },
  { name: 'export-schema', label: 'Export Schema', num: 3 },
  { name: 'exports', label: 'Export', num: 4 },
]
</script>

<template>
  <header class="app-header">
    <span class="app-title">Connector</span>
    <nav v-if="loggedIn" class="app-nav">
      <template v-for="(step, idx) in steps" :key="step.name">
        <RouterLink :to="{ name: step.name }" class="step-link" active-class="step-active">
          <span class="step-num">{{ step.num }}</span>
          <span class="step-label">{{ step.label }}</span>
        </RouterLink>
        <span v-if="idx < steps.length - 1" class="step-sep" aria-hidden="true">→</span>
      </template>
    </nav>
    <div v-if="loggedIn" class="user-row">
      <span class="user-name">{{ username }}</span>
      <button class="logout-btn" @click="logout">Sign out</button>
    </div>
  </header>
  <main>
    <RouterView />
  </main>
</template>

<style scoped>
.app-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.6rem 1.5rem;
  background: #1a1a2e;
  color: #e2e8f0;
  gap: 1.5rem;
}

.app-title {
  font-weight: 700;
  font-size: 1rem;
  letter-spacing: 0.04em;
  flex-shrink: 0;
  color: #f1f5f9;
}

.app-nav {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  flex: 1;
}

.step-link {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.3rem 0.7rem;
  border-radius: 0.375rem;
  text-decoration: none;
  color: #94a3b8;
  transition: background 0.1s, color 0.1s;
  white-space: nowrap;
}

.step-link:hover {
  background: #2d2d4e;
  color: #e2e8f0;
}

.step-active {
  background: #2d2d4e;
  color: #f1f5f9;
}

.step-num {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 1.3rem;
  height: 1.3rem;
  border-radius: 50%;
  border: 1px solid currentColor;
  font-size: 0.7rem;
  font-weight: 700;
  flex-shrink: 0;
}

.step-active .step-num {
  background: #4f46e5;
  border-color: #4f46e5;
  color: #fff;
}

.step-label {
  font-size: 0.82rem;
}

.step-sep {
  color: #475569;
  font-size: 0.75rem;
  padding: 0 0.1rem;
  flex-shrink: 0;
}

.user-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-shrink: 0;
}

.user-name {
  font-size: 0.82rem;
  color: #94a3b8;
}

.logout-btn {
  background: none;
  border: 1px solid #475569;
  color: #cbd5e1;
  border-radius: 0.375rem;
  padding: 0.2rem 0.65rem;
  font-size: 0.78rem;
  cursor: pointer;
}

.logout-btn:hover {
  background: #334155;
}

main {
  padding: 1.5rem;
}
</style>
