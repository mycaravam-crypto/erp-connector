<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { getSourceSchema, type SourceTable } from '@/api/connection'

const router = useRouter()

const schema = ref<{ connectionLabel: string; tables: SourceTable[] } | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)
const expandedTables = ref<Set<string>>(new Set())

async function load() {
  loading.value = true
  error.value = null
  try {
    schema.value = await getSourceSchema()
    if (!schema.value) {
      error.value = 'Source schema endpoint returned no data.'
    } else {
      expandedTables.value = new Set(schema.value.tables.map((t) => t.name))
    }
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    loading.value = false
  }
}

onMounted(load)

function toggleTable(name: string) {
  const next = new Set(expandedTables.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  expandedTables.value = next
}
</script>

<template>
  <div class="page">
    <div class="step-header">
      <span class="step-badge">Step 2</span>
      <h1>Source Schema</h1>
      <button class="refresh-btn" :disabled="loading" @click="load">Refresh</button>
    </div>

    <p class="intro">
      The connector has read the schema from your source database. Review the tables and columns
      below before configuring the export mapping.
    </p>

    <p v-if="loading" class="info">Reading schema…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <template v-else-if="schema">
      <div class="conn-label">
        <span class="conn-icon">⟳</span>
        <span>{{ schema.connectionLabel }}</span>
        <span class="table-count">{{ schema.tables.length }} tables</span>
      </div>

      <div class="table-list">
        <div v-for="table in schema.tables" :key="table.name" class="table-card">
          <button class="table-header" @click="toggleTable(table.name)">
            <span class="expand-icon">{{ expandedTables.has(table.name) ? '▼' : '▶' }}</span>
            <code class="table-name">{{ table.name }}</code>
            <span class="table-desc">{{ table.description }}</span>
            <span class="col-count">{{ table.columns.length }} columns</span>
          </button>

          <div v-if="expandedTables.has(table.name)" class="columns-grid">
            <table>
              <thead>
                <tr>
                  <th>Column</th>
                  <th>Type</th>
                  <th>Nullable</th>
                  <th>PK</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="col in table.columns" :key="col.name" :class="{ 'row-pk': col.primaryKey }">
                  <td>
                    <code class="col-name">{{ col.name }}</code>
                    <span v-if="col.primaryKey" class="pk-badge">PK</span>
                  </td>
                  <td><span class="type-pill">{{ col.type }}</span></td>
                  <td>
                    <span :class="col.nullable ? 'null-yes' : 'null-no'">
                      {{ col.nullable ? 'YES' : 'NO' }}
                    </span>
                  </td>
                  <td>
                    <span v-if="col.primaryKey" class="pk-dot" title="Primary key">●</span>
                    <span v-else class="no-pk">—</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div class="nav-actions">
        <button class="btn-back" @click="router.push({ name: 'connect' })">← Back to Connect</button>
        <button class="btn-next" @click="router.push({ name: 'export-schema' })">
          Configure Export Schema →
        </button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.page {
  max-width: 860px;
}

.step-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
}

.step-badge {
  background: #1a1a2e;
  color: #e2e8f0;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  flex-shrink: 0;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
  flex: 1;
}

.refresh-btn {
  padding: 0.25rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  font-size: 0.8rem;
  cursor: pointer;
  color: #475569;
}

.refresh-btn:disabled { opacity: 0.5; cursor: not-allowed; }

.intro {
  color: #475569;
  font-size: 0.9rem;
  margin: 0.5rem 0 1.25rem;
  line-height: 1.6;
}

.conn-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 0.9rem;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 0.375rem;
  font-size: 0.85rem;
  color: #475569;
  margin-bottom: 1.25rem;
}

.conn-icon { color: #4f46e5; font-weight: 700; }

.table-count {
  margin-left: auto;
  font-size: 0.78rem;
  background: #e0e7ff;
  color: #3730a3;
  padding: 0.1rem 0.45rem;
  border-radius: 9999px;
  font-weight: 600;
}

.table-list {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1.5rem;
}

.table-card {
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
  overflow: hidden;
}

.table-header {
  width: 100%;
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.65rem 0.9rem;
  background: #f8fafc;
  border: none;
  cursor: pointer;
  text-align: left;
  font-size: 0.875rem;
}

.table-header:hover { background: #f1f5f9; }

.expand-icon {
  font-size: 0.6rem;
  color: #64748b;
  flex-shrink: 0;
}

.table-name {
  font-size: 0.9rem;
  font-weight: 700;
  color: #1e293b;
  flex-shrink: 0;
}

.table-desc {
  color: #64748b;
  font-size: 0.82rem;
  flex: 1;
}

.col-count {
  font-size: 0.75rem;
  color: #94a3b8;
  flex-shrink: 0;
}

.columns-grid {
  padding: 0 0.5rem 0.5rem;
  border-top: 1px solid #e2e8f0;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.84rem;
}

th, td {
  padding: 0.4rem 0.65rem;
  text-align: left;
  border-bottom: 1px solid #f1f5f9;
  vertical-align: middle;
}

th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.72rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #64748b;
}

.row-pk td { background: #f0fdf4; }

.col-name { color: #1e293b; font-size: 0.85rem; }

.pk-badge {
  margin-left: 0.35rem;
  font-size: 0.65rem;
  font-weight: 700;
  background: #dbeafe;
  color: #1d4ed8;
  padding: 0.1rem 0.35rem;
  border-radius: 0.2rem;
}

.type-pill {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  border-radius: 0.25rem;
  font-size: 0.75rem;
  color: #334155;
  white-space: nowrap;
}

.null-yes { color: #94a3b8; font-size: 0.78rem; }
.null-no  { color: #1e293b; font-weight: 600; font-size: 0.78rem; }

.pk-dot  { color: #1d4ed8; font-size: 0.9rem; }
.no-pk   { color: #cbd5e1; }

.nav-actions {
  display: flex;
  justify-content: space-between;
  margin-top: 1rem;
}

.btn-back {
  padding: 0.45rem 1rem;
  border: 1px solid #cbd5e1;
  border-radius: 0.375rem;
  background: #fff;
  color: #475569;
  font-size: 0.875rem;
  cursor: pointer;
}

.btn-back:hover { background: #f1f5f9; }

.btn-next {
  padding: 0.45rem 1.25rem;
  border: none;
  border-radius: 0.375rem;
  background: #1a1a2e;
  color: #e2e8f0;
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-next:hover { background: #2d2d4e; }

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
