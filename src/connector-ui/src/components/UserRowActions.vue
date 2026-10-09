<script setup lang="ts">
import type { UserAccount } from '@/api/users'
import ConfirmAction from '@/components/ui/ConfirmAction.vue'

// The actions on one Settings → Users row. Your own row offers only the password reset: the server refuses
// changing your own role or deleting yourself.
const props = defineProps<{
  user: UserAccount
  /** Whether this row is the signed-in Admin. */
  self: boolean
  /** True while a change to this user runs. */
  busy: boolean
}>()
const confirming = defineModel<boolean>('confirming', { default: false })
const emit = defineEmits<{ switchRole: []; resetPassword: []; delete: [] }>()

const switchLabel = () => (props.user.role === 'Admin' ? 'Make User' : 'Make Admin')
</script>

<template>
  <div class="flex items-center gap-2.5 justify-end">
    <template v-if="!self">
      <button
        type="button"
        class="text-brand text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline disabled:opacity-50"
        :disabled="busy"
        @click="emit('switchRole')"
      >{{ switchLabel() }}</button>
    </template>
    <button
      type="button"
      class="text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
      @click="emit('resetPassword')"
    >Reset password</button>
    <ConfirmAction
      v-if="!self"
      v-model:confirming="confirming"
      variant="link"
      :busy="busy"
      @confirm="emit('delete')"
    >
      <template #trigger="{ open }">
        <button
          type="button"
          class="text-danger text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline"
          @click="open"
        >Delete</button>
      </template>
    </ConfirmAction>
  </div>
</template>
