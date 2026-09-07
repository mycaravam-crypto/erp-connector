<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { getUsername } from '@/api/auth'
import { getImportRun, releaseImportRun, rejectImportRun, type ImportRunDetail } from '@/api/importDefinitions'
import Button from '@/components/ui/Button.vue'
import Modal from '@/components/ui/Modal.vue'
import Input from '@/components/ui/Input.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import ImportPlanDiffTable from '@/components/ImportPlanDiffTable.vue'
import ImportRunCountSummary from '@/components/ImportRunCountSummary.vue'
import ImportRunOutcome from '@/components/ImportRunOutcome.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

// The Phase 17 Slice 6 review/diff view (import-definitions.md §5): reuses ReleaseDialog.vue's
// Operator/Approver form pattern, extended with the full Open Decision #11 count breakdown and a
// field-level diff of the matched/changed rows PlanJson actually carries — a run's plan is the write-side
// source of truth (Open Decision #11), so this dialog only ever displays it, never recomputes its own.
const props = defineProps<{ runId: number | null }>()
const emit = defineEmits<{ resolved: [] }>()
const open = defineModel<boolean>('open', { default: false })

const currentUser = computed(() => getUsername() ?? '')
const approver = ref('')
const serverError = ref<string | null>(null)

const loading = ref(false)
const loadError = ref<string | null>(null)
const detail = ref<ImportRunDetail | null>(null)

const sameUser = computed(
  () => approver.value.trim() !== '' && approver.value.trim().toLowerCase() === currentUser.value.toLowerCase(),
)
const valid = computed(() => approver.value.trim() !== '' && !sameUser.value)
const fieldError = computed(() => {
  if (sameUser.value) return 'Operator and approver must be different people.'
  return serverError.value ?? undefined
})

const isPending = computed(() => detail.value?.status === 'PendingReview')

async function load() {
  if (props.runId === null) return
  loading.value = true
  loadError.value = null
  try {
    detail.value = await getImportRun(props.runId)
    if (detail.value === null) loadError.value = 'Run not found.'
  } catch {
    loadError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    loading.value = false
  }
}

watch(open, (isOpen) => {
  if (isOpen) {
    approver.value = ''
    serverError.value = null
    detail.value = null
    load()
  }
})

const submitting = ref(false)
async function release() {
  if (!valid.value || detail.value === null) return
  submitting.value = true
  serverError.value = null
  try {
    const result = await releaseImportRun(detail.value.id, approver.value.trim())
    if (result.ok) {
      open.value = false
      emit('resolved')
    } else {
      serverError.value = result.error
    }
  } catch {
    serverError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    submitting.value = false
  }
}

const rejecting = ref(false)
async function reject() {
  if (detail.value === null) return
  rejecting.value = true
  serverError.value = null
  try {
    const result = await rejectImportRun(detail.value.id)
    if (result.ok) {
      open.value = false
      emit('resolved')
    } else {
      serverError.value = result.error
    }
  } catch {
    serverError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    rejecting.value = false
  }
}
</script>

<template>
  <Modal v-model:open="open" :title="runId ? `Import Run #${runId} Review` : 'Import Run Review'">
    <p v-if="loading" class="text-text-secondary text-sm m-0">Loading…</p>
    <p v-else-if="loadError" class="text-danger text-sm m-0">{{ loadError }}</p>

    <template v-else-if="detail">
      <div class="flex items-center gap-2 mb-3">
        <StatusBadge :status="detail.status" />
        <span class="text-xs text-text-secondary">{{ detail.importDefinitionName }} · {{ detail.sourceFileName || '(pasted preview)' }}</span>
      </div>

      <ImportRunCountSummary
        :matched-count="detail.matchedCount"
        :changed-count="detail.changedCount"
        :unchanged-count="detail.unchangedCount"
        :rejected-count="detail.rejectedCount"
        :conflict-count="detail.conflictCount"
        :invalid-count="detail.invalidCount"
      />

      <span class="inline-flex items-center gap-1.5 mb-2">
        <h3 class="text-sm font-semibold text-text-primary m-0">Field-level diff — matched/changed rows</h3>
        <HelpTooltip label="How to read this table" title="Field-level diff">
          <p>
            One row per column that would actually change, grouped by which ERP row (correlation
            value) it belongs to. <strong>Old value</strong> is what's in the ERP right now;
            <strong>New value</strong> is what releasing this run would write.
          </p>
          <p>Unchanged and rejected rows aren't shown here — only what would actually be written.</p>
        </HelpTooltip>
      </span>
      <div class="mb-4">
        <ImportPlanDiffTable :operations="detail.operations" />
      </div>

      <template v-if="isPending">
        <p class="text-text-secondary text-sm m-0 mb-3 inline-flex items-start gap-1.5">
          <span>
            Releasing as <strong class="text-text-primary">{{ currentUser }}</strong>. Approver must be a
            different registered user. Rejecting declines the run without writing anything to the ERP.
          </span>
          <HelpTooltip label="Why does this need two people?" title="Four-eyes control">
            <p>
              A "four-eyes" step means one person can't unilaterally push inbound data into the ERP —
              a second, different person has to review the diff above and approve it first. This
              matches the same operator/approver control used for the managed export's release step.
            </p>
          </HelpTooltip>
        </p>
        <Input
          v-model="approver"
          label="Approver username"
          placeholder="Approver username"
          autocomplete="off"
          :error="fieldError"
        />
      </template>
      <ImportRunOutcome
        v-else
        :operated-by="detail.operatedBy"
        :approved-by="detail.approvedBy"
        :released-at="detail.releasedAt"
        :error-message="detail.errorMessage"
      />
    </template>

    <template #footer v-if="detail && isPending">
      <Button variant="ghost" @click="open = false">Cancel</Button>
      <Button variant="danger" :disabled="rejecting || submitting" :loading="rejecting" @click="reject">
        {{ rejecting ? 'Rejecting…' : 'Reject' }}
      </Button>
      <Button :disabled="!valid || submitting || rejecting" :loading="submitting" @click="release">
        {{ submitting ? 'Releasing…' : 'Confirm Release' }}
      </Button>
    </template>
    <template #footer v-else-if="detail">
      <Button variant="ghost" @click="open = false">Close</Button>
    </template>
  </Modal>
</template>
