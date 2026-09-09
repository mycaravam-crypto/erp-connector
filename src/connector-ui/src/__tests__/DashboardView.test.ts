import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import DashboardView from '@/views/DashboardView.vue'
import * as connectionApi from '@/api/connection'
import * as exportDefinitionsApi from '@/api/exportDefinitions'
import * as importDefinitionsApi from '@/api/importDefinitions'
import type { ExportDefinitionSummary } from '@/api/exportDefinitions'
import type { ImportDefinitionSummary } from '@/api/importDefinitions'

async function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'dashboard', component: DashboardView },
      { path: '/connect', name: 'connect', component: { template: '<div/>' } },
      { path: '/export-schema', name: 'export-schema', component: { template: '<div/>' } },
      { path: '/exports', name: 'exports', component: { template: '<div/>' } },
      { path: '/export-definitions', name: 'export-definitions', component: { template: '<div/>' } },
      { path: '/import-definitions', name: 'import-definitions', component: { template: '<div/>' } },
      { path: '/source-schema', name: 'source-schema', component: { template: '<div/>' } },
      { path: '/icd-schema', name: 'icd-schema', component: { template: '<div/>' } },
    ],
  })
  await r.push('/')
  return r
}

const exportDef = (isEnabled: boolean): ExportDefinitionSummary => ({
  id: 1,
  name: 'Export',
  description: null,
  rootTable: 'masterdata',
  outputFormat: 'json',
  isEnabled,
  schedule: null,
  configVersion: 1,
  createdBy: 'x',
  createdAt: '2026-08-01T00:00:00Z',
  updatedBy: null,
  updatedAt: null,
})

const importDef = (isEnabled: boolean): ImportDefinitionSummary => ({
  id: 1,
  name: 'Import',
  description: null,
  rootTable: 'masterdata',
  unmatchedRootPolicy: 'reject',
  isEnabled,
  configVersion: 1,
  createdBy: 'x',
  createdAt: '2026-08-01T00:00:00Z',
  updatedBy: null,
  updatedAt: null,
})

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValue([])
  vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValue([])
})

describe('DashboardView', () => {
  it('shows the connection when one is configured', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValueOnce({
      host: 'db.internal',
      port: 5432,
      database: 'erp',
      username: 'svc',
      sslMode: null,
    })
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Connected to erp')
    expect(w.text()).toContain('svc@db.internal:5432')
  })

  it('shows a "no connection" message and a Connect link when none is configured', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValueOnce(null)
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No connection configured')
    expect(w.text()).toContain('Connect')
  })

  it('shows enabled/total counts for export jobs and import definitions', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValueOnce(null)
    vi.mocked(exportDefinitionsApi.listExportDefinitions).mockResolvedValueOnce([exportDef(true), exportDef(false)])
    vi.mocked(importDefinitionsApi.listImportDefinitions).mockResolvedValueOnce([importDef(true)])
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('1')
    expect(w.text()).toContain('2 enabled')
    expect(w.text()).toContain('1 enabled')
  })

  it('renders quick links to the main areas', async () => {
    vi.spyOn(connectionApi, 'getConnection').mockResolvedValueOnce(null)
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Export Jobs')
    expect(w.text()).toContain('Import Jobs')
    expect(w.text()).toContain('Source Schema')
  })
})
