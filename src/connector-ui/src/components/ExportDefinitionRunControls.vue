<script setup lang="ts">
import { ref } from 'vue'
import {
  updateExportDefinition,
  testExportDefinition,
  type ExportDefinition,
  type ExportDefinitionTestResult,
} from '@/api/exportDefinitions'

const props = defineProps<{
  definition: ExportDefinition
}>()

const saving = ref(false)
const saveStatus = ref<'idle' | 'ok' | 'error'>('idle')
const saveMessage = ref('')

async function save() {
  saving.value = true
  saveStatus.value = 'idle'
  saveMessage.value = ''
  try {
    const d = props.definition
    const result = await updateExportDefinition(d.id, {
      name: d.name,
      description: d.description,
      rootTable: d.rootTable,
      rootNode: d.rootNode,
      outputFormat: d.outputFormat,
      isEnabled: d.isEnabled,
      schedule: d.schedule,
    })
    if (result.ok) {
      // Mutate in place (not props.definition = result.data) so the parent's ref keeps pointing
      // at the same reactive object the rest of this view's template is already bound to.
      Object.assign(props.definition, result.data)
      saveStatus.value = 'ok'
      saveMessage.value = 'Saved.'
    } else {
      saveStatus.value = 'error'
      saveMessage.value = result.error
    }
  } catch {
    saveStatus.value = 'error'
    saveMessage.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    saving.value = false
  }
}

const testing = ref(false)
const testResult = ref<ExportDefinitionTestResult | null>(null)
const testError = ref<string | null>(null)

// Runs the saved definition against the live connection to confirm the fix actually resolves —
// e.g. that a corrected table/column name no longer 404s at the database.
async function runTest() {
  testing.value = true
  testResult.value = null
  testError.value = null
  try {
    const result = await testExportDefinition(props.definition.id)
    if (result.ok) {
      testResult.value = result.data
    } else {
      testError.value = result.error
    }
  } catch {
    testError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    testing.value = false
  }
}
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-3">
      <button
        type="button"
        class="px-5 py-2 border-0 rounded-md bg-slate-900 text-slate-200 text-sm font-semibold cursor-pointer hover:bg-slate-800 disabled:opacity-50"
        :disabled="saving"
        @click="save"
      >{{ saving ? 'Saving…' : 'Save' }}</button>
      <button
        type="button"
        class="px-5 py-2 border border-slate-300 rounded-md bg-white text-slate-700 text-sm font-semibold cursor-pointer hover:bg-slate-50 disabled:opacity-50"
        :disabled="testing"
        @click="runTest"
      >{{ testing ? 'Testing…' : 'Test against live connection' }}</button>
    </div>

    <p v-if="saveStatus === 'ok'" class="text-sm text-green-700 mb-3">{{ saveMessage }}</p>
    <p v-else-if="saveStatus === 'error'" class="text-sm text-red-600 mb-3">{{ saveMessage }}</p>

    <div v-if="testResult" class="bg-slate-50 border border-slate-200 rounded-md px-4 py-3 text-sm mb-3">
      <p v-if="testResult.status === 'Success'" class="text-green-700 m-0">
        Test succeeded — {{ testResult.recordCount }} record(s) read.
      </p>
      <p v-else class="text-red-600 m-0">
        Test failed: {{ testResult.errorMessage }}
      </p>
    </div>
    <p v-else-if="testError" class="text-sm text-red-600 mb-3">{{ testError }}</p>
  </div>
</template>
