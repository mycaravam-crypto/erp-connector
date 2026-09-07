<script setup lang="ts">
import { ref, computed } from 'vue'
import { getUsername } from '@/api/auth'
import { releaseExport } from '@/api/exports'
import Button from '@/components/ui/Button.vue'
import Modal from '@/components/ui/Modal.vue'
import Input from '@/components/ui/Input.vue'
import HelpTooltip from '@/components/ui/HelpTooltip.vue'

const props = defineProps<{ seqNo: number }>()
const emit = defineEmits<{ (e: 'released'): void }>()

const open = ref(false)
const currentUser = computed(() => getUsername() ?? '')
const approver = ref('')
const submitting = ref(false)
const serverError = ref<string | null>(null)

const sameUser = computed(
  () =>
    approver.value.trim() !== '' &&
    approver.value.trim().toLowerCase() === currentUser.value.toLowerCase(),
)

const valid = computed(() => approver.value.trim() !== '' && !sameUser.value)

const fieldError = computed(() => {
  if (sameUser.value) return 'Operator and approver must be different people.'
  return serverError.value ?? undefined
})

function openDialog() {
  approver.value = ''
  serverError.value = null
  open.value = true
}

async function submit() {
  if (!valid.value) return
  submitting.value = true
  serverError.value = null

  try {
    const result = await releaseExport(props.seqNo, { approver: approver.value.trim() })
    if (result.ok) {
      open.value = false
      emit('released')
    } else {
      serverError.value = result.message || `Error ${result.status}`
    }
  } catch {
    serverError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="release-dialog mt-6">
    <Button @click="openDialog">Release Run</Button>

    <Modal v-model:open="open" :title="`Four-Eyes Release — Run #${seqNo}`">
      <p class="text-text-secondary text-sm m-0 mb-4 inline-flex items-start gap-1.5">
        <span>
          Releasing as <strong class="text-text-primary">{{ currentUser }}</strong>. Approver must be a different registered user.
        </span>
        <HelpTooltip label="Why do I need an approver?" title="Four-eyes control">
          <p>
            A second, different person has to confirm the release before it's final — the same person
            who triggered the export can't approve their own run. Enter that colleague's username
            here; they don't need to be present, but they are accountable for the confirmation.
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

      <template #footer>
        <Button variant="ghost" @click="open = false">Cancel</Button>
        <Button :disabled="!valid || submitting" :loading="submitting" @click="submit">
          {{ submitting ? 'Releasing…' : 'Confirm Release' }}
        </Button>
      </template>
    </Modal>
  </div>
</template>
