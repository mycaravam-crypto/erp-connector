<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { getSchema, type SchemaColumnDef, type SchemaDefinition } from '@/api/icdSchema'

const schema = ref<SchemaDefinition | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

const activeColumns = computed(() => schema.value?.columns.filter((c) => c.active) ?? [])

// These fields are present in the ERP but permanently excluded from the export.
const pendingFields = [
  {
    erpSource: 'storagelocation.location_id',
    reason: 'Open Point #4',
    detail: 'Entitlement not yet confirmed — excluded until legal + data owner sign off.',
    tag: 'open-point',
  },
  {
    erpSource: 'systemconfiguration.technician_name',
    reason: 'GDPR Art. 5(1)(c)',
    detail: 'Personal data — permanently excluded. Pseudonymous token is an Iteration 3+ candidate.',
    tag: 'gdpr',
  },
]

onMounted(async () => {
  try {
    schema.value = await getSchema()
    if (!schema.value) error.value = 'No schema data returned from API.'
  } catch {
    error.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="max-w-3xl">
    <div class="flex items-center gap-3 mb-5">
      <h1 class="m-0 text-xl font-semibold">ICD Export Schema</h1>
      <span
        v-if="schema"
        class="inline-flex items-center gap-1 bg-indigo-100 text-indigo-700 text-xs font-bold px-2.5 py-1 rounded-full"
      >
        v{{ schema.version }}
      </span>
    </div>

    <p class="text-sm text-slate-500 mb-6">
      Read-only view of the agreed ICD export schema — the column contract between this connector
      and the vendor's Transform Map. Changes require a joint ICD change process.
    </p>

    <div v-if="loading" class="text-slate-500 text-sm">Loading schema…</div>

    <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">
      {{ error }}
    </div>

    <template v-else-if="schema">

      <!-- Active columns table -->
      <section class="mb-8">
        <h2 class="text-base font-semibold mb-3">Active columns <span class="text-slate-400 font-normal text-sm">({{ activeColumns.length }} of {{ schema.columns.length }})</span></h2>
        <div class="rounded-lg border border-slate-200 overflow-hidden">
          <table class="w-full text-sm">
            <thead>
              <tr class="bg-slate-50 border-b border-slate-200">
                <th class="text-left px-3 py-2 text-slate-600 font-medium w-36">Export column</th>
                <th class="text-left px-3 py-2 text-slate-600 font-medium">ERP source</th>
                <th class="text-left px-3 py-2 text-slate-600 font-medium w-32">Type / format</th>
                <th class="text-left px-3 py-2 text-slate-600 font-medium">Notes</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="col in schema.columns"
                :key="col.name"
                :class="col.active ? 'bg-white' : 'bg-slate-50 opacity-50'"
                class="border-b border-slate-100 last:border-0"
              >
                <td class="px-3 py-2 font-mono text-xs text-indigo-700">
                  {{ col.exportName ?? col.name }}
                  <span v-if="!col.active" class="ml-1 text-[0.65rem] text-slate-400 font-sans">(off)</span>
                </td>
                <td class="px-3 py-2 text-slate-500 font-mono text-xs">{{ col.erpSource }}</td>
                <td class="px-3 py-2 text-slate-600 text-xs">{{ col.type }}</td>
                <td class="px-3 py-2 text-slate-600 text-xs">{{ col.notes }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Coalesce key note -->
      <section class="mb-8 bg-blue-50 border border-blue-200 rounded-lg px-4 py-3 text-sm">
        <p class="font-semibold text-blue-800 mb-1">Coalesce key: <code class="font-mono">guid</code></p>
        <p class="text-blue-700">
          The vendor's Transform Map coalesces on <code class="font-mono">guid</code>
          (<code class="font-mono">systemconfiguration.id</code> — PostgreSQL UUID, stable for the entity lifetime).
          On every daily import, an existing vendor record with this GUID is <em>updated</em>;
          a missing GUID causes a new record to be <em>created</em>.
          The serial number identifies the physical unit but is not the coalesce key — serial corrections
          update the existing record rather than creating duplicates.
        </p>
      </section>

      <!-- Pending / excluded fields -->
      <section>
        <h2 class="text-base font-semibold mb-3">Excluded fields</h2>
        <p class="text-sm text-slate-500 mb-3">
          These ERP fields exist but are not in the current ICD allow-list and will never appear in any export artifact.
        </p>
        <div class="space-y-3">
          <div
            v-for="field in pendingFields"
            :key="field.erpSource"
            class="flex gap-3 items-start rounded-lg border px-4 py-3 text-sm"
            :class="field.tag === 'gdpr' ? 'border-red-200 bg-red-50' : 'border-amber-200 bg-amber-50'"
          >
            <span
              class="shrink-0 text-[0.68rem] font-bold px-1.5 py-0.5 rounded uppercase tracking-wide mt-0.5"
              :class="field.tag === 'gdpr' ? 'bg-red-200 text-red-800' : 'bg-amber-200 text-amber-800'"
            >
              {{ field.reason }}
            </span>
            <div>
              <code class="font-mono text-slate-700">{{ field.erpSource }}</code>
              <p class="mt-0.5 text-slate-600">{{ field.detail }}</p>
            </div>
          </div>
        </div>
      </section>

    </template>
  </div>
</template>
