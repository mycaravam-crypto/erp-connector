<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, RouterLink, useRouter, useRoute } from 'vue-router'
import { getUsername, clearSession, isLoggedIn } from '@/api/auth'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/UserMenu.vue'
import Icon from '@/components/ui/Icon.vue'
import { ChevronRight, Check } from 'lucide-vue-next'
import logo from '@/assets/logo.svg'

const router = useRouter()
const route = useRoute()
const loggedIn = computed(() => { void route.path; return isLoggedIn() })
const username = computed(() => { void route.path; return getUsername() })

function logout() {
  clearSession()
  router.push({ name: 'login' })
}

// One-time connector setup — a short, ordered sequence the user completes once. No step numbers:
// numbering suggested a wizard the user finishes and leaves, but Managed Export below is an
// ongoing operational area, not a step in the same sequence.
const setupSteps = [
  { name: 'connect', label: 'Connect' },
  { name: 'source-schema', label: 'Source Schema' },
  { name: 'export-schema', label: 'CMDB Export Mapping' },
]

const MANAGED_EXPORT_ROUTES = new Set(['exports', 'export-detail'])
const managedExportActive = computed(() => MANAGED_EXPORT_ROUTES.has(String(route.name)))

// The current setup step, if the active route is one of them.
// On a secondary page (Settings, Audit Log, ...) or on Managed Export this is -1, so no step
// shows as active — we don't know (or it doesn't apply) where in setup the user left off.
const currentStepIndex = computed(() =>
  setupSteps.findIndex((s) => route.path === `/${s.name}` || route.path.startsWith(`/${s.name}/`)),
)

const navLinkClass =
  'flex items-center gap-1.5 px-3 py-1.5 rounded-md no-underline transition-colors duration-fast ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav'

function isStepCompleted(idx: number): boolean {
  // Once the connector is operating (Managed Export), setup is implicitly done.
  if (managedExportActive.value) return true
  return idx < currentStepIndex.value
}

function stepLinkClass(idx: number): string {
  return idx === currentStepIndex.value
    ? `${navLinkClass} bg-nav-hover text-nav-text-strong`
    : `${navLinkClass} text-nav-text hover:bg-nav-hover hover:text-nav-text-strong`
}

const managedExportLinkClass = computed(() =>
  managedExportActive.value
    ? `${navLinkClass} bg-nav-hover text-nav-text-strong`
    : `${navLinkClass} text-nav-text hover:bg-nav-hover hover:text-nav-text-strong`,
)
</script>

<template>
  <header class="flex items-center justify-between gap-6 px-6 py-2.5 bg-nav text-nav-text">
    <span class="flex items-center gap-2 shrink-0">
      <img :src="logo" alt="" class="w-6 h-6 rounded-md" />
      <span class="font-bold text-sm tracking-wide text-white">X5 Connector</span>
    </span>

    <nav v-if="loggedIn" aria-label="Connector" class="flex items-center gap-1 flex-1">
      <template v-for="(step, idx) in setupSteps" :key="step.name">
        <RouterLink :to="{ name: step.name }" :class="stepLinkClass(idx)">
          <span
            class="flex items-center justify-center w-5 h-5 rounded-full border shrink-0"
            :class="isStepCompleted(idx) ? 'border-success text-success' : 'border-current'"
          >
            <Icon v-if="isStepCompleted(idx)" :icon="Check" :size="16" />
          </span>
          <span class="text-[0.82rem]">{{ step.label }}</span>
        </RouterLink>
        <span v-if="idx < setupSteps.length - 1" class="text-nav-border px-0.5 shrink-0" aria-hidden="true">
          <Icon :icon="ChevronRight" :size="16" />
        </span>
      </template>

      <span class="w-px h-5 mx-2.5 bg-nav-border shrink-0" aria-hidden="true" />

      <RouterLink :to="{ name: 'exports' }" :class="managedExportLinkClass">
        <span class="w-1.5 h-1.5 rounded-full bg-current opacity-70 shrink-0" aria-hidden="true" />
        <span class="text-[0.82rem]">Managed Export</span>
      </RouterLink>
    </nav>

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
