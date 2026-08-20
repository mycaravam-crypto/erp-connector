<script setup lang="ts">
import { ref } from 'vue'
import { deliverExport } from '@/api/exports'

const props = defineProps<{ seqNo: number }>()
const emit = defineEmits<{ (e: 'delivered'): void }>()

const importCount = ref<number | null>(null)
const notes = ref('')
const submitting = ref(false)
const error = ref<string | null>(null)

async function submit() {
  submitting.value = true
  error.value = null
  const result = await deliverExport(props.seqNo, {
    importedRecordCount: importCount.value,
    notes: notes.value.trim() || null,
  })
  submitting.value = false
  if (result.ok) {
    emit('delivered')
  } else {
    error.value = result.message || `Error ${result.status}`
  }
}
</script>

<template>
  <div class="delivery-card border border-slate-200 rounded-lg px-6 py-5 max-w-sm mt-6">
    <h2 class="m-0 mb-1 text-base font-semibold">Record Physical Delivery</h2>
    <p class="text-slate-500 text-sm m-0 mb-4 leading-relaxed">
      After handing the export file to the vendor, record the delivery here to close the
      custody chain. Import count is optional — enter it when the vendor confirms.
    </p>

    <div class="flex flex-col gap-1 mb-3">
      <label for="import-count" class="text-sm font-semibold">Vendor import count (optional)</label>
      <input
        id="import-count"
        v-model.number="importCount"
        type="number"
        min="0"
        placeholder="e.g. 5"
        class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100"
      />
    </div>

    <div class="flex flex-col gap-1 mb-3">
      <div class="flex items-baseline justify-between">
        <label for="delivery-notes" class="text-sm font-semibold">Notes (optional)</label>
        <span class="text-xs text-slate-400">{{ notes.length }}/2000</span>
      </div>
      <textarea
        id="delivery-notes"
        v-model="notes"
        maxlength="2000"
        rows="3"
        placeholder="e.g. USB-007, handed to J. Smith"
        class="px-2.5 py-1.5 border border-slate-300 rounded-md text-sm outline-none focus:border-indigo-500 focus:ring-2 focus:ring-indigo-100 resize-y"
      />
    </div>

    <p v-if="error" class="text-red-600 text-sm mb-2">{{ error }}</p>

    <button
      class="mt-1 px-5 py-2 bg-indigo-600 text-white border-0 rounded-md text-sm font-semibold cursor-pointer disabled:opacity-45 disabled:cursor-not-allowed hover:enabled:bg-indigo-700"
      :disabled="submitting"
      @click="submit"
    >{{ submitting ? 'Recording…' : 'Mark as Delivered' }}</button>
  </div>
</template>
