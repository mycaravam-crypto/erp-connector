<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { changePassword } from '@/api/auth'
import Button from '@/components/ui/Button.vue'
import Modal from '@/components/ui/Modal.vue'
import Input from '@/components/ui/Input.vue'

// Self-service password change, opened from the user menu. On success the server has signed this account out
// everywhere, so the dialog emits `changed` and the app sends the user back to the login page.
const MIN_LENGTH = 10

const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ changed: [] }>()

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const submitting = ref(false)
const serverError = ref<string | null>(null)

const newPasswordError = computed(() =>
  newPassword.value !== '' && newPassword.value.length < MIN_LENGTH
    ? `At least ${MIN_LENGTH} characters.`
    : undefined,
)
const confirmError = computed(() =>
  confirmPassword.value !== '' && confirmPassword.value !== newPassword.value ? 'The passwords differ.' : undefined,
)
const valid = computed(
  () =>
    currentPassword.value !== '' &&
    newPassword.value.length >= MIN_LENGTH &&
    confirmPassword.value === newPassword.value,
)

watch(open, (isOpen) => {
  if (!isOpen) return
  currentPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  serverError.value = null
})

async function submit() {
  if (!valid.value) return
  submitting.value = true
  serverError.value = null
  try {
    const result = await changePassword(currentPassword.value, newPassword.value)
    if (result.ok) {
      open.value = false
      emit('changed')
    } else {
      serverError.value = result.error
    }
  } catch {
    serverError.value = 'Could not reach the backend. Is the backend service running?'
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <Modal v-model:open="open" title="Change password">
    <p class="text-text-secondary text-sm m-0 mb-4">
      You'll be signed out on every device afterwards and need to sign in again with the new password.
    </p>
    <Input
      v-model="currentPassword"
      type="password"
      label="Current password"
      autocomplete="current-password"
      class="mb-3"
      :error="serverError ?? undefined"
    />
    <Input
      v-model="newPassword"
      type="password"
      label="New password"
      autocomplete="new-password"
      class="mb-3"
      :help-text="`At least ${MIN_LENGTH} characters.`"
      :error="newPasswordError"
    />
    <Input
      v-model="confirmPassword"
      type="password"
      label="Repeat new password"
      autocomplete="new-password"
      :error="confirmError"
    />

    <template #footer>
      <Button variant="ghost" @click="open = false">Cancel</Button>
      <Button :disabled="!valid || submitting" :loading="submitting" @click="submit">
        {{ submitting ? 'Saving…' : 'Change password' }}
      </Button>
    </template>
  </Modal>
</template>
