<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { RouterLink, useRouter } from 'vue-router'
import { listExportDefinitions } from '@/api/exportDefinitions'
import { listImportDefinitions } from '@/api/importDefinitions'
import { ArrowRight } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Card from '@/components/ui/Card.vue'
import PageHeader from '@/components/ui/PageHeader.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import ConnectionStatusCard from '@/components/ConnectionStatusCard.vue'
import { canOpen, useCurrentUser } from '@/composables/useCurrentUser'
import { Permission } from '@/lib/permissions'

// The landing page once a connection is configured (router guard sends first-run visitors to /connect
// instead) — an orientation point for the areas that used to be reachable only from the account menu,
// not a replacement for any of them. Counts and tiles appear only for areas the user may open.
const router = useRouter()
const { can } = useCurrentUser()
const exportJobCount = ref<number | null>(null)
const enabledExportJobCount = ref<number | null>(null)
const importDefinitionCount = ref<number | null>(null)
const enabledImportDefinitionCount = ref<number | null>(null)

onMounted(async () => {
  const [exportDefs, importDefs] = await Promise.all([
    showExportJobs ? listExportDefinitions().catch(() => []) : null,
    showImportJobs ? listImportDefinitions().catch(() => []) : null,
  ])
  if (exportDefs) {
    exportJobCount.value = exportDefs.length
    enabledExportJobCount.value = exportDefs.filter((d) => d.isEnabled).length
  }
  if (importDefs) {
    importDefinitionCount.value = importDefs.length
    enabledImportDefinitionCount.value = importDefs.filter((d) => d.isEnabled).length
  }
})

const allLinks = [
  { to: { name: 'export-schema' }, title: 'CMDB Export Mapping', description: 'The managed export field mapping to the CMDB.' },
  { to: { name: 'exports' }, title: 'Managed Export', description: 'Run and review the managed CMDB export.' },
  { to: { name: 'export-definitions' }, title: 'Export Jobs', description: 'Independent export jobs with their own schedule.' },
  { to: { name: 'import-definitions' }, title: 'Import Jobs', description: 'Independent inbound data imports.' },
  { to: { name: 'source-schema' }, title: 'Source Schema', description: 'Browse the connected ERP database schema.' },
  { to: { name: 'icd-schema' }, title: 'ICD Schema', description: 'The interface control document field list.' },
] as const
const links = computed(() => allLinks.filter((link) => canOpen(router.resolve(link.to))))
const showExportJobs = can(Permission.ExportJobsView)
const showImportJobs = can(Permission.ImportJobsView)
const showJobCounts = showExportJobs || showImportJobs
</script>

<template>
  <PageHeader title="Dashboard">
    <template #help>
      <HelpTooltip label="About this dashboard" title="What is this page?">
        <p>
          This is the connector's home base — an overview of your database connection and a
          shortcut to every other area, so you don't have to remember which menu something lives
          under.
        </p>
        <p>
          <strong>Example:</strong> if the "Export Jobs" tile shows <code>2 / 3 enabled</code>,
          it means 3 export jobs exist but only 2 are currently scheduled to run automatically.
        </p>
      </HelpTooltip>
    </template>
  </PageHeader>

  <ConnectionStatusCard class="mb-5" />

  <div v-if="showJobCounts" class="grid grid-cols-2 gap-3 mb-5">
    <Card v-if="showExportJobs">
      <p class="m-0 text-xs font-semibold uppercase tracking-wide text-text-secondary mb-1">Export Jobs</p>
      <p class="m-0 text-2xl font-semibold text-text-primary">
        {{ enabledExportJobCount ?? '—' }}<span v-if="exportJobCount !== null" class="text-base font-normal text-text-muted"> / {{ exportJobCount }} enabled</span>
      </p>
    </Card>
    <Card v-if="showImportJobs">
      <p class="m-0 text-xs font-semibold uppercase tracking-wide text-text-secondary mb-1">Import Jobs</p>
      <p class="m-0 text-2xl font-semibold text-text-primary">
        {{ enabledImportDefinitionCount ?? '—' }}<span v-if="importDefinitionCount !== null" class="text-base font-normal text-text-muted"> / {{ importDefinitionCount }} enabled</span>
      </p>
    </Card>
  </div>

  <p v-if="links.length > 0" class="text-text-secondary text-xs font-semibold uppercase tracking-wide m-0 mb-2 flex items-center gap-1.5">
    Go to
    <HelpTooltip label="What's the difference between these?" title="Managed export vs. Export Jobs — which do I want?">
      <p>
        <strong>CMDB Export Mapping</strong> and <strong>Managed Export</strong> are one linked pair: the
        mapping is the field configuration, Managed Export is where you run it and get a signed-off
        ("four-eyes") release for the vendor. There's only ever one of these.
      </p>
      <p>
        <strong>Export Jobs</strong> are separate, independent exports you create yourself — each with
        its own table, fields, format and schedule. Use these for anything beyond the one managed
        CMDB feed, e.g. a nightly export of a different table for another system.
      </p>
    </HelpTooltip>
  </p>
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
</template>
