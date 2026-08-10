<script setup lang="ts">
import type { ErpCiRecord } from '@/api/erp'
import BomTreeRow from '@/components/BomTreeRow.vue'

defineProps<{
  roots: ErpCiRecord[]
  childrenOf: Map<string, ErpCiRecord[]>
  expandedIds: Set<string>
  detailId: string | null
}>()
const emit = defineEmits<{
  (e: 'toggleExpand', id: string): void
  (e: 'toggleDetail', id: string): void
}>()
</script>

<template>
  <div class="rounded-lg border border-slate-200 overflow-hidden text-sm">
    <table class="w-full">
      <thead>
        <tr class="bg-slate-50 border-b border-slate-200">
          <th class="text-left px-3 py-2 text-slate-600 font-medium">Serial / Model</th>
          <th class="text-left px-3 py-2 text-slate-600 font-medium">Part #</th>
          <th class="text-left px-3 py-2 text-slate-600 font-medium">Status</th>
          <th class="text-left px-3 py-2 text-slate-600 font-medium">Scope</th>
        </tr>
      </thead>
      <tbody>
        <template v-for="root in roots" :key="root.id">
          <BomTreeRow
            :record="root"
            :depth="0"
            :has-children="!!childrenOf.get(root.id)?.length"
            :expanded="expandedIds.has(root.id)"
            :show-detail="detailId === root.id"
            @toggle-expand="emit('toggleExpand', root.id)"
            @toggle-detail="emit('toggleDetail', root.id)"
          />
          <template v-if="expandedIds.has(root.id)">
            <BomTreeRow
              v-for="child in childrenOf.get(root.id)"
              :key="child.id"
              :record="child"
              :depth="1"
              :has-children="false"
              :expanded="false"
              :show-detail="detailId === child.id"
              @toggle-detail="emit('toggleDetail', child.id)"
            />
          </template>
        </template>
      </tbody>
    </table>
  </div>
</template>
