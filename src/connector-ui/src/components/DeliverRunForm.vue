<script setup lang="ts">
import { ref } from 'vue'
import { deliverExport } from '@/api/exports'
import Card from '@/components/ui/Card.vue'
import Input from '@/components/ui/Input.vue'
import TextField from '@/components/ui/TextField.vue'
import Button from '@/components/ui/Button.vue'

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
  <Card class="delivery-card max-w-sm mt-6">
    <h2 class="m-0 mb-1 text-base font-semibold text-text-primary">Record Physical Delivery</h2>
    <p class="text-text-secondary text-sm m-0 mb-4 leading-relaxed">
      After handing the export file to the vendor, record the delivery here to close the
      custody chain. Import count is optional — enter it when the vendor confirms.
    </p>

    <Input
      id="import-count"
      :model-value="importCount === null ? '' : String(importCount)"
      type="number"
      label="Vendor import count (optional)"
      placeholder="e.g. 5"
      class="mb-3"
      @update:model-value="(v) => (importCount = v === '' ? null : Number(v))"
    />

    <div class="flex flex-col gap-1 mb-3">
      <div class="flex items-baseline justify-between">
        <label for="delivery-notes" class="text-sm font-semibold text-text-primary">Notes (optional)</label>
        <span class="text-xs text-text-muted">{{ notes.length }}/2000</span>
      </div>
      <TextField id="delivery-notes" v-model="notes" :maxlength="2000" placeholder="e.g. USB-007, handed to J. Smith" />
    </div>

    <p v-if="error" class="text-danger text-sm mb-2">{{ error }}</p>

    <Button :disabled="submitting" @click="submit">{{ submitting ? 'Recording…' : 'Mark as Delivered' }}</Button>
  </Card>
</template>
