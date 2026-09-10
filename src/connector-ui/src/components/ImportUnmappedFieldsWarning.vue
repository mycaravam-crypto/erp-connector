<script setup lang="ts">
// Split out of ImportDefinitionPreviewPanel.vue purely to keep that panel's own template under fallow's
// complexity gate — see findUnmappedRootFields (lib/importPreviewHints.ts) for what this actually warns
// about. Renders nothing when there's nothing to warn about, so the caller can drop it in unconditionally
// alongside the (mutually exclusive) "looks like an exported job file" alert.
import Alert from '@/components/ui/Alert.vue'

defineProps<{ fields: string[] }>()
</script>

<template>
  <Alert v-if="fields.length > 0" variant="warning" class="mb-3">
    {{ fields.length === 1 ? 'This field is' : 'These fields are' }} not mapped as writable
    columns on this definition, so edits to {{ fields.length === 1 ? 'it' : 'them' }} in your
    sample will never show up below:
    <template v-for="(field, i) in fields" :key="field"><code>{{ field }}</code><span v-if="i < fields.length - 1">, </span></template>.
  </Alert>
</template>
