<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { getSchema, type SchemaColumnDef, type SchemaDefinition } from '@/api/icdSchema'
import Alert from '@/components/ui/Alert.vue'

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
      <h1 class="m-0 text-xl font-semibold text-text-primary">ICD Export Schema</h1>
      <span
        v-if="schema"
        class="inline-flex items-center gap-1 bg-brand/10 text-brand text-xs font-bold px-2.5 py-1 rounded-full"
      >
        v{{ schema.version }}
      </span>
    </div>

    <p class="text-sm text-text-secondary mb-6">
      Read-only view of the agreed ICD export schema — the column contract between this connector
      and the vendor's Transform Map. Changes require a joint ICD change process.
    </p>

    <div v-if="loading" class="text-text-secondary text-sm">Loading schema…</div>

    <Alert v-else-if="error" variant="danger">{{ error }}</Alert>

    <template v-else-if="schema">

      <!-- Active columns table -->
      <section class="mb-8">
        <h2 class="text-base font-semibold text-text-primary mb-3">Active columns <span class="text-text-muted font-normal text-sm">({{ activeColumns.length }} of {{ schema.columns.length }})</span></h2>
        <div class="rounded-lg border border-border overflow-hidden">
          <table class="w-full text-sm">
            <thead>
              <tr class="bg-surface-elevated border-b border-border">
                <th class="text-left px-3 py-2 text-text-secondary font-medium w-36">Export column</th>
                <th class="text-left px-3 py-2 text-text-secondary font-medium">ERP source</th>
                <th class="text-left px-3 py-2 text-text-secondary font-medium w-32">Type / format</th>
                <th class="text-left px-3 py-2 text-text-secondary font-medium">Notes</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="col in schema.columns"
                :key="col.name"
                :class="col.active ? 'bg-surface' : 'bg-surface-elevated opacity-50'"
                class="border-b border-border last:border-0"
              >
                <td class="px-3 py-2 font-mono text-xs text-brand">
                  {{ col.exportName ?? col.name }}
                  <span v-if="!col.active" class="ml-1 text-[0.65rem] text-text-muted font-sans">(off)</span>
                </td>
                <td class="px-3 py-2 text-text-secondary font-mono text-xs">{{ col.erpSource }}</td>
                <td class="px-3 py-2 text-text-secondary text-xs">{{ col.type }}</td>
                <td class="px-3 py-2 text-text-secondary text-xs">{{ col.notes }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Coalesce key note -->
      <Alert variant="info" class="mb-8" title="Coalesce key: guid">
        The vendor's Transform Map coalesces on <code class="font-mono">guid</code>
        (<code class="font-mono">systemconfiguration.id</code> — PostgreSQL UUID, stable for the entity lifetime).
        On every daily import, an existing vendor record with this GUID is <em>updated</em>;
        a missing GUID causes a new record to be <em>created</em>.
        The serial number identifies the physical unit but is not the coalesce key — serial corrections
        update the existing record rather than creating duplicates.
      </Alert>

      <!-- Pending / excluded fields -->
      <section>
        <h2 class="text-base font-semibold text-text-primary mb-3">Excluded fields</h2>
        <p class="text-sm text-text-secondary mb-3">
          These ERP fields exist but are not in the current ICD allow-list and will never appear in any export artifact.
        </p>
        <div class="space-y-3">
          <div
            v-for="field in pendingFields"
            :key="field.erpSource"
            class="flex gap-3 items-start rounded-lg border px-4 py-3 text-sm"
            :class="field.tag === 'gdpr' ? 'border-danger/25 bg-danger-bg' : 'border-warning/25 bg-warning-bg'"
          >
            <span
              class="shrink-0 text-[0.68rem] font-bold px-1.5 py-0.5 rounded uppercase tracking-wide mt-0.5"
              :class="field.tag === 'gdpr' ? 'bg-danger text-white' : 'bg-warning text-white'"
            >
              {{ field.reason }}
            </span>
            <div>
              <code class="font-mono text-text-primary">{{ field.erpSource }}</code>
              <p class="mt-0.5 text-text-secondary">{{ field.detail }}</p>
            </div>
          </div>
        </div>
      </section>

    </template>
  </div>
</template>
