<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, RouterLink, useRouter, useRoute } from 'vue-router'
import { getUsername, clearSession, isLoggedIn } from '@/api/auth'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/UserMenu.vue'
import ConnectorNav from '@/components/ConnectorNav.vue'
import logo from '@/assets/logo.svg'

const router = useRouter()
const route = useRoute()
const loggedIn = computed(() => { void route.path; return isLoggedIn() })
const username = computed(() => { void route.path; return getUsername() })

function logout() {
  clearSession()
  router.push({ name: 'login' })
}
</script>

<template>
  <header class="sticky top-0 z-10 flex items-center justify-between gap-6 px-6 py-2.5 bg-nav/90 backdrop-blur-md text-nav-text border-b border-nav-border shadow-sm">
    <RouterLink :to="loggedIn ? { name: 'dashboard' } : { name: 'login' }" class="flex items-center gap-2 shrink-0 no-underline">
      <img :src="logo" alt="" class="w-6 h-6 rounded-md" />
      <span class="font-bold text-sm tracking-wide text-white">X5 Connector</span>
    </RouterLink>

    <ConnectorNav v-if="loggedIn" />

    <div class="flex items-center gap-3 shrink-0" :class="!loggedIn && 'ml-auto'">
      <ThemeToggle />
      <template v-if="loggedIn">
        <span class="w-px self-stretch bg-nav-border" aria-hidden="true" />
        <UserMenu :username="username" @sign-out="logout" />
      </template>
    </div>
  </header>

  <main class="p-6">
    <RouterView />
  </main>
</template>
