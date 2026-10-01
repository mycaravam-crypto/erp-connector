<script setup lang="ts">
import Button from '@/components/ui/Button.vue'

// Shared shell of the per-definition run-history tables (ExportDefinitionRunsTable.vue,
// ImportDefinitionRunsTable.vue): heading with Refresh, loading/error states, the empty message, and the
// table with its header row. Callers supply only the body rows, through the default slot.
defineProps<{
  title: string
  /** Header cells, in order; `numeric` right-aligns the column. */
  columns: { label: string; numeric?: boolean }[]
  loading: boolean
  error: string | null
  empty: boolean
  emptyText: string
}>()
defineEmits<{ refresh: [] }>()

const headerCell =
  'px-3 py-2 bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap'
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-3">
      <h2 class="m-0 text-base font-semibold text-text-primary">{{ title }}</h2>
      <Button variant="secondary" class="ml-auto" :loading="loading" @click="$emit('refresh')">Refresh</Button>
    </div>

    <p v-if="loading" class="text-text-secondary text-sm">Loading…</p>
    <p v-else-if="error" class="text-danger text-sm">{{ error }}</p>

    <div v-else class="rounded-lg border border-border overflow-x-auto">
      <p v-if="empty" class="text-text-secondary text-sm text-center py-6">{{ emptyText }}</p>
      <table v-else class="w-full border-collapse text-sm">
        <thead>
          <tr>
            <th v-for="column in columns" :key="column.label" :class="[headerCell, column.numeric ? 'text-right' : 'text-left']">
              {{ column.label }}
            </th>
          </tr>
        </thead>
        <tbody>
          <slot />
        </tbody>
      </table>
    </div>
  </div>
</template>
