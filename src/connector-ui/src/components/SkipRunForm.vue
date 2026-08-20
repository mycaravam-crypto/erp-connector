<script setup lang="ts">
import { ref } from 'vue'
import { skipExport } from '@/api/exports'

const props = defineProps<{ seqNo: number }>()
const emit = defineEmits<{ (e: 'skipped'): void }>()

const reason = ref('')
const submitting = ref(false)
const error = ref<string | null>(null)

async function submit() {
  submitting.value = true
  error.value = null
  const result = await skipExport(props.seqNo, { reason: reason.value.trim() || null })
  submitting.value = false
  if (result.ok) {
    emit('skipped')
  } else {
    error.value = result.message || `Error ${result.status}`
  }
}
</script>

<template>
  <div class="skip-card border border-slate-200 rounded-lg px-6 py-5 max-w-sm mt-6">
    <h2 class="m-0 mb-1 text-base font-semibold text-slate-700">Skip This Run</h2>
    <p class="text-slate-500 text-sm m-0 mb-4 leading-relaxed">
      Permanently marks this run as Skipped so it no longer blocks sequence gap detection.
      Use this when a run can never be released (e.g. no ERP data at that time, file lost).
    </p>

    <div class="flex flex-col gap-1 mb-3">
      <label for="skip-reason" class="text-sm font-semibold">Reason (optional)</label>
      <input
        id="skip-reason"
        v-model="reason"
        type="text"
        maxlength="200"
        placeholder="e.g. ERP offline during scheduled run"
        class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
      />
    </div>

    <p v-if="error" class="text-red-600 text-sm mb-2">{{ error }}</p>

    <button
      class="px-5 py-2 border border-slate-400 rounded-md bg-white text-slate-700 text-sm font-semibold cursor-pointer hover:enabled:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed"
      :disabled="submitting"
      @click="submit"
    >{{ submitting ? 'Skipping…' : 'Skip Run' }}</button>
  </div>
</template>
