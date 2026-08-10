<script setup lang="ts">
import type { SourceColumn } from '@/api/connection'

defineProps<{
  columns: SourceColumn[]
}>()
</script>

<template>
  <table class="w-full border-collapse text-sm">
    <thead>
      <tr>
        <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">Column</th>
        <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">Type</th>
        <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">Nullable</th>
        <th class="px-2.5 py-2 text-left bg-slate-50 font-semibold text-[0.72rem] uppercase tracking-wide text-slate-500 border-b border-slate-100">PK</th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="col in columns" :key="col.name" :class="col.primaryKey ? 'bg-green-50' : ''">
        <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
          <code class="text-sm text-slate-900">{{ col.name }}</code>
          <span v-if="col.primaryKey" class="ml-1.5 text-[0.65rem] font-bold bg-blue-100 text-blue-800 px-1.5 py-0.5 rounded">PK</span>
        </td>
        <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
          <span class="inline-block px-1.5 py-0.5 bg-slate-100 border border-slate-200 rounded text-xs text-slate-600 whitespace-nowrap">{{ col.type }}</span>
        </td>
        <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
          <span :class="col.nullable ? 'text-slate-400 text-xs' : 'text-slate-900 font-semibold text-xs'">
            {{ col.nullable ? 'YES' : 'NO' }}
          </span>
        </td>
        <td class="px-2.5 py-1.5 border-b border-slate-50 align-middle">
          <span v-if="col.primaryKey" class="text-blue-700 text-sm">●</span>
          <span v-else class="text-slate-200">—</span>
        </td>
      </tr>
    </tbody>
  </table>
</template>
