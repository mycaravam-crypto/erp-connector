<script setup lang="ts">
import { ref, computed } from 'vue'
import { createUser } from '@/api/users'
import Button from '@/components/ui/Button.vue'
import Input from '@/components/ui/Input.vue'
import Select from '@/components/ui/Select.vue'
import { useToasts } from '@/composables/useToasts'

// Settings → Users' "add a user" row: username, initial password and role. Emits `added` so the list reloads.
const props = defineProps<{ minPasswordLength: number }>()
const emit = defineEmits<{ added: [] }>()
const toasts = useToasts()

const username = ref('')
const password = ref('')
const role = ref<'Admin' | 'User'>('User')
const adding = ref(false)
const canAdd = computed(() => username.value.trim() !== '' && password.value.length >= props.minPasswordLength)

async function add() {
  if (!canAdd.value) return
  adding.value = true
  try {
    const result = await createUser(username.value.trim(), password.value, role.value)
    if (result.ok) {
      toasts.success(`User ${result.data.username} added.`)
      username.value = ''
      password.value = ''
      role.value = 'User'
      emit('added')
    } else {
      toasts.error(result.error)
    }
  } finally {
    adding.value = false
  }
}
</script>

<template>
  <form class="flex flex-wrap items-end gap-4" @submit.prevent="add">
    <Input v-model="username" label="New username" autocomplete="off" class="w-48" />
    <Input
      v-model="password"
      type="password"
      label="Initial password"
      autocomplete="new-password"
      :help-text="`At least ${minPasswordLength} characters.`"
      class="w-56"
    />
    <Select v-model="role" label="Role" class="w-28">
      <option value="User">User</option>
      <option value="Admin">Admin</option>
    </Select>
    <Button type="submit" variant="secondary" :disabled="!canAdd" :loading="adding">
      {{ adding ? 'Adding…' : 'Add user' }}
    </Button>
  </form>
</template>
