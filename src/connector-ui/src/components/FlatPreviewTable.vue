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
  <div class="overflow-x-auto max-h-80 overflow-y-auto border border-slate-200 rounded-md">
    <table class="w-full border-collapse text-sm">
      <thead class="sticky top-0">
        <tr>
          <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">#</th>
          <th v-for="col in columns" :key="col" class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.7rem] uppercase tracking-wide border-b border-slate-200 whitespace-nowrap">{{ col }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(rec, idx) in rows" :key="idx" class="hover:bg-slate-50">
          <td class="px-2.5 py-1.5 border-b border-slate-200 align-middle text-center text-slate-400 text-xs w-8">{{ idx + 1 }}</td>
          <td v-for="col in columns" :key="col" class="px-2.5 py-1.5 border-b border-slate-200 align-middle whitespace-nowrap">{{ cellVal(rec, col) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
  <p v-if="props.totalCount > props.max" class="text-xs text-slate-400 mt-1.5">
    Showing first {{ props.max }} rows — preview is capped at 50; the full export includes all in-scope records.
  </p>
</template>
