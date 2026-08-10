<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { listErpRecords, type ErpCiRecord } from '@/api/erp'
import FlatRecordsTable, { type SortKey } from '@/components/FlatRecordsTable.vue'
import BomTree from '@/components/BomTree.vue'

const records = ref<ErpCiRecord[]>([])
const totalCount = ref(0)
const loading = ref(true)
const error = ref<string | null>(null)

// Search and filter state
const search = ref('')
const scopeFilter = ref<'all' | 'in-scope' | 'excluded'>('all')
const expandedIds = ref<Set<string>>(new Set())
const detailId = ref<string | null>(null)

onMounted(async () => {
  try {
    const result = await listErpRecords()
    records.value = result.records
    totalCount.value = result.total
  } catch {
    error.value = 'Could not reach the API. Is the backend service running?'
  } finally {
    loading.value = false
  }
})

// Flat-list mode activates when search or scope filter is active
const isFlatMode = computed(() => search.value.trim() !== '' || scopeFilter.value !== 'all')

const filtered = computed(() => {
  let list = records.value

  if (scopeFilter.value === 'in-scope') list = list.filter((r) => r.inScope)
  else if (scopeFilter.value === 'excluded') list = list.filter((r) => !r.inScope)

  const q = search.value.trim().toLowerCase()
  if (q) {
    list = list.filter((r) =>
      [r.serial, r.articleName, r.partNumber, r.manufacturer, r.id]
        .some((v) => v?.toLowerCase().includes(q)),
    )
  }

  return list
})

// BOM tree: roots are records with no parentId (among visible records)
const visibleIds = computed(() => new Set(filtered.value.map((r) => r.id)))
const roots = computed(() =>
  filtered.value.filter((r) => !r.parentId || !visibleIds.value.has(r.parentId)),
)
const childrenOf = computed(() => {
  const map = new Map<string, ErpCiRecord[]>()
  for (const r of filtered.value) {
    if (r.parentId && visibleIds.value.has(r.parentId)) {
      const list = map.get(r.parentId) ?? []
      list.push(r)
      map.set(r.parentId, list)
    }
  }
  return map
})

function toggleExpand(id: string) {
  if (expandedIds.value.has(id)) expandedIds.value.delete(id)
  else expandedIds.value.add(id)
  // Re-assign to trigger reactivity
  expandedIds.value = new Set(expandedIds.value)
}

function toggleDetail(id: string) {
  detailId.value = detailId.value === id ? null : id
}

function detailFor(id: string): ErpCiRecord | undefined {
  return records.value.find((r) => r.id === id)
}

const inScopeCount = computed(() => records.value.filter((r) => r.inScope).length)
const excludedCount = computed(() => records.value.filter((r) => !r.inScope).length)

// Sorting for flat list
const sortKey = ref<SortKey>('serial')
const sortAsc = ref(true)

function setSort(key: SortKey) {
  if (sortKey.value === key) sortAsc.value = !sortAsc.value
  else { sortKey.value = key; sortAsc.value = true }
}

const sortedFlat = computed(() => {
  const list = [...filtered.value]
  const dir = sortAsc.value ? 1 : -1
  list.sort((a, b) => {
    const av = String(a[sortKey.value] ?? '')
    const bv = String(b[sortKey.value] ?? '')
    return av.localeCompare(bv) * dir
  })
  return list
})
</script>

<template>
  <div>
    <div class="flex items-center gap-3 mb-2">
      <h1 class="m-0 text-xl font-semibold">ERP Database</h1>
      <span v-if="!loading && !error" class="text-xs text-slate-500">
        {{ totalCount }} CIs total ·
        <span class="text-green-700">{{ inScopeCount }} in scope</span> ·
        <span class="text-slate-500">{{ excludedCount }} excluded</span>
      </span>
    </div>

    <p class="text-sm text-slate-500 mb-4">
      Live view of the ERP database — shows every CI, its scope status, and the excluded fields
      that confirm the data-minimisation boundary.
    </p>

    <!-- Cap notice: shown when the API returned fewer records than the total -->
    <div
      v-if="!loading && !error && records.length < totalCount"
      class="bg-amber-50 border border-amber-200 rounded-md px-4 py-2.5 text-sm text-amber-900 mb-4"
    >
      Showing {{ records.length }} of {{ totalCount }} CIs — the view is capped at 500.
      Search or filter to narrow results within the loaded set.
    </div>

    <!-- Controls -->
    <div v-if="!loading && !error" class="flex flex-wrap items-center gap-3 mb-4">
      <input
        v-model="search"
        type="search"
        placeholder="Search serial, model, part, manufacturer, ID…"
        class="border border-slate-300 rounded-md px-3 py-1.5 text-sm w-72 focus:outline-none focus:ring-2 focus:ring-indigo-400"
      />
      <div class="flex rounded-md border border-slate-300 overflow-hidden text-sm">
        <button
          v-for="opt in [['all', 'All'], ['in-scope', 'In Scope'], ['excluded', 'Excluded']] as const"
          :key="opt[0]"
          class="px-3 py-1.5 transition-colors"
          :class="scopeFilter === opt[0]
            ? 'bg-indigo-600 text-white'
            : 'bg-white text-slate-600 hover:bg-slate-50'"
          @click="scopeFilter = opt[0]"
        >
          {{ opt[1] }}
        </button>
      </div>
      <span v-if="isFlatMode" class="text-xs text-slate-400">Flat list mode · {{ filtered.length }} results</span>
    </div>

    <div v-if="loading" class="text-slate-500 text-sm">Loading ERP records…</div>
    <div v-else-if="error" class="bg-red-50 border border-red-200 rounded-lg px-4 py-3 text-sm text-red-800">{{ error }}</div>
    <div v-else-if="filtered.length === 0" class="text-slate-500 text-sm">No records match the current filter.</div>

    <!-- ── Flat list mode ─────────────────────────────────────────────────────── -->
    <FlatRecordsTable
      v-else-if="isFlatMode"
      :records="sortedFlat"
      :sort-key="sortKey"
      :sort-asc="sortAsc"
      :detail-id="detailId"
      @sort="setSort"
      @toggle-detail="toggleDetail"
    />

    <!-- ── BOM tree mode ──────────────────────────────────────────────────────── -->
    <BomTree
      v-else
      :roots="roots"
      :children-of="childrenOf"
      :expanded-ids="expandedIds"
      :detail-id="detailId"
      @toggle-expand="toggleExpand"
      @toggle-detail="toggleDetail"
    />
  </div>
</template>
