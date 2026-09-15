<script setup lang="ts">
// Preview runs POST .../preview — parses + walks + plans a sample inbound file against this saved
// definition with zero persistence (no ImportRunEntity, nothing written to the ERP). Run instead posts to
// .../runs, which stages a real ImportRunEntity at PendingReview the same way the inbound/ folder watcher
// would — the parent view owns that call so it can refresh run history / open the review dialog.
import { computed, ref } from 'vue'
import type { ImportNode, ImportPlan } from '@/api/importDefinitions'
import { detectExportFile, hasIntegrationKeyProvenance, toImportEnvelope } from '@/lib/exportedFileDetection'
import { findUnmappedRootFields } from '@/lib/importPreviewHints'
import ImportPlanDiffTable from '@/components/ImportPlanDiffTable.vue'
import ImportUnmappedFieldsWarning from '@/components/ImportUnmappedFieldsWarning.vue'
import ImportPlanSummaryBadges from '@/components/ImportPlanSummaryBadges.vue'
import Alert from '@/components/ui/Alert.vue'
import Button from '@/components/ui/Button.vue'
import SectionHeader from '@/components/ui/SectionHeader.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

const inboundJson = defineModel<string>('inboundJson', { default: '' })

const props = defineProps<{
  plan: ImportPlan | null
  loading: boolean
  error: string | null
  running: boolean
  // Optional: the saved definition's field tree, used only for the client-side "unmapped field" hint
  // below — never sent anywhere, never affects what Preview actually runs.
  rootNode?: ImportNode | null
}>()
const emit = defineEmits<{
  refresh: []
  'create-from-export': [envelopeJson: string]
  run: [sourceFileName: string | undefined]
}>()

// The last file picked via "Choose File", so Run can pass its name along as SourceFileName — cosmetic
// only (shown in run history), so a paste-in-textarea Run just goes through without one.
const selectedFileName = ref<string | undefined>(undefined)
const fileInput = ref<HTMLInputElement | null>(null)

async function onFileSelected(event: Event) {
  const file = (event.target as HTMLInputElement).files?.[0]
  if (!file) return
  inboundJson.value = await file.text()
  selectedFileName.value = file.name
  if (fileInput.value) fileInput.value.value = ''
}

function runImport() {
  emit('run', selectedFileName.value)
}

// Detects a pasted exported job file (see lib/exportedFileDetection.ts) so it can be offered a one-click
// fix instead of only a rejection once Preview is clicked — the same confusion the backend's
// ImportNodeWalker.ParseRecords error now names explicitly.
const detectedExport = computed(() => detectExportFile(inboundJson.value))

// Root-level record keys that don't match any enabled scalar-field's SourceKey on this definition — the
// walker (ImportNodeWalker.DiffScalarFields) silently ignores these, so an operator editing one of them in
// their sample would see no change here no matter how many times they click Preview. A best-effort,
// client-side-only hint: never blocks Preview, never replaces the server's own validation.
const unmappedFields = computed(() => findUnmappedRootFields(inboundJson.value, props.rootNode ?? null))

// The exact sample text a plan on screen actually reflects. Editing the textarea after Preview has run
// doesn't recompute anything by itself (Preview is a manual, explicit action) — this powers the "stale"
// banner below so that gap is visible instead of silently showing an old result for new text.
const lastPreviewedJson = ref<string | null>(null)
const isStale = computed(
  () => props.plan !== null && lastPreviewedJson.value !== null && lastPreviewedJson.value !== inboundJson.value,
)

function runPreview() {
  lastPreviewedJson.value = inboundJson.value
  emit('refresh')
}

function convertToEnvelope() {
  if (detectedExport.value) inboundJson.value = toImportEnvelope(detectedExport.value)
}

function createFromExport() {
  if (detectedExport.value) emit('create-from-export', toImportEnvelope(detectedExport.value))
}
</script>

<template>
  <div>
    <SectionHeader title="Preview & Run" class="mb-1">
      <template #help>
        <HelpTooltip label="What's the difference between Preview and Run?" title="A dry run vs. the real thing">
          <p>
            Both run your sample JSON through this definition's real logic — matching, correlation, the
            allowed-columns check. <strong>Preview</strong> stops there: nothing is written to the ERP, no
            run is recorded, so it's safe to click repeatedly while you're still shaping a mapping.
            <strong>Run</strong> instead stages a real <code>PendingReview</code> run, the same as a file
            dropped in the inbound folder — it shows up in run history below and needs an Approver to
            release it before anything actually reaches the ERP.
          </p>
          <p>
            <strong>Changed</strong> compares each mapped field to the value <strong>currently in the
            database</strong> — not to a previous export or an earlier preview. A field can look edited in
            your sample and still count as unchanged if the database already has that value, and a field
            that isn't mapped as a writable column below is never compared at all.
          </p>
          <p>
            <strong>Rejected</strong> means its correlation key matched no row; <strong>Invalid</strong>
            means the record itself was malformed. Neither Preview nor Run refreshes on its own — editing
            the sample afterwards doesn't recompute anything until you click the button again.
          </p>
        </HelpTooltip>
      </template>
    </SectionHeader>
    <p class="text-xs text-text-secondary m-0 mb-2">
      Paste or select a sample <code>ImportEnvelope</code> JSON (schemaVersion + records[]). <strong>Preview</strong>
      is a dry run — nothing is written, no run is recorded. <strong>Run</strong> stages it for real, the
      same as a file dropped in the inbound folder, for an Approver to review and release.
    </p>

    <input ref="fileInput" type="file" accept="application/json,.json" class="hidden" @change="onFileSelected" />
    <div class="flex items-center gap-2 mb-2">
      <Button variant="secondary" @click="fileInput?.click()">Choose File…</Button>
      <span v-if="selectedFileName" class="text-xs text-text-secondary">{{ selectedFileName }}</span>
    </div>

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

    <ImportUnmappedFieldsWarning v-else :fields="unmappedFields" />

    <div class="flex items-center gap-2 mb-3">
      <button
        class="px-2.5 py-1 border border-border-strong rounded-md bg-surface text-xs text-text-secondary cursor-pointer disabled:opacity-50 hover:enabled:bg-surface-elevated"
        :disabled="loading || inboundJson.trim() === ''"
        @click="runPreview"
      >{{ loading ? 'Previewing…' : 'Preview' }}</button>
      <Button :disabled="running || inboundJson.trim() === ''" :loading="running" @click="runImport">
        {{ running ? 'Running…' : 'Run' }}
      </Button>
    </div>

    <p v-if="error" class="text-danger text-sm">{{ error }}</p>

    <template v-else-if="plan">
      <ImportPlanSummaryBadges :plan="plan" :is-stale="isStale" />
      <ImportPlanDiffTable :operations="plan.operations" />
    </template>
  </div>
</template>
