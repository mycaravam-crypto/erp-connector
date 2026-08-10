<script setup lang="ts">
import type { ErpCiRecord } from '@/api/erp'

defineProps<{
  record: ErpCiRecord
  showParentSerial?: boolean
}>()
</script>

<template>
  <div class="text-xs space-y-1.5">
    <p><span class="text-slate-500 w-36 inline-block">ERP ID (GUID)</span><code class="font-mono">{{ record.id }}</code></p>
    <p><span class="text-slate-500 w-36 inline-block">Commission date</span>{{ record.commissionDate ?? '—' }}</p>
    <p><span class="text-slate-500 w-36 inline-block">Maint. plan status</span>{{ record.maintenancePlanStatus ?? '—' }}</p>
    <p><span class="text-slate-500 w-36 inline-block">Allocation chart ref</span>{{ record.allocationChartRef ?? '—' }}</p>
    <p v-if="showParentSerial"><span class="text-slate-500 w-36 inline-block">Parent serial</span>{{ record.parentSerial ?? '—' }}</p>
    <div class="mt-2 pt-2 border-t border-indigo-200 space-y-1">
      <p class="text-slate-400 font-medium mb-1">Excluded from export:</p>
      <p>
        <span class="text-slate-500 w-36 inline-block">Technician name</span>
        <span class="text-slate-700">{{ record.technicianName ?? '—' }}</span>
        <span class="ml-2 text-[0.65rem] bg-red-100 text-red-700 px-1.5 py-0.5 rounded uppercase font-bold">GDPR</span>
      </p>
      <p>
        <span class="text-slate-500 w-36 inline-block">Storage location</span>
        <span class="text-slate-700">{{ record.storageLocation ?? '—' }}</span>
        <span class="ml-2 text-[0.65rem] bg-amber-100 text-amber-700 px-1.5 py-0.5 rounded uppercase font-bold">Open Point #4</span>
      </p>
    </div>
  </div>
</template>
