<script setup lang="ts">
import { ref, computed } from 'vue'
import { getUsername } from '@/api/auth'
import { releaseExport } from '@/api/exports'

const props = defineProps<{ seqNo: number }>()
const emit = defineEmits<{ (e: 'released'): void }>()

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

async function submit() {
  if (!valid.value) return
  submitting.value = true
  serverError.value = null

  const result = await releaseExport(props.seqNo, { approver: approver.value.trim() })

  submitting.value = false

  if (result.ok) {
    emit('released')
  } else {
    serverError.value = result.message || `Error ${result.status}`
  }
}
</script>

<template>
  <div class="release-dialog border border-slate-200 rounded-lg px-6 py-5 max-w-sm mt-6">
    <h2 class="m-0 mb-1 text-base font-semibold">Four-Eyes Release — Run #{{ seqNo }}</h2>
    <p class="text-slate-500 text-sm m-0 mb-4">
      Releasing as <strong>{{ currentUser }}</strong>. Approver must be a different registered user.
    </p>

    <div class="flex flex-col gap-1 mb-3">
      <label for="approver" class="text-sm font-semibold">Approver username</label>
      <input
        id="approver"
        v-model="approver"
        placeholder="Approver username"
        autocomplete="off"
        class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
      />
    </div>

    <p v-if="sameUser" class="text-red-600 text-sm mb-1">Operator and approver must be different people.</p>
    <p v-if="serverError" class="text-red-600 text-sm mb-1">{{ serverError }}</p>

    <button
      class="mt-1 px-5 py-2 bg-indigo-600 text-white border-0 rounded-md text-sm font-semibold cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed hover:enabled:bg-indigo-700"
      :disabled="!valid || submitting"
      @click="submit"
    >
      {{ submitting ? 'Releasing…' : 'Confirm Release' }}
    </button>
  </div>
</template>
