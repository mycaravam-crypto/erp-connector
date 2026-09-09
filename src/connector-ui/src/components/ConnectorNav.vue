<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute } from 'vue-router'
import { Menu, X } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

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

// Below the `nav` breakpoint (see base.css), the pill row has nowhere to go — it collapses into this toggle + dropdown panel
// instead, mirroring UserMenu's click-outside/Escape/route-change-close behavior so the two
// header dropdowns behave the same way.
const open = ref(false)
const rootEl = ref<HTMLElement | null>(null)

function toggle() {
  open.value = !open.value
}

function close() {
  open.value = false
}

// composedPath(), not .contains(e.target) — the toggle button's icon swaps components (Menu ↔ X)
// on the very click that opens the menu, which can replace that DOM node before this document-level
// bubble-phase listener runs. composedPath() is captured at dispatch time, so it stays correct even
// though e.target itself may already be detached by the time we check it here.
function onDocClick(e: MouseEvent) {
  if (open.value && rootEl.value && !e.composedPath().includes(rootEl.value)) close()
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

watch(() => route.path, close)

onMounted(() => {
  document.addEventListener('click', onDocClick)
  document.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocClick)
  document.removeEventListener('keydown', onKeydown)
})
</script>

<template>
  <!-- No `relative` here on purpose: the mobile dropdown panel below needs `left-0 right-0` to span
       the full header width, and this wrapper (a flex-1 item sized to its own narrow content — just
       the toggle button, on mobile) is too narrow to anchor that. Leaving this `static` lets the
       panel's containing block resolve to the header instead, which is already `position: sticky`
       and spans the full viewport width. -->
  <div ref="rootEl" class="flex-1 min-w-0">
    <nav aria-label="Connector" class="hidden nav:flex flex-wrap items-center gap-x-1 gap-y-1.5 min-w-0">
      <template v-for="(link, idx) in navLinks" :key="link.label">
        <RouterLink :to="link.to" :class="linkClass(link)">
          <span class="w-1.5 h-1.5 rounded-full bg-current opacity-70 shrink-0" aria-hidden="true" />
          <span class="text-[0.82rem]">{{ link.label }}</span>
        </RouterLink>
        <span v-if="idx === 3" class="w-px h-5 mx-1.5 bg-nav-border shrink-0" aria-hidden="true" />
      </template>
    </nav>

    <button
      type="button"
      aria-haspopup="menu"
      :aria-expanded="open"
      aria-label="Menu"
      class="nav:hidden flex items-center justify-center w-9 h-9 rounded-md text-nav-text bg-transparent border-none cursor-pointer hover:bg-nav-hover hover:text-nav-text-strong transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav"
      @click="toggle"
    >
      <Icon :icon="open ? X : Menu" :size="20" />
    </button>

    <nav
      v-if="open"
      aria-label="Connector"
      class="nav:hidden absolute left-0 right-0 top-full mt-1.5 flex flex-col gap-0.5 p-1.5 rounded-md border border-nav-border bg-nav shadow-lg z-10"
    >
      <template v-for="(link, idx) in navLinks" :key="link.label">
        <RouterLink :to="link.to" :class="linkClass(link)">
          <span class="w-1.5 h-1.5 rounded-full bg-current opacity-70 shrink-0" aria-hidden="true" />
          <span class="text-[0.82rem]">{{ link.label }}</span>
        </RouterLink>
        <span v-if="idx === 3" class="h-px my-1 bg-nav-border" aria-hidden="true" />
      </template>
    </nav>
  </div>
</template>
