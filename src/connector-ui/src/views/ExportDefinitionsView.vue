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
    loadError.value = 'Could not load export definitions. Is the backend service running?'
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
    if (result.ok) def.isEnabled = result.data.isEnabled
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
    testMessage.value = {
      ...testMessage.value,
      [def.id]: result.ok
        ? `Test succeeded — ${result.data.recordCount} record(s).`
        : `Test failed: ${result.error}`,
    }
  } finally {
    testingId.value = null
  }
}

const duplicatingId = ref<number | null>(null)
async function duplicate(def: ExportDefinitionSummary) {
  duplicatingId.value = def.id
  try {
    const result = await duplicateExportDefinition(def.id)
    if (result.ok) await load()
  } finally {
    duplicatingId.value = null
  }
}

const confirmingDeleteId = ref<number | null>(null)
const deletingId = ref<number | null>(null)
async function confirmDelete(def: ExportDefinitionSummary) {
  deletingId.value = def.id
  try {
    if (await deleteExportDefinition(def.id)) await load()
  } finally {
    deletingId.value = null
    confirmingDeleteId.value = null
  }
}
</script>

<template>
  <div class="max-w-5xl">
    <div class="flex items-center justify-between gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold text-text-primary">Export Definitions</h1>
      <div class="flex items-center gap-2">
        <RouterLink
          :to="{ name: 'export-definition-edit', params: { id: 'new' } }"
          class="px-4 py-1.5 border-0 rounded-md bg-brand text-white text-sm font-semibold no-underline hover:bg-brand-hover"
        >+ New</RouterLink>
        <Button variant="secondary" :loading="loading" @click="load">
          {{ loading ? 'Loading…' : 'Refresh' }}
        </Button>
      </div>
    </div>

    <p class="text-text-secondary text-sm mt-2 mb-5 leading-relaxed">
      Saved, independently triggerable export configs. Definitions here were migrated automatically
      from the legacy mapping screen the first time it had a config to convert — edit one to point it
      at the right tables/columns for your actual source database.
    </p>

    <div v-if="loading && definitions.length === 0" class="text-text-secondary text-sm mt-4">Loading…</div>

    <Alert v-else-if="loadError" variant="danger" class="mt-4">{{ loadError }}</Alert>

    <div v-else-if="definitions.length === 0" class="text-text-secondary text-sm mt-4">
      No export definitions yet.
    </div>

    <table v-else class="w-full text-sm border-collapse">
      <thead>
        <tr class="text-left text-text-secondary border-b border-border">
          <th class="px-3 py-2 font-semibold">Name</th>
          <th class="px-3 py-2 font-semibold">Root table</th>
          <th class="px-3 py-2 font-semibold">Format</th>
          <th class="px-3 py-2 font-semibold">Enabled</th>
          <th class="px-3 py-2 font-semibold">Schedule</th>
          <th class="px-3 py-2 font-semibold">Last run</th>
          <th class="px-3 py-2 font-semibold"></th>
        </tr>
      </thead>
      <tbody>
        <template v-for="def in definitions" :key="def.id">
          <tr class="border-b border-slate-100 hover:bg-slate-50">
            <td class="px-3 py-2 font-medium text-slate-800">{{ def.name }}</td>
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
                <template v-if="confirmingDeleteId === def.id">
                  <button
                    type="button"
                    class="text-danger text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline disabled:opacity-50"
                    :disabled="deletingId === def.id"
                    @click="confirmDelete(def)"
                  >Confirm</button>
                  <button
                    type="button"
                    class="text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
                    @click="confirmingDeleteId = null"
                  >Cancel</button>
                </template>
                <button
                  v-else
                  type="button"
                  class="text-danger text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
                  @click="confirmingDeleteId = def.id"
                >Delete</button>
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
