<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import { getConnection, type ErpConnectionInfo } from '@/api/connection'
import { listExportDefinitions } from '@/api/exportDefinitions'
import { listImportDefinitions } from '@/api/importDefinitions'
import { Plug, ArrowRight } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Card from '@/components/ui/Card.vue'
import PageHeader from '@/components/ui/PageHeader.vue'

// The landing page once a connection is configured (router guard sends first-run visitors to /connect
// instead) — an orientation point for the areas that used to be reachable only from the account menu,
// not a replacement for any of them.
const connection = ref<ErpConnectionInfo | null>(null)
const exportJobCount = ref<number | null>(null)
const enabledExportJobCount = ref<number | null>(null)
const importDefinitionCount = ref<number | null>(null)
const enabledImportDefinitionCount = ref<number | null>(null)

onMounted(async () => {
  const [conn, exportDefs, importDefs] = await Promise.all([
    getConnection(),
    listExportDefinitions().catch(() => []),
    listImportDefinitions().catch(() => []),
  ])
  connection.value = conn
  exportJobCount.value = exportDefs.length
  enabledExportJobCount.value = exportDefs.filter((d) => d.isEnabled).length
  importDefinitionCount.value = importDefs.length
  enabledImportDefinitionCount.value = importDefs.filter((d) => d.isEnabled).length
})

const links = [
  { to: { name: 'export-schema' }, title: 'CMDB Export Mapping', description: 'The managed export field mapping to the CMDB.' },
  { to: { name: 'exports' }, title: 'Managed Export', description: 'Run and review the managed CMDB export.' },
  { to: { name: 'export-definitions' }, title: 'Export Jobs', description: 'Independent export jobs with their own schedule.' },
  { to: { name: 'import-definitions' }, title: 'Import Jobs', description: 'Independent inbound data imports.' },
  { to: { name: 'source-schema' }, title: 'Source Schema', description: 'Browse the connected ERP database schema.' },
  { to: { name: 'icd-schema' }, title: 'ICD Schema', description: 'The interface control document field list.' },
] as const
</script>

<template>
  <div class="max-w-5xl">
    <PageHeader title="Dashboard" />

    <Card class="mb-5">
      <div class="flex items-center gap-3">
        <span class="text-brand shrink-0"><Icon :icon="Plug" :size="24" /></span>
        <div class="min-w-0">
          <p class="m-0 text-sm font-semibold text-text-primary">
            {{ connection ? `Connected to ${connection.database}` : 'No connection configured' }}
          </p>
          <p v-if="connection" class="m-0 text-xs text-text-secondary font-mono truncate">
            {{ connection.username }}@{{ connection.host }}:{{ connection.port }}
          </p>
        </div>
        <RouterLink :to="{ name: 'connect' }" class="ml-auto text-brand text-sm shrink-0 hover:underline">
          {{ connection ? 'Edit' : 'Connect' }}
        </RouterLink>
      </div>
    </Card>

    <div class="grid grid-cols-2 gap-3 mb-5">
      <Card>
        <p class="m-0 text-xs font-semibold uppercase tracking-wide text-text-secondary mb-1">Export Jobs</p>
        <p class="m-0 text-2xl font-semibold text-text-primary">
          {{ enabledExportJobCount ?? '—' }}<span v-if="exportJobCount !== null" class="text-base font-normal text-text-muted"> / {{ exportJobCount }} enabled</span>
        </p>
      </Card>
      <Card>
        <p class="m-0 text-xs font-semibold uppercase tracking-wide text-text-secondary mb-1">Import Jobs</p>
        <p class="m-0 text-2xl font-semibold text-text-primary">
          {{ enabledImportDefinitionCount ?? '—' }}<span v-if="importDefinitionCount !== null" class="text-base font-normal text-text-muted"> / {{ importDefinitionCount }} enabled</span>
        </p>
      </Card>
    </div>

    <p class="text-text-secondary text-xs font-semibold uppercase tracking-wide m-0 mb-2">Go to</p>
    <div class="grid grid-cols-2 gap-3">
      <RouterLink
        v-for="link in links"
        :key="link.title"
        :to="link.to"
        class="flex items-center gap-3 rounded-lg border border-border bg-surface px-4 py-3 no-underline hover:bg-surface-elevated"
      >
        <div class="min-w-0 flex-1">
          <p class="m-0 text-sm font-semibold text-text-primary">{{ link.title }}</p>
          <p class="m-0 text-xs text-text-secondary">{{ link.description }}</p>
        </div>
        <Icon :icon="ArrowRight" :size="16" class="text-text-muted shrink-0" />
      </RouterLink>
    </div>
  </div>
</template>
