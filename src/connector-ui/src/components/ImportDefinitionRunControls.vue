<script setup lang="ts">
import { ref } from 'vue'
import {
  updateImportDefinition,
  duplicateImportDefinition,
  deleteImportDefinition,
  type ImportDefinition,
} from '@/api/importDefinitions'
import Button from '@/components/ui/Button.vue'
import ConfirmAction from '@/components/ui/ConfirmAction.vue'

// The import-side analogue of ExportDefinitionRunControls.vue — deliberately narrower: there's no
// "Test against live connection" or "Run Now" here. An import definition has nothing to run on demand —
// staging a real run is Slice 4's inbound/ folder watcher, and previewing against a sample file is
// ImportDefinitionPreviewPanel.vue's job, not a mutating action this component owns.
const props = defineProps<{
  definition: ImportDefinition
}>()
const emit = defineEmits<{
  duplicated: [data: ImportDefinition]
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
    const result = await updateImportDefinition(d.id, {
      name: d.name,
      description: d.description,
      rootTable: d.rootTable,
      rootMatchColumn: d.rootMatchColumn,
      rootNode: d.rootNode,
      allowedWritableColumns: d.allowedWritableColumns,
      unmatchedRootPolicy: d.unmatchedRootPolicy,
      isEnabled: d.isEnabled,
      integrationKey: d.integrationKey,
      contractVersion: d.contractVersion,
    })
    if (result.ok) {
      // Mutate in place (not props.definition = result.data) so the parent's ref keeps pointing at the
      // same reactive object the rest of this view's template is already bound to.
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

const duplicating = ref(false)
async function duplicate() {
  duplicating.value = true
  try {
    const result = await duplicateImportDefinition(props.definition.id)
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
    if (await deleteImportDefinition(props.definition.id)) emit('deleted')
  } finally {
    deleting.value = false
    confirmingDelete.value = false
  }
}
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-3 flex-wrap">
      <Button :loading="saving" @click="save">{{ saving ? 'Saving…' : 'Save' }}</Button>
      <Button variant="secondary" :loading="duplicating" @click="duplicate">{{ duplicating ? 'Duplicating…' : 'Duplicate' }}</Button>

      <div class="ml-auto flex items-center gap-2">
        <ConfirmAction
          v-model:confirming="confirmingDelete"
          :busy="deleting"
          :confirm-label="deleting ? 'Deleting…' : 'Confirm'"
          @confirm="confirmDelete"
        >
          <template #trigger="{ open }">
            <Button variant="danger" @click="open">Delete</Button>
          </template>
        </ConfirmAction>
      </div>
    </div>

    <p v-if="saveStatus === 'ok'" class="text-sm text-success mb-3">{{ saveMessage }}</p>
    <p v-else-if="saveStatus === 'error'" class="text-sm text-danger mb-3">{{ saveMessage }}</p>
  </div>
</template>
