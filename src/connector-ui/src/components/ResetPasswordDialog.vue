<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { updateUser } from '@/api/users'
import Button from '@/components/ui/Button.vue'
import Modal from '@/components/ui/Modal.vue'
import Input from '@/components/ui/Input.vue'
import { useToasts } from '@/composables/useToasts'

// An Admin sets a new password for `username` (open while it is non-null). The server signs that user out
// everywhere.
const props = defineProps<{ minPasswordLength: number }>()
const username = defineModel<string | null>('username', { default: null })
const toasts = useToasts()

const password = ref('')
const saving = ref(false)
const open = computed({
  get: () => username.value !== null,
  set: (isOpen) => {
    if (!isOpen) username.value = null
  },
})
const valid = computed(() => password.value.length >= props.minPasswordLength)

watch(username, () => (password.value = ''))

async function submit() {
  const target = username.value
  if (!target || !valid.value) return
  saving.value = true
  try {
    const result = await updateUser(target, { password: password.value })
    if (result.ok) {
      toasts.success(`Password of ${target} reset. Their open sessions were signed out.`)
      username.value = null
    } else {
      toasts.error(result.error)
    }
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Modal v-model:open="open" :title="`Reset password — ${username}`">
    <p class="text-text-secondary text-sm m-0 mb-4">
      Sets a new password and signs this user out everywhere. Tell them the new password yourself; they can
      change it afterwards from their user menu.
    </p>
    <Input
      v-model="password"
      type="password"
      label="New password"
      autocomplete="new-password"
      :help-text="`At least ${minPasswordLength} characters.`"
    />
    <template #footer>
      <Button variant="ghost" @click="open = false">Cancel</Button>
      <Button :disabled="!valid || saving" :loading="saving" @click="submit">Reset password</Button>
    </template>
  </Modal>
</template>
