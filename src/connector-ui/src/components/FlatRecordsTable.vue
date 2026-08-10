<script setup lang="ts">
import type { ErpCiRecord } from '@/api/erp'
import CiDetailPanel from '@/components/CiDetailPanel.vue'

export type SortKey = 'serial' | 'articleName' | 'partNumber' | 'manufacturer' | 'status' | 'inScope'

const props = defineProps<{
  records: ErpCiRecord[]
  sortKey: SortKey
  sortAsc: boolean
  detailId: string | null
}>()
const emit = defineEmits<{
  (e: 'sort', key: SortKey): void
  (e: 'toggleDetail', id: string): void
}>()

function sortIcon(key: SortKey) {
  if (props.sortKey !== key) return '↕'
  return props.sortAsc ? '↑' : '↓'
}
</script>

<template>
  <div class="rounded-lg border border-slate-200 overflow-hidden text-sm">
    <table class="w-full">
      <thead>
        <tr class="bg-slate-50 border-b border-slate-200">
          <th
            v-for="[key, label] in [['serial','Serial'], ['articleName','Model'], ['partNumber','Part #'], ['manufacturer','Manufacturer'], ['status','Status'], ['inScope','Scope']]"
            :key="key"
            class="text-left px-3 py-2 text-slate-600 font-medium cursor-pointer select-none hover:bg-slate-100"
            @click="emit('sort', key as SortKey)"
          >
            {{ label }} <span class="text-slate-400 text-xs">{{ sortIcon(key as SortKey) }}</span>
          </th>
        </tr>
      </thead>
      <tbody>
        <template v-for="r in records" :key="r.id">
          <tr
            class="border-b border-slate-100 last:border-0 cursor-pointer hover:bg-slate-50 transition-colors"
            :class="detailId === r.id ? 'bg-indigo-50' : ''"
            @click="emit('toggleDetail', r.id)"
          >
            <td class="px-3 py-2 font-mono text-xs">{{ r.serial ?? '—' }}</td>
            <td class="px-3 py-2">{{ r.articleName ?? '—' }}</td>
            <td class="px-3 py-2 font-mono text-xs">{{ r.partNumber ?? '—' }}</td>
            <td class="px-3 py-2">{{ r.manufacturer ?? '—' }}</td>
            <td class="px-3 py-2">{{ r.status ?? '—' }}</td>
            <td class="px-3 py-2">
              <span
                class="inline-flex items-center text-xs font-medium px-2 py-0.5 rounded-full"
                :class="r.inScope ? 'bg-green-100 text-green-800' : 'bg-slate-100 text-slate-500'"
              >
                {{ r.inScope ? 'In Scope' : r.exclusionReason ?? 'Excluded' }}
              </span>
            </td>
          </tr>
          <!-- Detail panel -->
          <tr v-if="detailId === r.id" class="bg-indigo-50 border-b border-slate-100">
            <td colspan="6" class="px-4 py-3">
              <CiDetailPanel :record="r" show-parent-serial />
            </td>
          </tr>
        </template>
      </tbody>
    </table>
  </div>
</template>
