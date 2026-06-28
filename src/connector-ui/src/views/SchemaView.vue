<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { getSchema, type SchemaDefinition } from '@/api/erp'

const schema = ref<SchemaDefinition | null>(null)
const error = ref<string | null>(null)
const loading = ref(true)

async function load() {
  loading.value = true
  error.value = null
  try {
    schema.value = await getSchema()
    if (!schema.value) error.value = 'Schema endpoint returned no data.'
  } catch {
    error.value = 'Could not reach the API. Is the backend running on :5189?'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <div class="toolbar">
      <h1>Export Schema</h1>
      <button @click="load" :disabled="loading">Refresh</button>
    </div>

    <p v-if="loading" class="info">Loading…</p>
    <p v-else-if="error" class="error">{{ error }}</p>

    <template v-else-if="schema">
      <div class="version-row">
        <span class="version-label">Schema Version</span>
        <span class="version-badge">v{{ schema.version }}</span>
        <span class="version-note">
          Sent in every export manifest — the vendor's Transform Map rejects unknown versions.
        </span>
      </div>

      <h2>Active Export Columns</h2>
      <p class="section-desc">
        These {{ schema.columns.length }} columns appear in every exported Excel file,
        in this order. All are required by the ICD.
      </p>

      <table>
        <thead>
          <tr>
            <th>#</th>
            <th>Export Column</th>
            <th>ERP Source</th>
            <th>Type / Format</th>
            <th>Notes</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(col, idx) in schema.columns" :key="col.name">
            <td class="col-idx">{{ idx + 1 }}</td>
            <td><code class="col-name">{{ col.name }}</code></td>
            <td class="erp-source">{{ col.erpSource }}</td>
            <td><span class="type-pill">{{ col.type }}</span></td>
            <td class="notes">{{ col.notes }}</td>
          </tr>
        </tbody>
      </table>

      <div class="pending-section">
        <h2>Pending / Not Yet In Scope</h2>
        <p class="section-desc">
          These fields exist in the ERP but are not exported. Each requires an explicit
          ICD change before it can be added.
        </p>

        <table>
          <thead>
            <tr>
              <th>ERP Field</th>
              <th>Potential Export Column</th>
              <th>ServiceNow Target</th>
              <th>Blocker</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><code>storagelocation.location_id</code></td>
              <td><code>location_id</code></td>
              <td><code>cmdb_ci_hardware.location</code></td>
              <td>
                <span class="blocker-pill">Open Point #4</span>
                Vendor entitlement not confirmed — excluded until ICD is updated
              </td>
            </tr>
            <tr>
              <td><code>systemconfiguration.technician_name</code></td>
              <td>—</td>
              <td>N/A</td>
              <td>
                <span class="blocker-pill blocker-gdpr">GDPR Art. 5(1)(c)</span>
                Personal data — will never appear in the export in current scope
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="coalesce-note">
        <h2>Coalesce Key</h2>
        <p>
          The vendor's Transform Map uses <code>guid</code> as the coalesce field on both
          <code>cmdb_ci_hardware</code> and <code>alm_hardware</code>. Each daily import
          updates existing records (matched by GUID) rather than creating duplicates.
          The GUID is the PostgreSQL primary key of <code>systemconfiguration</code> —
          it is stable for the entity lifetime and always present (non-null).
        </p>
      </div>
    </template>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
}

h1 {
  margin: 0;
  font-size: 1.25rem;
}

h2 {
  font-size: 1rem;
  font-weight: 600;
  margin: 1.5rem 0 0.4rem;
  color: #1e293b;
}

.version-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
  padding: 0.75rem 1rem;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 0.5rem;
}

.version-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.version-badge {
  background: #1a1a2e;
  color: #e2e8f0;
  padding: 0.2rem 0.6rem;
  border-radius: 9999px;
  font-size: 0.8rem;
  font-weight: 700;
}

.version-note {
  font-size: 0.82rem;
  color: #64748b;
}

.section-desc {
  font-size: 0.85rem;
  color: #64748b;
  margin: 0 0 0.75rem;
}

table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
}

th, td {
  padding: 0.5rem 0.75rem;
  text-align: left;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: top;
}

th {
  background: #f8fafc;
  font-weight: 600;
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  white-space: nowrap;
}

.col-idx {
  color: #94a3b8;
  font-size: 0.8rem;
  text-align: center;
  width: 2rem;
}

.col-name {
  font-size: 0.85rem;
  font-weight: 600;
  color: #1e293b;
}

.erp-source {
  font-size: 0.82rem;
  color: #475569;
  font-style: italic;
}

.type-pill {
  display: inline-block;
  padding: 0.1rem 0.45rem;
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  border-radius: 0.25rem;
  font-size: 0.75rem;
  color: #334155;
  white-space: nowrap;
}

.notes {
  font-size: 0.82rem;
  color: #475569;
}

.pending-section {
  margin-top: 2rem;
  padding-top: 1.5rem;
  border-top: 1px solid #e2e8f0;
}

.blocker-pill {
  display: inline-block;
  padding: 0.1rem 0.4rem;
  border-radius: 0.25rem;
  font-size: 0.72rem;
  font-weight: 600;
  background: #fef9c3;
  color: #854d0e;
  margin-right: 0.4rem;
}

.blocker-gdpr {
  background: #fee2e2;
  color: #991b1b;
}

.coalesce-note {
  margin-top: 2rem;
  padding: 1rem 1.25rem;
  background: #f0fdf4;
  border: 1px solid #bbf7d0;
  border-radius: 0.5rem;
}

.coalesce-note h2 {
  margin-top: 0;
  color: #166534;
}

.coalesce-note p {
  font-size: 0.875rem;
  color: #15803d;
  margin: 0;
  line-height: 1.6;
}

.info  { color: #64748b; }
.error { color: #dc2626; }
</style>
