<script setup lang="ts">
import { computed } from 'vue'
import { RouterView, RouterLink, useRouter, useRoute } from 'vue-router'
import { getUsername, clearSession, isLoggedIn } from '@/api/auth'
import ThemeToggle from '@/components/ThemeToggle.vue'
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

const steps = [
  { name: 'connect', label: 'Connect', num: 1 },
  { name: 'source-schema', label: 'Source Schema', num: 2 },
  { name: 'export-schema', label: 'Export Schema', num: 3 },
  { name: 'exports', label: 'Export', num: 4 },
]

// The current step, if the active route is one of the golden-path steps.
// On a secondary page (Settings, Audit Log, ...) this is -1, so no step
// shows as active or completed — we don't know where in the flow the user
// left off from a page outside it.
const currentStepIndex = computed(() =>
  steps.findIndex((s) => route.path === `/${s.name}` || route.path.startsWith(`/${s.name}/`)),
)

const navLinkClass =
  'flex items-center gap-1.5 px-3 py-1.5 rounded-md no-underline transition-colors duration-fast ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav'

const secondaryLinkClass =
  'text-[0.82rem] text-nav-text no-underline hover:text-nav-text-strong transition-colors rounded-sm ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav'

function isStepCompleted(idx: number): boolean {
  return idx < currentStepIndex.value
}

function stepLinkClass(idx: number): string {
  return idx === currentStepIndex.value
    ? `${navLinkClass} bg-nav-hover text-nav-text-strong`
    : `${navLinkClass} text-nav-text hover:bg-nav-hover hover:text-nav-text-strong`
}
</script>

<template>
  <header class="flex items-center justify-between gap-6 px-6 py-2.5 bg-nav text-nav-text">
    <span class="flex items-center gap-2 shrink-0">
      <img :src="logo" alt="" class="w-6 h-6 rounded-md" />
      <span class="font-bold text-sm tracking-wide text-white">X5 Connector</span>
    </span>

    <nav v-if="loggedIn" aria-label="Workflow steps" class="flex items-center gap-1 flex-1">
      <template v-for="(step, idx) in steps" :key="step.name">
        <RouterLink :to="{ name: step.name }" :class="stepLinkClass(idx)">
          <span
            class="flex items-center justify-center w-5 h-5 rounded-full border text-[0.65rem] font-bold shrink-0"
            :class="isStepCompleted(idx) ? 'border-success text-success' : 'border-current'"
          >
            <Icon v-if="isStepCompleted(idx)" :icon="Check" :size="16" />
            <template v-else>{{ step.num }}</template>
          </span>
          <span class="text-[0.82rem]">{{ step.label }}</span>
        </RouterLink>
        <span v-if="idx < steps.length - 1" class="text-nav-border px-0.5 shrink-0" aria-hidden="true">
          <Icon :icon="ChevronRight" :size="16" />
        </span>
      </template>
    </nav>

    <div class="flex items-center gap-3 shrink-0" :class="!loggedIn && 'ml-auto'">
      <ThemeToggle />
      <template v-if="loggedIn">
        <span class="w-px self-stretch bg-nav-border" aria-hidden="true" />
        <nav aria-label="Secondary" class="flex items-center gap-3">
          <RouterLink :to="{ name: 'icd-schema' }" :class="secondaryLinkClass" active-class="!text-nav-text-strong">
            ICD Schema
          </RouterLink>
          <RouterLink :to="{ name: 'export-definitions' }" :class="secondaryLinkClass" active-class="!text-nav-text-strong">
            Export Definitions
          </RouterLink>
          <RouterLink :to="{ name: 'settings' }" :class="secondaryLinkClass" active-class="!text-nav-text-strong">
            Settings
          </RouterLink>
          <RouterLink :to="{ name: 'audit' }" :class="secondaryLinkClass" active-class="!text-nav-text-strong">
            Audit Log
          </RouterLink>
        </nav>
        <span class="w-px self-stretch bg-nav-border" aria-hidden="true" />
        <span class="text-[0.82rem] text-nav-text">{{ username }}</span>
        <button
          class="border border-nav-border text-nav-text rounded-md px-2.5 py-1 text-[0.78rem] bg-transparent cursor-pointer hover:bg-nav-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav"
          @click="logout"
        >
          Sign out
        </button>
      </template>
    </div>
  </header>

  <main class="p-6">
    <RouterView />
  </main>
</template>
