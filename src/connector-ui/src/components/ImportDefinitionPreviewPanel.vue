<script setup lang="ts">
// Runs POST /api/import-definitions/{id}/preview — parses + walks + plans a sample inbound file against
// this saved definition, with zero persistence (no ImportRunEntity, nothing written to the ERP). Unlike
// the export side's preview (which just re-runs the live query), the import side has nothing to run
// against without a file: Slice 4's inbound/ folder watcher is the real trigger, so an operator pastes a
// sample ImportEnvelope JSON here to sanity-check the tree before a real vendor file ever arrives.
import { computed } from 'vue'
import type { ImportPlan } from '@/api/importDefinitions'
import { detectExportFile, hasIntegrationKeyProvenance, toImportEnvelope } from '@/lib/exportedFileDetection'
import ImportPlanDiffTable from '@/components/ImportPlanDiffTable.vue'
import Alert from '@/components/ui/Alert.vue'
import Badge from '@/components/ui/Badge.vue'
import Button from '@/components/ui/Button.vue'
import SectionHeader from '@/components/ui/SectionHeader.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

const inboundJson = defineModel<string>('inboundJson', { default: '' })

defineProps<{
  plan: ImportPlan | null
  loading: boolean
  error: string | null
}>()
const emit = defineEmits<{ refresh: []; 'create-from-export': [envelopeJson: string] }>()

// Detects a pasted exported job file (see lib/exportedFileDetection.ts) so it can be offered a one-click
// fix instead of only a rejection once Preview is clicked — the same confusion the backend's
// ImportNodeWalker.ParseRecords error now names explicitly.
const detectedExport = computed(() => detectExportFile(inboundJson.value))

function convertToEnvelope() {
  if (detectedExport.value) inboundJson.value = toImportEnvelope(detectedExport.value)
}

function createFromExport() {
  if (detectedExport.value) emit('create-from-export', toImportEnvelope(detectedExport.value))
}
</script>

<template>
  <div>
    <SectionHeader title="Preview" class="mb-1">
      <template #help>
        <HelpTooltip label="What does Preview do?" title="A safe dry run">
          <p>
            Runs your sample JSON through this definition's real logic — matching, correlation, the
            allowed-columns check — without writing anything to the ERP or recording a run.
          </p>
          <p>
            <strong>Changed</strong> means that row would actually be written to on a real run;
            <strong>Rejected</strong> means its correlation key matched no row; <strong>Invalid</strong>
            means the record itself was malformed.
          </p>
        </HelpTooltip>
      </template>
    </SectionHeader>
    <p class="text-xs text-text-secondary m-0 mb-2">
      Paste a sample <code>ImportEnvelope</code> JSON (schemaVersion + records[]) to see what this
      definition would do to it — nothing is written, and no run is recorded.
    </p>

    <textarea
      v-model="inboundJson"
      rows="6"
      placeholder='{"schemaVersion": 1, "definition": "...", "records": [...]}'
      aria-label="Sample inbound JSON"
      class="w-full px-3 py-2 border border-border-strong rounded-md text-xs font-mono text-text-primary outline-none focus:border-brand mb-2"
    ></textarea>

    <Alert v-if="detectedExport" variant="info" class="mb-3">
      This looks like an exported job file, not an <code>ImportEnvelope</code> — its <code>records</code>
      are reusable, but the wrapper needs to change first.
      <div class="flex items-center gap-2 flex-wrap mt-2">
        <Button variant="secondary" @click="convertToEnvelope">Convert to ImportEnvelope</Button>
        <Button v-if="hasIntegrationKeyProvenance(detectedExport)" variant="ghost" @click="createFromExport">
          Create Import Definition from this export
        </Button>
      </div>
    </Alert>

    <button
      class="px-2.5 py-1 border border-border-strong rounded-md bg-surface text-xs text-text-secondary cursor-pointer disabled:opacity-50 hover:enabled:bg-surface-elevated mb-3"
      :disabled="loading || inboundJson.trim() === ''"
      @click="$emit('refresh')"
    >{{ loading ? 'Previewing…' : 'Preview' }}</button>

    <p v-if="error" class="text-danger text-sm">{{ error }}</p>

    <template v-else-if="plan">
      <div class="flex flex-wrap gap-2 mb-3">
        <Badge variant="neutral">{{ plan.recordCount }} record(s)</Badge>
        <Badge variant="success">{{ plan.changedCount }} changed</Badge>
        <Badge variant="neutral">{{ plan.unchangedCount }} unchanged</Badge>
        <Badge variant="warning">{{ plan.rejectedCount }} rejected</Badge>
        <Badge variant="danger">{{ plan.invalidCount }} invalid</Badge>
      </div>

      <ImportPlanDiffTable :operations="plan.operations" />
    </template>
  </div>
</template>
