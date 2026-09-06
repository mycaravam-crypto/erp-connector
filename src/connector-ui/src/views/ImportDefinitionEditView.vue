<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { getSourceSchema, type SourceTable } from '@/api/connection'
import {
  getImportDefinition,
  createImportDefinition,
  previewImportDefinition,
  listImportDefinitionRuns,
  type ImportDefinition,
  type ImportDefinitionRun,
  type ImportPlan,
} from '@/api/importDefinitions'
import { blankRootNode, columnsAsDisabledScalarFields, collectWritableTargets } from '@/lib/importNodeBuilders'
import ImportDefinitionBasicFields from '@/components/ImportDefinitionBasicFields.vue'
import ImportAllowedColumnsEditor from '@/components/ImportAllowedColumnsEditor.vue'
import ImportDefinitionRunControls from '@/components/ImportDefinitionRunControls.vue'
import ImportNodeTreeEditor from '@/components/ImportNodeTreeEditor.vue'
import ImportDefinitionPreviewPanel from '@/components/ImportDefinitionPreviewPanel.vue'
import ImportDefinitionRunsTable from '@/components/ImportDefinitionRunsTable.vue'
import ImportRunReviewDialog from '@/components/ImportRunReviewDialog.vue'
import { ChevronLeft } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'
import Button from '@/components/ui/Button.vue'

const route = useRoute()
const router = useRouter()
const isNew = computed(() => route.params.id === 'new')

function blankDefinition(): ImportDefinition {
  return {
    id: 0,
    name: '',
    description: null,
    rootTable: '',
    rootMatchColumn: '',
    unmatchedRootPolicy: 'reject',
    allowedWritableColumns: [],
    isEnabled: false,
    configVersion: 0,
    createdBy: '',
    createdAt: '',
    updatedBy: null,
    updatedAt: null,
    rootNode: blankRootNode(),
  }
}

const definition = ref<ImportDefinition | null>(null)
const loading = ref(true)
const loadError = ref<string | null>(null)
const notFound = ref(false)
const availableTables = ref<SourceTable[]>([])

// isSaved gates every feature that needs a real database row: Save vs Create button,
// Duplicate/Delete, preview, and run history all require an id the backend recognizes.
const isSaved = computed(() => (definition.value?.id ?? 0) > 0)
const rootTableLocked = computed(() => (definition.value?.rootNode.children.length ?? 0) > 0)
const usedColumns = computed(() =>
  definition.value ? collectWritableTargets(definition.value.rootNode, definition.value.rootMatchColumn) : [],
)

async function load() {
  loading.value = true
  loadError.value = null
  notFound.value = false

  // Best-effort: the tree editor's dropdowns degrade to free-text inputs if this fails, same fallback
  // ExportDefinitionEditView.vue uses for its own root-table field.
  try {
    availableTables.value = (await getSourceSchema()).tables
  } catch {
    availableTables.value = []
  }

  if (isNew.value) {
    definition.value = blankDefinition()
    loading.value = false
    return
  }

  try {
    const result = await getImportDefinition(Number(route.params.id))
    if (result === null) {
      notFound.value = true
    } else {
      definition.value = result
      await refreshRuns()
    }
  } catch {
    loadError.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function onRootTableChanged() {
  if (!definition.value || rootTableLocked.value) return
  definition.value.rootNode.children = columnsAsDisabledScalarFields(definition.value.rootTable, availableTables.value)
}

const creating = ref(false)
const createError = ref<string | null>(null)

async function create() {
  if (!definition.value) return
  creating.value = true
  createError.value = null
  try {
    const d = definition.value
    const result = await createImportDefinition({
      name: d.name,
      description: d.description,
      rootTable: d.rootTable,
      rootMatchColumn: d.rootMatchColumn,
      rootNode: d.rootNode,
      allowedWritableColumns: d.allowedWritableColumns,
      unmatchedRootPolicy: d.unmatchedRootPolicy,
      isEnabled: d.isEnabled,
    })
    if (result.ok) {
      definition.value = result.data
      await router.replace({ name: 'import-definition-edit', params: { id: result.data.id } })
    } else {
      createError.value = result.error
    }
  } catch {
    createError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    creating.value = false
  }
}

function onDuplicated(copy: ImportDefinition) {
  router.push({ name: 'import-definition-edit', params: { id: copy.id } })
}

function onDeleted() {
  router.push({ name: 'import-definitions' })
}

const previewInboundJson = ref('')
const previewLoading = ref(false)
const previewError = ref<string | null>(null)
const previewPlan = ref<ImportPlan | null>(null)

async function runPreview() {
  if (!definition.value) return
  previewLoading.value = true
  previewError.value = null
  try {
    const result = await previewImportDefinition(definition.value.id, previewInboundJson.value)
    if (result.ok) {
      previewPlan.value = result.data
    } else {
      previewError.value = result.error
    }
  } catch {
    previewError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    previewLoading.value = false
  }
}

const runsLoading = ref(false)
const runsError = ref<string | null>(null)
const runs = ref<ImportDefinitionRun[]>([])

async function refreshRuns() {
  if (!definition.value) return
  runsLoading.value = true
  runsError.value = null
  try {
    runs.value = await listImportDefinitionRuns(definition.value.id)
  } catch {
    runsError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    runsLoading.value = false
  }
}

const reviewOpen = ref(false)
const reviewingRunId = ref<number | null>(null)

function openReview(runId: number) {
  reviewingRunId.value = runId
  reviewOpen.value = true
}

function onReviewResolved() {
  refreshRuns()
}
</script>

<template>
  <div class="max-w-3xl">
    <Button variant="ghost" class="mb-4" @click="router.push({ name: 'import-definitions' })">
      <template #icon><Icon :icon="ChevronLeft" :size="16" /></template>
      Back to list
    </Button>

    <p v-if="loading" class="text-text-secondary">Loading…</p>
    <p v-else-if="notFound" class="text-danger">Import definition not found.</p>
    <p v-else-if="loadError" class="text-danger">{{ loadError }}</p>

    <template v-else-if="definition">
      <h1 class="m-0 text-xl font-semibold text-text-primary mb-1">{{ isSaved ? definition.name || '(untitled)' : 'New Import Definition' }}</h1>
      <p v-if="isSaved" class="text-text-secondary text-sm mt-1 mb-5">
        Config version {{ definition.configVersion }} · created by {{ definition.createdBy }}
      </p>

      <ImportDefinitionBasicFields
        :definition="definition"
        :available-tables="availableTables"
        :root-table-locked="rootTableLocked"
        @root-table-changed="onRootTableChanged"
      />

      <ImportAllowedColumnsEditor :columns="definition.allowedWritableColumns" :used-columns="usedColumns" />

      <h2 class="text-base font-semibold text-text-primary mb-2.5">Fields</h2>
      <p class="text-text-secondary text-sm mb-3 leading-relaxed">
        Add the root's correlation-key field (mapped to the root match column above) plus every
        confirmation/status field the vendor may write back. Picking a related table fills in every one of
        its columns (unchecked) so you only have to check the ones you want.
      </p>
      <ImportNodeTreeEditor
        v-if="definition.rootTable"
        :nodes="definition.rootNode.children"
        :context-table="definition.rootTable"
        :available-tables="availableTables"
        :depth="0"
        class="mb-6"
      />
      <p v-else class="text-text-muted text-sm mb-6">Select a root table above to start adding fields.</p>

      <template v-if="isSaved">
        <ImportDefinitionRunControls
          :definition="definition"
          @duplicated="onDuplicated"
          @deleted="onDeleted"
        />

        <div class="mt-6 mb-6">
          <ImportDefinitionPreviewPanel
            v-model:inbound-json="previewInboundJson"
            :plan="previewPlan"
            :loading="previewLoading"
            :error="previewError"
            @refresh="runPreview"
          />
        </div>

        <ImportDefinitionRunsTable
          :runs="runs"
          :loading="runsLoading"
          :error="runsError"
          @refresh="refreshRuns"
          @review="openReview"
        />
        <ImportRunReviewDialog v-model:open="reviewOpen" :run-id="reviewingRunId" @resolved="onReviewResolved" />
      </template>

      <template v-else>
        <Button :disabled="creating || !definition.rootTable" :loading="creating" @click="create">
          {{ creating ? 'Creating…' : 'Create' }}
        </Button>
        <p v-if="createError" class="text-sm text-danger mt-3">{{ createError }}</p>
      </template>
    </template>
  </div>
</template>
