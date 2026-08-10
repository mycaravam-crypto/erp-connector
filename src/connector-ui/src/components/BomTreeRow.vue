<script setup lang="ts">
import type { ErpCiRecord } from '@/api/erp'
import CiDetailPanel from '@/components/CiDetailPanel.vue'

defineProps<{
  record: ErpCiRecord
  depth: 0 | 1
  hasChildren: boolean
  expanded: boolean
  showDetail: boolean
}>()
const emit = defineEmits<{
  (e: 'toggleExpand'): void
  (e: 'toggleDetail'): void
}>()
</script>

<template>
  <tr
    class="border-b border-slate-100 cursor-pointer hover:bg-slate-50 transition-colors"
    :class="showDetail ? 'bg-indigo-50' : ''"
    @click="emit('toggleDetail')"
  >
    <td :class="depth === 0 ? 'px-3 py-2' : 'pl-8 pr-3 py-2'">
      <div class="flex items-center gap-2">
        <template v-if="depth === 0">
          <button
            v-if="hasChildren"
            class="w-5 h-5 flex items-center justify-center border border-slate-300 rounded text-slate-500 text-xs bg-white hover:bg-slate-100 shrink-0"
            @click.stop="emit('toggleExpand')"
          >{{ expanded ? '−' : '+' }}</button>
          <span v-else class="w-5 shrink-0 text-center text-slate-300 text-xs select-none">·</span>
        </template>
        <span v-else class="text-slate-300 text-xs select-none">└</span>
        <div>
          <span class="font-mono text-xs">{{ record.serial ?? '—' }}</span>
          <span v-if="record.articleName" class="text-slate-500 text-xs ml-1.5">{{ record.articleName }}</span>
        </div>
      </div>
    </td>
    <td class="px-3 py-2 font-mono text-xs text-slate-500">{{ record.partNumber ?? '—' }}</td>
    <td class="px-3 py-2 text-xs">{{ record.status ?? '—' }}</td>
    <td class="px-3 py-2">
      <span
        class="inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full"
        :class="record.inScope ? 'bg-green-100 text-green-800' : 'bg-slate-100 text-slate-500'"
      >
        {{ record.inScope ? 'In Scope' : record.exclusionReason ?? 'Excluded' }}
      </span>
    </td>
  </tr>
  <tr v-if="showDetail" class="bg-indigo-50 border-b border-slate-100">
    <td colspan="4" :class="depth === 0 ? 'px-4 py-3' : 'pl-12 pr-4 py-3'">
      <CiDetailPanel :record="record" />
    </td>
  </tr>
</template>
