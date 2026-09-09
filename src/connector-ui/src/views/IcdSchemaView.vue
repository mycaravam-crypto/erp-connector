<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { getSchema, type SchemaDefinition } from '@/api/icdSchema'
import Alert from '@/components/ui/Alert.vue'
import IcdActiveColumnsTable from '@/components/IcdActiveColumnsTable.vue'
import IcdExcludedFieldsList from '@/components/IcdExcludedFieldsList.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

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
  <div class="max-w-5xl">
    <div class="flex items-center gap-3 mb-5">
      <h1 class="m-0 text-xl font-semibold text-text-primary">ICD Export Schema</h1>
      <HelpTooltip label="What is an ICD?" title="ICD = Interface Control Document">
        <p>
          It's the jointly-agreed contract that says exactly which columns cross the wire to the
          vendor, under what names — nothing more, nothing less. Think of it as a signed-off "menu"
          both sides agreed on, so neither side can quietly add or remove a field without the other
          noticing.
        </p>
        <p>
          This page is <strong>read-only</strong> — changing what's exported means going through the
          joint change process referenced above, not editing here.
        </p>
      </HelpTooltip>
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
          <IcdActiveColumnsTable :columns="schema.columns" />
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
        <IcdExcludedFieldsList :fields="pendingFields" />
      </section>

    </template>
  </div>
</template>
