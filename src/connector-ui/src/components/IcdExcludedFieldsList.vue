<script setup lang="ts">
defineProps<{
  fields: { erpSource: string; reason: string; detail: string; tag: string }[]
}>()
</script>

<template>
  <div class="space-y-3">
    <div
      v-for="field in fields"
      :key="field.erpSource"
      class="flex gap-3 items-start rounded-lg border px-4 py-3 text-sm"
      :class="field.tag === 'gdpr' ? 'border-danger/25 bg-danger-bg' : 'border-warning/25 bg-warning-bg'"
    >
      <span
        class="shrink-0 text-[0.68rem] font-bold px-1.5 py-0.5 rounded uppercase tracking-wide mt-0.5"
        :class="field.tag === 'gdpr' ? 'bg-danger text-white' : 'bg-warning text-white'"
      >
        {{ field.reason }}
      </span>
      <div>
        <code class="font-mono text-text-primary">{{ field.erpSource }}</code>
        <p class="mt-0.5 text-text-secondary">{{ field.detail }}</p>
      </div>
    </div>
  </div>
</template>
