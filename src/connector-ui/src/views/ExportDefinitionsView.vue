<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { RouterLink } from 'vue-router'
import {
  listExportDefinitions,
  setExportDefinitionEnabled,
  duplicateExportDefinition,
  deleteExportDefinition,
  testExportDefinition,
  listExportDefinitionRuns,
  type ExportDefinitionSummary,
} from '@/api/exportDefinitions'
import StatusBadge from '@/components/StatusBadge.vue'
import Button from '@/components/ui/Button.vue'
import Alert from '@/components/ui/Alert.vue'
import PageHeader from '@/components/ui/PageHeader.vue'
import EmptyState from '@/components/ui/EmptyState.vue'
import ConfirmAction from '@/components/ui/ConfirmAction.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'
import { useToasts } from '@/composables/useToasts'

const toasts = useToasts()

const definitions = ref<ExportDefinitionSummary[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

// Latest run's status per definition id, fetched alongside the list — the "last run status" column
// export-definitions-2.0.md §7 calls for. Absent (undefined) means "not fetched/no runs yet",
// distinct from an empty array's "definitely no runs".
const lastRunStatus = ref<Record<number, string | null>>({})

async function load() {
  loading.value = true
  loadError.value = null
  try {
    definitions.value = await listExportDefinitions()
    await Promise.all(
      definitions.value.map(async (d) => {
        const runs = await listExportDefinitionRuns(d.id)
        lastRunStatus.value[d.id] = runs[0]?.status ?? null
      }),
    )
  } catch {
    loadError.value = 'Could not load export jobs. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

const togglingId = ref<number | null>(null)
async function toggleEnabled(def: ExportDefinitionSummary) {
  togglingId.value = def.id
  try {
    const result = await setExportDefinitionEnabled(def.id, !def.isEnabled)
    if (result.ok) {
      def.isEnabled = result.data.isEnabled
      toasts.success(def.isEnabled ? `${def.name} enabled.` : `${def.name} disabled.`)
    } else {
      toasts.error(result.error)
    }
  } finally {
    togglingId.value = null
  }
}

const scheduleLabel = (schedule: string | null) => schedule ?? 'Manual'

const testingId = ref<number | null>(null)
const testMessage = ref<Record<number, string>>({})
async function runTest(def: ExportDefinitionSummary) {
  testingId.value = def.id
  testMessage.value = { ...testMessage.value, [def.id]: '' }
  try {
    const result = await testExportDefinition(def.id)
    const message = result.ok
      ? `Test succeeded — ${result.data.recordCount} record(s).`
      : `Test failed: ${result.error}`
    testMessage.value = { ...testMessage.value, [def.id]: message }
    if (result.ok) toasts.success(message)
    else toasts.error(message)
  } finally {
    testingId.value = null
  }
}

const duplicatingId = ref<number | null>(null)
async function duplicate(def: ExportDefinitionSummary) {
  duplicatingId.value = def.id
  try {
    const result = await duplicateExportDefinition(def.id)
    if (result.ok) {
      toasts.success('Export definition duplicated.')
      await load()
    } else {
      toasts.error(result.error)
    }
  } finally {
    duplicatingId.value = null
  }
}

const confirmingDeleteId = ref<number | null>(null)
const deletingId = ref<number | null>(null)
async function confirmDelete(def: ExportDefinitionSummary) {
  deletingId.value = def.id
  try {
    if (await deleteExportDefinition(def.id)) {
      toasts.success('Export definition deleted.')
      await load()
    } else {
      toasts.error('Failed to delete export definition.')
    }
  } finally {
    deletingId.value = null
    confirmingDeleteId.value = null
  }
}
</script>

<template>
  <div class="max-w-5xl">
    <PageHeader title="Export Jobs" eyebrow="Exports · Independent Jobs">
      <template #help>
        <HelpTooltip label="About Export Jobs" title="What's an Export Job?">
          <p>
            A self-contained export you configure yourself: pick a table, choose which columns and
            related data to include, a file format, and an optional schedule — separate from the
            connector's one managed CMDB export.
          </p>
          <p>
            <strong>Example:</strong> a weekly CSV of your <code>purchaseorder</code> table for a
            finance team's reporting tool, running every Monday at 06:00 UTC, independent of anything
            else the connector does.
          </p>
          <p>
            Use <strong>Test</strong> in the row actions to run the query once against your live
            connection without saving anything, to check it actually works.
          </p>
        </HelpTooltip>
      </template>
      <template #actions>
        <RouterLink
          :to="{ name: 'export-definition-edit', params: { id: 'new' } }"
          class="px-4 py-1.5 border-0 rounded-md bg-brand text-white text-sm font-semibold no-underline hover:bg-brand-hover"
        >+ New</RouterLink>
        <Button variant="secondary" :loading="loading" @click="load">
          {{ loading ? 'Loading…' : 'Refresh' }}
        </Button>
      </template>
    </PageHeader>

    <p class="text-text-secondary text-sm mt-2 mb-5 leading-relaxed">
      Create additional independent exports with their own fields, structure, format and schedule —
      separate from the connector's <RouterLink :to="{ name: 'export-schema' }" class="text-brand hover:underline">managed CMDB export</RouterLink>.
    </p>

    <div v-if="loading && definitions.length === 0" class="text-text-secondary text-sm mt-4">Loading…</div>

    <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

    <EmptyState
      v-else-if="definitions.length === 0"
      title="No export jobs yet"
      description="Create one to start exporting records from a table on their own schedule."
      class="mt-4"
    />

    <table v-else class="w-full text-sm border-collapse">
      <thead>
        <tr class="text-left text-text-secondary border-b border-border">
          <th class="px-3 py-2 font-semibold">Name</th>
          <th class="px-3 py-2 font-semibold">Root table</th>
          <th class="px-3 py-2 font-semibold">Format</th>
          <th class="px-3 py-2 font-semibold">Enabled</th>
          <th class="px-3 py-2 font-semibold">Schedule</th>
          <th class="px-3 py-2 font-semibold">
            <span class="inline-flex items-center gap-1">
              Last run
              <HelpTooltip label="What do the run statuses mean?" title="Run statuses">
                <ul>
                  <li><strong>Success</strong> — the export ran and produced a file.</li>
                  <li><strong>Failed</strong> — the query or write failed; open the job and check Test.</li>
                  <li><strong>Skipped</strong> — deliberately not run for that cycle.</li>
                </ul>
                <p>Blank means this job has never run.</p>
              </HelpTooltip>
            </span>
          </th>
          <th class="px-3 py-2 font-semibold"></th>
        </tr>
      </thead>
      <tbody>
        <template v-for="def in definitions" :key="def.id">
          <tr class="border-b border-border hover:bg-surface-elevated">
            <td class="px-3 py-2 font-medium text-text-primary">{{ def.name }}</td>
            <td class="px-3 py-2 font-mono text-text-primary">{{ def.rootTable }}</td>
            <td class="px-3 py-2 text-text-secondary">{{ def.outputFormat }}</td>
            <td class="px-3 py-2">
              <input
                type="checkbox"
                :checked="def.isEnabled"
                :disabled="togglingId === def.id"
                class="cursor-pointer"
                :aria-label="`Enable ${def.name}`"
                @change="toggleEnabled(def)"
              />
            </td>
            <td class="px-3 py-2 font-mono text-xs text-text-secondary whitespace-nowrap">{{ scheduleLabel(def.schedule) }}</td>
            <td class="px-3 py-2">
              <StatusBadge v-if="lastRunStatus[def.id]" :status="lastRunStatus[def.id]!" />
              <span v-else class="text-text-muted text-xs">—</span>
            </td>
            <td class="px-3 py-2 text-right whitespace-nowrap">
              <div class="flex items-center gap-2.5 justify-end">
                <RouterLink
                  :to="{ name: 'export-definition-edit', params: { id: def.id } }"
                  class="text-brand text-sm hover:underline"
                >Edit</RouterLink>
                <button
                  type="button"
                  class="text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline disabled:opacity-50"
                  :disabled="testingId === def.id"
                  @click="runTest(def)"
                >{{ testingId === def.id ? 'Testing…' : 'Test' }}</button>
                <button
                  type="button"
                  class="text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline disabled:opacity-50"
                  :disabled="duplicatingId === def.id"
                  @click="duplicate(def)"
                >{{ duplicatingId === def.id ? 'Duplicating…' : 'Duplicate' }}</button>
                <ConfirmAction
                  variant="link"
                  :confirming="confirmingDeleteId === def.id"
                  :busy="deletingId === def.id"
                  :confirm-label="deletingId === def.id ? 'Deleting…' : 'Confirm'"
                  @update:confirming="(v) => (confirmingDeleteId = v ? def.id : null)"
                  @confirm="confirmDelete(def)"
                >
                  <template #trigger="{ open }">
                    <button
                      type="button"
                      class="text-danger text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
                      @click="open"
                    >Delete</button>
                  </template>
                </ConfirmAction>
              </div>
            </td>
          </tr>
          <tr v-if="testMessage[def.id]">
            <td colspan="7" class="px-3 pb-2 text-xs text-text-secondary">{{ testMessage[def.id] }}</td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>
</template>
