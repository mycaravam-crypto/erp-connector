<script setup lang="ts">
import { ref } from 'vue'
import { skipExport } from '@/api/exports'
import Card from '@/components/ui/Card.vue'
import Input from '@/components/ui/Input.vue'
import Button from '@/components/ui/Button.vue'

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
  <Card class="skip-card max-w-sm mt-6">
    <h2 class="m-0 mb-1 text-base font-semibold text-text-primary">Skip This Run</h2>
    <p class="text-text-secondary text-sm m-0 mb-4 leading-relaxed">
      Permanently marks this run as Skipped so it no longer blocks sequence gap detection.
      Use this when a run can never be released (e.g. no ERP data at that time, file lost).
    </p>

    <Input
      id="skip-reason"
      v-model="reason"
      :maxlength="200"
      label="Reason (optional)"
      placeholder="e.g. ERP offline during scheduled run"
      class="mb-3"
    />

    <p v-if="error" class="text-danger text-sm mb-2">{{ error }}</p>

    <Button variant="secondary" :disabled="submitting" @click="submit">
      {{ submitting ? 'Skipping…' : 'Skip Run' }}
    </Button>
  </Card>
</template>
