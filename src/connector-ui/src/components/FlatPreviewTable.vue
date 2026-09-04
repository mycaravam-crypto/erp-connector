<script setup lang="ts">
const props = defineProps<{
  columns: string[]
  rows: Record<string, string>[]
  totalCount: number
  max: number
}>()

function cellVal(rec: Record<string, string>, col: string): string {
  return rec[col] ?? '—'
}
</script>

<template>
  <div class="overflow-x-auto max-h-80 overflow-y-auto border border-border rounded-lg">
    <table class="w-full border-collapse text-sm">
      <thead class="sticky top-0">
        <tr>
          <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">#</th>
          <th v-for="col in columns" :key="col" class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide border-b border-border whitespace-nowrap">{{ col }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(rec, idx) in rows" :key="idx" class="border-b border-border last:border-0 hover:bg-surface-elevated transition-colors">
          <td class="px-3 py-2 align-middle text-center text-text-muted text-xs w-8">{{ idx + 1 }}</td>
          <td v-for="col in columns" :key="col" class="px-3 py-2 align-middle whitespace-nowrap">{{ cellVal(rec, col) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
  <p v-if="props.totalCount > props.max" class="text-xs text-text-muted mt-1.5">
    Showing first {{ props.max }} rows — preview is capped at 50; the full export includes all in-scope records.
  </p>
</template>
