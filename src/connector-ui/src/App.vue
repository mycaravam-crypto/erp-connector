<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, RouterLink, useRouter, useRoute } from 'vue-router'
import { getUsername, clearSession, isLoggedIn, revokeAllSessions } from '@/api/auth'
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

// SR-16: the revoke call also invalidates the token this request itself would use, so the local session
// is cleared unconditionally afterward — an already-dead token isn't worth keeping around either way.
async function revokeSessions() {
  await revokeAllSessions()
  clearSession()
  router.push({ name: 'login' })
}
</script>

<template>
  <header class="sticky top-0 z-10 flex items-center justify-between gap-3 sm:gap-6 px-4 sm:px-6 py-2.5 bg-nav/90 backdrop-blur-md text-nav-text border-b border-nav-border shadow-sm">
    <RouterLink :to="loggedIn ? { name: 'dashboard' } : { name: 'login' }" class="flex items-center gap-2 shrink-0 no-underline">
      <img :src="logo" alt="" class="w-6 h-6 rounded-md" />
      <span class="hidden sm:inline font-bold text-sm tracking-wide text-white">X5 Connector</span>
    </RouterLink>

    <ConnectorNav v-if="loggedIn" />

    <div class="flex items-center gap-3 shrink-0" :class="!loggedIn && 'ml-auto'">
      <ThemeToggle />
      <template v-if="loggedIn">
        <span class="w-px self-stretch bg-nav-border" aria-hidden="true" />
        <UserMenu :username="username" @sign-out="logout" @revoke-sessions="revokeSessions" />
      </template>
    </div>
  </header>

  <main class="p-6">
    <div class="max-w-[1280px] mx-auto">
      <RouterView />
    </div>
  </main>
</template>
