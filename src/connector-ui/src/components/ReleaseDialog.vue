<script setup lang="ts">
import { ref, computed } from 'vue'
import { releaseExport } from '@/api/exports'

const props = defineProps<{ seqNo: number }>()
const emit = defineEmits<{ (e: 'released'): void }>()

const operator = ref('')
const approver = ref('')
const submitting = ref(false)
const serverError = ref<string | null>(null)

const sameUser = computed(
  () =>
    operator.value.trim() !== '' &&
    operator.value.trim().toLowerCase() === approver.value.trim().toLowerCase(),
)

const valid = computed(
  () => operator.value.trim() !== '' && approver.value.trim() !== '' && !sameUser.value,
)

async function submit() {
  if (!valid.value) return
  submitting.value = true
  serverError.value = null

  const result = await releaseExport(props.seqNo, {
    operator: operator.value.trim(),
    approver: approver.value.trim(),
  })

  submitting.value = false

  if (result.ok) {
    emit('released')
  } else {
    serverError.value = result.message || `Error ${result.status}`
  }
}
</script>

<template>
  <div class="release-dialog">
    <h2>Four-Eyes Release — Run #{{ seqNo }}</h2>
    <p class="hint">Operator and approver must be different people.</p>

    <div class="field">
      <label for="operator">Operator</label>
      <input id="operator" v-model="operator" placeholder="Operator name" autocomplete="off" />
    </div>

    <div class="field">
      <label for="approver">Approver</label>
      <input id="approver" v-model="approver" placeholder="Approver name" autocomplete="off" />
    </div>

    <p v-if="sameUser" class="error">Operator and approver must be different people.</p>
    <p v-if="serverError" class="error">{{ serverError }}</p>

    <button @click="submit" :disabled="!valid || submitting">
      {{ submitting ? 'Releasing…' : 'Confirm Release' }}
    </button>
  </div>
</template>

<style scoped>
.release-dialog {
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  padding: 1.5rem;
  max-width: 420px;
  margin-top: 1.5rem;
}

h2 {
  margin: 0 0 0.25rem;
  font-size: 1.1rem;
}

.hint {
  color: #64748b;
  font-size: 0.85rem;
  margin: 0 0 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
  margin-bottom: 0.75rem;
}

label {
  font-size: 0.85rem;
  font-weight: 600;
}

input {
  padding: 0.4rem 0.6rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  outline: none;
}

input:focus {
  border-color: #6366f1;
  box-shadow: 0 0 0 2px #e0e7ff;
}

button {
  margin-top: 0.5rem;
  padding: 0.45rem 1.2rem;
  background: #4f46e5;
  color: #fff;
  border: none;
  border-radius: 0.375rem;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

button:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.error {
  color: #dc2626;
  font-size: 0.85rem;
  margin: 0.25rem 0;
}
</style>
