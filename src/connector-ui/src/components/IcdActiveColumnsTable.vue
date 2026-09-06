<script setup lang="ts">
import type { SchemaColumnDef } from '@/api/icdSchema'

defineProps<{
  columns: SchemaColumnDef[]
}>()
</script>

<template>
  <table class="w-full text-sm">
    <thead>
      <tr class="bg-surface-elevated border-b border-border">
        <th class="text-left px-3 py-2 text-text-secondary font-medium w-36">Export column</th>
        <th class="text-left px-3 py-2 text-text-secondary font-medium">ERP source</th>
        <th class="text-left px-3 py-2 text-text-secondary font-medium w-32">Type / format</th>
        <th class="text-left px-3 py-2 text-text-secondary font-medium">Notes</th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="col in columns"
        :key="col.name"
        :class="col.active ? 'bg-surface' : 'bg-surface-elevated opacity-50'"
        class="border-b border-border last:border-0"
      >
        <td class="px-3 py-2 font-mono text-xs text-brand">
          {{ col.exportName ?? col.name }}
          <span v-if="!col.active" class="ml-1 text-[0.65rem] text-text-muted font-sans">(off)</span>
        </td>
        <td class="px-3 py-2 text-text-secondary font-mono text-xs">{{ col.erpSource }}</td>
        <td class="px-3 py-2 text-text-secondary text-xs">{{ col.type }}</td>
        <td class="px-3 py-2 text-text-secondary text-xs">{{ col.notes }}</td>
      </tr>
    </tbody>
  </table>
</template>
