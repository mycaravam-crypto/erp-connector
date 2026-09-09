<script setup lang="ts">
import { useRoute } from 'vue-router'

const route = useRoute()

// One flat list, not a "setup wizard you finish then leave" — the old version drew Connect/Source
// Schema/CMDB Export Mapping as numbered steps with checkmarks (implying a one-time onboarding
// sequence) and everything else as plain pills, but in practice people jump back into any of these
// areas at any time, so the completion-state framing was never actually true. The divider below
// groups the built-in CMDB pipeline (config → run) apart from Export/Import Jobs, which are
// independent, user-created jobs — a real structural distinction, unlike the old step/non-step split.
const navLinks = [
  { to: { name: 'connect' }, label: 'Connect', activeNames: new Set(['connect']) },
  { to: { name: 'source-schema' }, label: 'Source Schema', activeNames: new Set(['source-schema']) },
  { to: { name: 'export-schema' }, label: 'CMDB Export Mapping', activeNames: new Set(['export-schema']) },
  { to: { name: 'exports' }, label: 'Managed Export', activeNames: new Set(['exports', 'export-detail']) },
  { to: { name: 'export-definitions' }, label: 'Export Jobs', activeNames: new Set(['export-definitions', 'export-definition-edit']) },
  { to: { name: 'import-definitions' }, label: 'Import Jobs', activeNames: new Set(['import-definitions', 'import-definition-edit']) },
] as const

const navLinkClass =
  'flex items-center gap-1.5 px-3 py-1.5 rounded-md no-underline whitespace-nowrap shrink-0 transition-colors duration-fast ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav'

function linkClass(link: (typeof navLinks)[number]): string {
  return link.activeNames.has(String(route.name))
    ? `${navLinkClass} bg-nav-hover text-nav-text-strong`
    : `${navLinkClass} text-nav-text hover:bg-nav-hover hover:text-nav-text-strong`
}
</script>

<template>
  <nav aria-label="Connector" class="flex flex-wrap items-center gap-x-1 gap-y-1.5 flex-1 min-w-0">
    <template v-for="(link, idx) in navLinks" :key="link.label">
      <RouterLink :to="link.to" :class="linkClass(link)">
        <span class="w-1.5 h-1.5 rounded-full bg-current opacity-70 shrink-0" aria-hidden="true" />
        <span class="text-[0.82rem]">{{ link.label }}</span>
      </RouterLink>
      <span v-if="idx === 3" class="w-px h-5 mx-1.5 bg-nav-border shrink-0" aria-hidden="true" />
    </template>
  </nav>
</template>
