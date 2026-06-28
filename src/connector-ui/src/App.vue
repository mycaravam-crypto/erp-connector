<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, RouterLink, useRouter } from 'vue-router'
import { getUsername, clearSession, isLoggedIn } from '@/api/auth'

const router = useRouter()
const loggedIn = computed(isLoggedIn)
const username = computed(getUsername)

function logout() {
  clearSession()
  router.push({ name: 'login' })
}
</script>

<template>
  <header class="app-header">
    <span class="app-title">Connector</span>
    <nav v-if="loggedIn" class="app-nav">
      <RouterLink :to="{ name: 'exports' }" class="nav-link">Export Runs</RouterLink>
      <RouterLink :to="{ name: 'erp-database' }" class="nav-link">ERP Database</RouterLink>
      <RouterLink :to="{ name: 'schema' }" class="nav-link">Export Schema</RouterLink>
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
  padding: 0.75rem 1.5rem;
  background: #1a1a2e;
  color: #e2e8f0;
  gap: 1.5rem;
}

.app-title {
  font-weight: 600;
  font-size: 1rem;
  letter-spacing: 0.02em;
  flex-shrink: 0;
}

.app-nav {
  display: flex;
  gap: 0.25rem;
  flex: 1;
}

.nav-link {
  padding: 0.3rem 0.75rem;
  border-radius: 0.375rem;
  font-size: 0.85rem;
  color: #94a3b8;
  text-decoration: none;
  transition: background 0.1s, color 0.1s;
}

.nav-link:hover {
  background: #334155;
  color: #e2e8f0;
}

.nav-link.router-link-active {
  background: #334155;
  color: #f1f5f9;
}

.user-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  flex-shrink: 0;
}

.user-name {
  font-size: 0.85rem;
  color: #94a3b8;
}

.logout-btn {
  background: none;
  border: 1px solid #475569;
  color: #cbd5e1;
  border-radius: 0.375rem;
  padding: 0.25rem 0.75rem;
  font-size: 0.8rem;
  cursor: pointer;
}

.logout-btn:hover {
  background: #334155;
}

main {
  padding: 1.5rem;
}
</style>
