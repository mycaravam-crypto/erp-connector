<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { ChevronDown, LogOut } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

defineProps<{ username: string | null }>()
const emit = defineEmits<{ signOut: [] }>()

const route = useRoute()
const open = ref(false)
const rootEl = ref<HTMLElement | null>(null)

const linkGroups = [
  {
    label: 'Connector',
    links: [
      { name: 'icd-schema', label: 'ICD Schema' },
      { name: 'settings', label: 'Settings' },
      { name: 'audit', label: 'Audit Log' },
    ],
  },
  {
    label: 'Exports',
    links: [
      { name: 'export-definitions', label: 'Export Jobs' },
      { name: 'import-definitions', label: 'Import Definitions' },
    ],
  },
]

const groupLabelClass = 'px-3 pt-2 pb-1 text-[0.7rem] font-semibold uppercase tracking-wide text-nav-text/60'

const menuLinkClass =
  'flex items-center px-3 py-2 text-[0.82rem] text-nav-text no-underline hover:bg-nav-hover hover:text-nav-text-strong transition-colors ' +
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav'

function toggle() {
  open.value = !open.value
}

function close() {
  open.value = false
}

function onDocClick(e: MouseEvent) {
  if (open.value && rootEl.value && !rootEl.value.contains(e.target as Node)) close()
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

function signOut() {
  close()
  emit('signOut')
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
  <div ref="rootEl" class="relative shrink-0">
    <button
      type="button"
      aria-haspopup="menu"
      :aria-expanded="open"
      class="flex items-center gap-1.5 px-2.5 py-1.5 rounded-md text-[0.82rem] text-nav-text bg-transparent border-none cursor-pointer hover:bg-nav-hover hover:text-nav-text-strong transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus focus-visible:ring-offset-2 focus-visible:ring-offset-nav"
      @click="toggle"
    >
      {{ username }}
      <Icon :icon="ChevronDown" :size="16" />
    </button>

    <div
      v-if="open"
      class="absolute right-0 top-full mt-1.5 min-w-[11rem] py-1.5 rounded-md border border-nav-border bg-nav shadow-lg z-10"
    >
      <nav aria-label="Secondary" class="flex flex-col">
        <template v-for="(group, idx) in linkGroups" :key="group.label">
          <span v-if="idx > 0" class="block h-px my-1.5 bg-nav-border" aria-hidden="true" />
          <span :class="groupLabelClass">{{ group.label }}</span>
          <RouterLink
            v-for="link in group.links"
            :key="link.name"
            :to="{ name: link.name }"
            :class="menuLinkClass"
            active-class="!text-nav-text-strong !bg-nav-hover"
          >
            {{ link.label }}
          </RouterLink>
        </template>
      </nav>
      <span class="block h-px my-1.5 bg-nav-border" aria-hidden="true" />
      <button
        type="button"
        :class="menuLinkClass"
        class="w-full text-left bg-transparent border-none cursor-pointer"
        @click="signOut"
      >
        <Icon :icon="LogOut" :size="16" class="mr-1.5" />
        Sign out
      </button>
    </div>
  </div>
</template>
