<script setup lang="ts">
import type { AuditEntry } from '@/api/audit'
import { formatDate } from '@/lib/dates'

defineProps<{
  entries: AuditEntry[]
}>()
</script>

<template>
  <table class="w-full text-sm border-collapse">
    <thead>
      <tr class="text-left text-text-secondary border-b border-border">
        <th class="px-3 py-2 font-semibold">Timestamp</th>
        <th class="px-3 py-2 font-semibold">User</th>
        <th class="px-3 py-2 font-semibold">Action</th>
        <th class="px-3 py-2 font-semibold">Detail</th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="entry in entries"
        :key="entry.id"
        class="border-b border-border hover:bg-surface-elevated"
      >
        <td class="px-3 py-2 whitespace-nowrap text-text-secondary">{{ formatDate(entry.timestamp) }}</td>
        <td class="px-3 py-2 font-medium text-text-primary">{{ entry.username }}</td>
        <td class="px-3 py-2 font-mono text-text-primary">{{ entry.action }}</td>
        <td class="px-3 py-2 text-text-secondary">{{ entry.detail ?? '—' }}</td>
      </tr>
    </tbody>
  </table>
</template>
