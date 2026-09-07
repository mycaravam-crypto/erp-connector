<script setup lang="ts">
// "Create from export" (knowledge/pipeline/import-mapping-presets.md §3.4/§4, Open Decision #1): the New
// Import Definition flow's other starting point besides "Start blank." A dedicated step rather than
// reusing ImportDefinitionPreviewPanel.vue's paste-a-sample affordance verbatim — that panel drives
// POST .../{id}/preview against an *already-saved* definition's live ERP connection, which doesn't exist
// yet here. This mirrors its paste-JSON UX instead, wired to the suggestion lookup rather than a plan diff.
import { ref } from 'vue'
import { suggestImportMappingFromExport, type ImportMappingSuggestion } from '@/api/importDefinitions'
import Button from '@/components/ui/Button.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

const emit = defineEmits<{
  accept: [suggestion: ImportMappingSuggestion]
  'start-blank': []
}>()

const inboundJson = ref('')
const checking = ref(false)
const checked = ref(false)
const suggestion = ref<ImportMappingSuggestion | null>(null)
const missReason = ref<string | null>(null)

async function check() {
  checking.value = true
  checked.value = false
  suggestion.value = null
  missReason.value = null
  try {
    const result = await suggestImportMappingFromExport(inboundJson.value)
    suggestion.value = result.suggestion
    missReason.value = result.reason
  } finally {
    checking.value = false
    checked.value = true
  }
}
</script>

<template>
  <div class="border border-border-strong rounded-lg p-4 mb-6 bg-surface-elevated">
    <span class="inline-flex items-center gap-1.5 mb-1">
      <h2 class="m-0 text-base font-semibold text-text-primary">Start from</h2>
      <HelpTooltip label="How does this work?" title="Auto-detecting a matching export">
        <p>
          Take one real JSON file produced by an export that has <strong>Integration tagging</strong>
          set up, and paste it here. If its <code>provenance</code> block matches a known export, this
          builds the whole field tree for you instead of starting from an empty one.
        </p>
        <p>No match, or don't have a sample? Use <strong>Start blank</strong> instead — nothing is lost.</p>
      </HelpTooltip>
    </span>
    <p class="text-xs text-text-secondary m-0 mb-3">
      Paste a sample inbound <code>ImportEnvelope</code> carrying a <code>provenance</code> block to check
      whether it matches a known export's tagged output — or skip this and start with a blank tree.
    </p>

    <textarea
      v-model="inboundJson"
      rows="5"
      placeholder='{"schemaVersion": "1", "provenance": {"integrationKey": "...", "contractVersion": 1}, "records": [...]}'
      aria-label="Sample inbound JSON"
      class="w-full px-3 py-2 border border-border-strong rounded-md text-xs font-mono text-text-primary bg-surface outline-none focus:border-brand mb-2"
    ></textarea>

    <div class="flex items-center gap-2 flex-wrap">
      <Button variant="secondary" :disabled="checking || inboundJson.trim() === ''" :loading="checking" @click="check">
        {{ checking ? 'Checking…' : 'Check for match' }}
      </Button>
      <Button variant="ghost" @click="emit('start-blank')">Start blank</Button>
    </div>

    <div
      v-if="checked && suggestion"
      class="mt-3 p-3 border border-brand rounded-md bg-surface flex items-center justify-between gap-3 flex-wrap"
    >
      <p class="m-0 text-sm text-text-primary">
        Create from export "<strong>{{ suggestion.exportDefinitionName }}</strong>" (v{{ suggestion.contractVersion }})?
      </p>
      <Button @click="emit('accept', suggestion)">Create from export</Button>
    </div>
    <p v-else-if="checked" class="mt-3 text-sm text-text-secondary">
      {{ missReason ?? 'No matching export found for this sample — start blank instead, or check a different sample.' }}
    </p>
  </div>
</template>
