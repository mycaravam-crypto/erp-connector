<script setup lang="ts">
import { ref } from 'vue'
import {
  updateExportDefinition,
  testExportDefinition,
  runExportDefinition,
  duplicateExportDefinition,
  deleteExportDefinition,
  type ExportDefinition,
  type ExportDefinitionTestResult,
} from '@/api/exportDefinitions'

const props = defineProps<{
  definition: ExportDefinition
}>()
const emit = defineEmits<{
  duplicated: [data: ExportDefinition]
  deleted: []
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

const running = ref(false)
const runError = ref<string | null>(null)
const runMessage = ref<string | null>(null)

async function runNow() {
  running.value = true
  runError.value = null
  runMessage.value = null
  try {
    const result = await runExportDefinition(props.definition.id)
    if (result.ok) {
      downloadBlob(result.blob, result.fileName)
      runMessage.value = `Downloaded ${result.fileName} — ${result.recordCount} record(s).`
    } else {
      runError.value = result.error
    }
  } catch {
    runError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    running.value = false
  }
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.click()
  URL.revokeObjectURL(url)
}

const duplicating = ref(false)
async function duplicate() {
  duplicating.value = true
  try {
    const result = await duplicateExportDefinition(props.definition.id)
    if (result.ok) emit('duplicated', result.data)
  } finally {
    duplicating.value = false
  }
}

const deleting = ref(false)
const confirmingDelete = ref(false)
async function confirmDelete() {
  deleting.value = true
  try {
    if (await deleteExportDefinition(props.definition.id)) emit('deleted')
  } finally {
    deleting.value = false
    confirmingDelete.value = false
  }
}
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-3 flex-wrap">
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
      <button
        type="button"
        class="px-5 py-2 border border-slate-300 rounded-md bg-white text-slate-700 text-sm font-semibold cursor-pointer hover:bg-slate-50 disabled:opacity-50"
        :disabled="running"
        @click="runNow"
      >{{ running ? 'Running…' : 'Run Now' }}</button>
      <button
        type="button"
        class="px-4 py-2 border border-slate-300 rounded-md bg-white text-slate-700 text-sm cursor-pointer hover:bg-slate-50 disabled:opacity-50"
        :disabled="duplicating"
        @click="duplicate"
      >{{ duplicating ? 'Duplicating…' : 'Duplicate' }}</button>

      <div class="ml-auto flex items-center gap-2">
        <template v-if="confirmingDelete">
          <span class="text-sm text-red-700">Delete permanently?</span>
          <button
            type="button"
            class="px-3 py-1.5 border-0 rounded-md bg-red-600 text-white text-sm font-semibold cursor-pointer hover:bg-red-700 disabled:opacity-50"
            :disabled="deleting"
            @click="confirmDelete"
          >{{ deleting ? 'Deleting…' : 'Confirm' }}</button>
          <button
            type="button"
            class="px-3 py-1.5 border border-slate-300 rounded-md bg-white text-slate-700 text-sm cursor-pointer hover:bg-slate-50"
            @click="confirmingDelete = false"
          >Cancel</button>
        </template>
        <button
          v-else
          type="button"
          class="px-4 py-2 border border-red-200 rounded-md bg-white text-red-600 text-sm cursor-pointer hover:bg-red-50"
          @click="confirmingDelete = true"
        >Delete</button>
      </div>
    </div>

    <p v-if="saveStatus === 'ok'" class="text-sm text-green-700 mb-3">{{ saveMessage }}</p>
    <p v-else-if="saveStatus === 'error'" class="text-sm text-red-600 mb-3">{{ saveMessage }}</p>

    <p v-if="runMessage" class="text-sm text-green-700 mb-3">{{ runMessage }}</p>
    <p v-else-if="runError" class="text-sm text-red-600 mb-3">{{ runError }}</p>

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
