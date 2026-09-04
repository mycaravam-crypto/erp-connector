<script setup lang="ts">
import type { SourceColumn } from '@/api/connection'
import { Key } from 'lucide-vue-next'
import Icon from '@/components/ui/Icon.vue'

defineProps<{
  columns: SourceColumn[]
}>()
</script>

<template>
  <table class="w-full border-collapse text-sm">
    <thead>
      <tr>
        <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide text-text-secondary border-b border-border">Column</th>
        <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide text-text-secondary border-b border-border">Type</th>
        <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide text-text-secondary border-b border-border">Nullable</th>
        <th class="px-3 py-2 text-left bg-surface-elevated font-semibold text-[0.7rem] uppercase tracking-wide text-text-secondary border-b border-border">PK</th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="col in columns"
        :key="col.name"
        class="border-b border-border last:border-0 transition-colors"
        :class="col.primaryKey ? 'bg-success-bg hover:bg-success-bg' : 'hover:bg-surface-elevated'"
      >
        <td class="px-3 py-2 align-middle">
          <code class="text-sm text-text-primary">{{ col.name }}</code>
          <span v-if="col.primaryKey" class="ml-1.5 text-[0.65rem] font-bold bg-info-bg text-info px-1.5 py-0.5 rounded">PK</span>
        </td>
        <td class="px-3 py-2 align-middle">
          <span class="inline-block px-1.5 py-0.5 bg-surface-elevated border border-border rounded text-xs text-text-secondary whitespace-nowrap">{{ col.type }}</span>
        </td>
        <td class="px-3 py-2 align-middle">
          <span :class="col.nullable ? 'text-text-muted text-xs' : 'text-text-primary font-semibold text-xs'">
            {{ col.nullable ? 'YES' : 'NO' }}
          </span>
        </td>
        <td class="px-3 py-2 align-middle">
          <span v-if="col.primaryKey" class="text-info"><Icon :icon="Key" :size="16" /></span>
          <span v-else class="text-text-muted">—</span>
        </td>
      </tr>
    </tbody>
  </table>
</template>
