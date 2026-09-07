<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import Icon from '@/components/ui/Icon.vue'
import { ChevronRight, Check } from 'lucide-vue-next'

// The top-level "Connector" nav — pulled out of App.vue to keep its template's branch count (setup
// steps + completion state + operational pills, each with its own active/completed class logic) from
// growing App.vue's own template complexity past the fallow health gate.
const route = useRoute()

// One-time connector setup — a short, ordered sequence the user completes once. No step numbers:
// numbering suggested a wizard the user finishes and leaves, but the operations links below are an
// ongoing area, not a step in the same sequence.
const setupSteps = [
  { name: 'connect', label: 'Connect' },
  { name: 'source-schema', label: 'Source Schema' },
  { name: 'export-schema', label: 'CMDB Export Mapping' },
]

// Operational areas — reachable once setup is done, not part of the setup sequence itself. Each is a
// separate saved-jobs area (its own schedule, its own runs), so each gets its own top-level pill rather
// than nesting Export Jobs/Import Definitions inside the account menu where they were easy to miss.
const operationsLinks = [
  { to: { name: 'exports' }, label: 'Managed Export', activeNames: new Set(['exports', 'export-detail']) },
  { to: { name: 'export-definitions' }, label: 'Export Jobs', activeNames: new Set(['export-definitions', 'export-definition-edit']) },
  { to: { name: 'import-definitions' }, label: 'Import Definitions', activeNames: new Set(['import-definitions', 'import-definition-edit']) },
]
const OPERATIONS_ROUTES = new Set(operationsLinks.flatMap((l) => [...l.activeNames]))
const operationsActive = computed(() => OPERATIONS_ROUTES.has(String(route.name)))

// The current setup step, if the active route is one of them.
// On a secondary page (Settings, Audit Log, ...) or an operational area this is -1, so no step shows
// as active — we don't know (or it doesn't apply) where in setup the user left off.
const currentStepIndex = computed(() =>
  setupSteps.findIndex((s) => route.path === `/${s.name}` || route.path.startsWith(`/${s.name}/`)),
)

const navLinkClass =
  'flex items-center gap-1.5 px-3 py-1.5 rounded-md no-underline transition-colors duration-fast ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav'

function isStepCompleted(idx: number): boolean {
  // Once the connector is operating (any operations link active), setup is implicitly done.
  if (operationsActive.value) return true
  return idx < currentStepIndex.value
}

function stepLinkClass(idx: number): string {
  return idx === currentStepIndex.value
    ? `${navLinkClass} bg-nav-hover text-nav-text-strong`
    : `${navLinkClass} text-nav-text hover:bg-nav-hover hover:text-nav-text-strong`
}

function operationsLinkClass(link: (typeof operationsLinks)[number]): string {
  return link.activeNames.has(String(route.name))
    ? `${navLinkClass} bg-nav-hover text-nav-text-strong`
    : `${navLinkClass} text-nav-text hover:bg-nav-hover hover:text-nav-text-strong`
}
</script>

<template>
  <nav aria-label="Connector" class="flex items-center gap-1 flex-1">
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

    <RouterLink v-for="link in operationsLinks" :key="link.label" :to="link.to" :class="operationsLinkClass(link)">
      <span class="w-1.5 h-1.5 rounded-full bg-current opacity-70 shrink-0" aria-hidden="true" />
      <span class="text-[0.82rem]">{{ link.label }}</span>
    </RouterLink>
  </nav>
</template>
