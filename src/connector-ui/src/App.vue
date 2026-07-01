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
  <header class="flex items-center justify-between gap-6 px-6 py-2.5 bg-slate-900 text-slate-200">
    <span class="font-bold text-sm tracking-wide text-white shrink-0">Connector</span>

    <nav v-if="loggedIn" class="flex items-center gap-1 flex-1">
      <template v-for="(step, idx) in steps" :key="step.name">
        <RouterLink
          :to="{ name: step.name }"
          class="flex items-center gap-1.5 px-3 py-1.5 rounded-md text-slate-400 no-underline transition-colors hover:bg-slate-800 hover:text-slate-200"
          active-class="!bg-slate-800 !text-slate-100"
        >
          <span class="flex items-center justify-center w-5 h-5 rounded-full border border-current text-[0.65rem] font-bold shrink-0">{{ step.num }}</span>
          <span class="text-[0.82rem]">{{ step.label }}</span>
        </RouterLink>
        <span v-if="idx < steps.length - 1" class="text-slate-600 text-xs px-0.5 shrink-0" aria-hidden="true">→</span>
      </template>
    </nav>

    <div v-if="loggedIn" class="flex items-center gap-3 shrink-0">
      <RouterLink
        :to="{ name: 'erp-database' }"
        class="text-[0.82rem] text-slate-400 no-underline hover:text-slate-200 transition-colors"
        active-class="!text-slate-100"
      >
        ERP Database
      </RouterLink>
      <RouterLink
        :to="{ name: 'icd-schema' }"
        class="text-[0.82rem] text-slate-400 no-underline hover:text-slate-200 transition-colors"
        active-class="!text-slate-100"
      >
        ICD Schema
      </RouterLink>
      <RouterLink
        :to="{ name: 'settings' }"
        class="text-[0.82rem] text-slate-400 no-underline hover:text-slate-200 transition-colors"
        active-class="!text-slate-100"
      >
        Settings
      </RouterLink>
      <span class="text-slate-600 text-xs" aria-hidden="true">|</span>
      <span class="text-[0.82rem] text-slate-400">{{ username }}</span>
      <button
        class="border border-slate-600 text-slate-300 rounded-md px-2.5 py-1 text-[0.78rem] bg-transparent cursor-pointer hover:bg-slate-800"
        @click="logout"
      >
        Sign out
      </button>
    </div>
  </header>

  <main class="p-6">
    <RouterView />
  </main>
</template>
