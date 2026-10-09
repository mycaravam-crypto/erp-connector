<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import ConfirmAction from '@/components/ui/ConfirmAction.vue'

// The per-row actions of the Export Jobs and Import Jobs lists: open (Edit, or View without the edit permission),
// then Test, Duplicate and Delete (with confirm), each shown only when the parent allows it.
withDefaults(
  defineProps<{
    /** Route of the job's edit page. */
    to: RouteLocationRaw
    /** Whether the user may change the job, which labels the link Edit rather than View. */
    canEdit: boolean
    showTest?: boolean
    showDuplicate: boolean
    showDelete: boolean
    /** True while that action runs for this row. */
    testing?: boolean
    duplicating: boolean
    deleting: boolean
  }>(),
  { showTest: false, testing: false },
)
const confirming = defineModel<boolean>('confirming', { default: false })
const emit = defineEmits<{ test: []; duplicate: []; delete: [] }>()

const linkButton =
  'text-text-secondary text-sm bg-transparent border-0 p-0 cursor-pointer hover:underline disabled:opacity-50'
</script>

<template>
  <div class="flex items-center gap-2.5 justify-end">
    <RouterLink :to="to" class="text-brand text-sm hover:underline">{{ canEdit ? 'Edit' : 'View' }}</RouterLink>
    <button v-if="showTest" type="button" :class="linkButton" :disabled="testing" @click="emit('test')">
      {{ testing ? 'Testing…' : 'Test' }}
    </button>
    <button v-if="showDuplicate" type="button" :class="linkButton" :disabled="duplicating" @click="emit('duplicate')">
      {{ duplicating ? 'Duplicating…' : 'Duplicate' }}
    </button>
    <ConfirmAction
      v-if="showDelete"
      v-model:confirming="confirming"
      variant="link"
      :busy="deleting"
      :confirm-label="deleting ? 'Deleting…' : 'Confirm'"
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
