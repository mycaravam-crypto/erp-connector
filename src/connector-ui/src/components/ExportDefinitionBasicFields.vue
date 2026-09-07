<script setup lang="ts">
import { computed } from 'vue'
import type { SourceTable } from '@/api/connection'
import type { ExportDefinition } from '@/api/exportDefinitions'
import ExportFormatPicker from '@/components/ExportFormatPicker.vue'
import ExportScheduleField from '@/components/ExportScheduleField.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

const props = defineProps<{
  definition: ExportDefinition
  availableTables: SourceTable[]
  /** Disables the root-table picker once children reference it — changing tables out from under an
   * already-built tree would silently invalidate every SourceField/RelatedTable in it. */
  rootTableLocked: boolean
}>()
const emit = defineEmits<{ 'root-table-changed': [] }>()

function onRootTableChanged() {
  if (!props.rootTableLocked) emit('root-table-changed')
}

// v-model.number leaves a cleared number input as '' rather than null, which the backend's nullable int
// rejects outright as a malformed body — coerce it explicitly instead of relying on the modifier.
const contractVersionInput = computed<number | null>({
  get: () => props.definition.contractVersion,
  set: (v) => {
    props.definition.contractVersion = v === null || Number.isNaN(v) ? null : v
  },
})

// Plain v-model on a `string | null` field leaves a cleared input as '' rather than null — harmless for
// free-text fields like Description, but IntegrationKey/ContractVersion must be null *together* (see the
// save-time validator), so clearing one text field back to '' must mean "unset," not "set to empty string."
function nullableTextInput(key: 'integrationKey' | 'correlationKeySourceField') {
  return computed<string | null>({
    get: () => props.definition[key],
    set: (v) => {
      props.definition[key] = v === '' ? null : v
    },
  })
}
const integrationKeyInput = nullableTextInput('integrationKey')
const correlationKeySourceFieldInput = nullableTextInput('correlationKeySourceField')
</script>

<template>
  <h2 class="text-base font-semibold text-text-primary mb-2.5">General</h2>
  <div class="flex flex-col gap-3 mb-5">
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-28 shrink-0">Name</label>
      <input
        type="text"
        v-model="definition.name"
        aria-label="Name"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary bg-surface outline-none focus:border-brand"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-28 shrink-0">Description</label>
      <input
        type="text"
        v-model="definition.description"
        placeholder="optional"
        aria-label="Description"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary bg-surface outline-none focus:border-brand"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-28 shrink-0 inline-flex items-center gap-1">
        Root table
        <HelpTooltip label="What's a root table?" title="Root table">
          <p>
            The main table this export starts from — one row here becomes one record in the output
            file. Every field and related entity you add below is reached from this table.
          </p>
          <p>
            <strong>Example:</strong> pick <code>purchaseorder</code> to export one row per purchase
            order; add a related entity for <code>purchaseorderline</code> to nest each order's line
            items inside it.
          </p>
          <p>Once you've added fields, this locks — clear them first to switch tables.</p>
        </HelpTooltip>
      </label>
      <select
        v-if="availableTables.length > 0"
        v-model="definition.rootTable"
        :disabled="rootTableLocked"
        aria-label="Root table"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono bg-surface disabled:bg-surface-elevated disabled:text-text-muted"
        @change="onRootTableChanged"
      >
        <option value="" disabled>— select —</option>
        <option v-for="t in availableTables" :key="t.name" :value="t.name">{{ t.name }}</option>
      </select>
      <input
        v-else
        type="text"
        v-model="definition.rootTable"
        placeholder="e.g. systemconfiguration"
        aria-label="Root table"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono bg-surface outline-none focus:border-brand"
      />
      <span v-if="rootTableLocked" class="text-xs text-text-muted">clear all fields to change</span>
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-28 shrink-0">Enabled</label>
      <input type="checkbox" v-model="definition.isEnabled" class="cursor-pointer" />
      <span class="text-xs text-text-secondary">Scheduled runs only fire for enabled definitions.</span>
    </div>
  </div>

  <h2 class="text-base font-semibold text-text-primary mb-2.5">Schedule</h2>
  <div class="mb-5">
    <ExportScheduleField v-model="definition.schedule" />
  </div>

  <h2 class="text-base font-semibold text-text-primary mb-2.5">Output</h2>
  <ExportFormatPicker v-model="definition.outputFormat as 'xlsx' | 'csv' | 'json'" />

  <!-- knowledge/pipeline/import-mapping-presets.md §3.1/§3.2 — optional provenance tagging. Setting
       IntegrationKey is what makes JsonExportFormatWriter emit a provenance block an ImportDefinition can
       later be suggested from (§3.4); until it's set, this export is invisible to that feature entirely. -->
  <div class="flex flex-col gap-3 mt-5 pt-5 border-t border-border-strong">
    <span class="inline-flex items-center gap-1.5">
      <h3 class="m-0 text-sm font-semibold text-text-primary">Integration tagging (optional)</h3>
      <HelpTooltip label="What is integration tagging for?" title="Pairing an export with an import automatically">
        <p>
          Tags this export's JSON output with a name + version so a matching Import Job can be
          created from it in one click, instead of building the field mapping by hand on the import
          side.
        </p>
        <p>
          <strong>Example:</strong> set integration key <code>ci-confirmation</code>, contract version
          <code>1</code>. Every JSON file this export produces now carries
          <code>{"integrationKey": "ci-confirmation", "contractVersion": 1}</code> in its provenance
          block. On the Import Jobs side, "Create from export" reads that block and offers to build a
          matching import job automatically.
        </p>
        <p>Only affects the JSON output format — leave both fields blank if you don't need this.</p>
      </HelpTooltip>
    </span>
    <p class="text-xs text-text-secondary m-0">
      Tag this export's JSON output with a stable, versioned identifier so a matching
      <code>ImportDefinition</code> can later be created from it. Only takes effect for the JSON output
      format.
    </p>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Integration key</label>
      <input
        type="text"
        v-model="integrationKeyInput"
        placeholder="e.g. ci-confirmation"
        aria-label="Integration key"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono bg-surface outline-none focus:border-brand"
      />
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0">Contract version</label>
      <input
        type="number"
        min="1"
        v-model.number="contractVersionInput"
        placeholder="1"
        aria-label="Contract version"
        class="w-24 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary bg-surface outline-none focus:border-brand"
      />
      <span class="text-xs text-text-muted">set together with the integration key, or leave both blank</span>
    </div>
    <div class="flex items-center gap-2">
      <label class="text-sm text-text-secondary w-36 shrink-0 inline-flex items-center gap-1">
        Correlation key field
        <HelpTooltip label="What is a correlation key?" title="Correlation key field">
          <p>
            The export key (from the field tree above) that a later inbound reply will use to find
            its way back to the right row — think of it as the "order number" printed on both the
            outgoing shipment and the return receipt.
          </p>
          <p>
            <strong>Example:</strong> if this export includes a field with export key <code>guid</code>,
            set correlation key to <code>guid</code>. A paired Import Job's <em>Root match column</em>
            then matches inbound records back to the ERP row using that same value.
          </p>
        </HelpTooltip>
      </label>
      <input
        type="text"
        v-model="correlationKeySourceFieldInput"
        placeholder="e.g. guid"
        aria-label="Correlation key field"
        class="flex-1 px-2.5 py-1.5 border border-border-strong rounded-md text-sm text-text-primary font-mono bg-surface outline-none focus:border-brand"
      />
      <span class="text-xs text-text-muted">the root field name an inbound reply matches back against</span>
    </div>
  </div>
</template>
