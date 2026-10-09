import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import DashboardView from '@/views/DashboardView.vue'
import * as connectionApi from '@/api/connection'
import * as exportDefinitionsApi from '@/api/exportDefinitions'
import * as importDefinitionsApi from '@/api/importDefinitions'
import type { ExportDefinitionSummary } from '@/api/exportDefinitions'
import type { ImportDefinitionSummary } from '@/api/importDefinitions'
import { signInAs } from './signInAs'

async function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'dashboard', component: DashboardView },
      { path: '/connect', name: 'connect', component: { template: '<div/>' } },
      { path: '/export-schema', name: 'export-schema', component: { template: '<div/>' } },
      { path: '/exports', name: 'exports', component: { template: '<div/>' } },
      {
        path: '/export-definitions',
        name: 'export-definitions',
        component: { template: '<div/>' },
        meta: { access: { anyOf: ['exportJobs.view' as const] } },
      },
      {
        path: '/import-definitions',
        name: 'import-definitions',
        component: { template: '<div/>' },
        meta: { access: { anyOf: ['importJobs.view' as const] } },
      },
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

const CONFIGURED = { configured: true, type: 0 as const, name: 'erp' }
const NOT_CONFIGURED = { configured: false, type: null, name: null }

beforeEach(async () => {
  vi.restoreAllMocks()
  vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValue([])
  vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValue([])
  vi.spyOn(connectionApi, 'getConnectionStatus').mockResolvedValue(NOT_CONFIGURED)
  vi.spyOn(connectionApi, 'getConnection').mockResolvedValue(null)
  await signInAs('Admin')
})

describe('DashboardView', () => {
  it('shows the connection when one is configured', async () => {
    vi.mocked(connectionApi.getConnectionStatus).mockResolvedValue(CONFIGURED)
    vi.mocked(connectionApi.getConnection).mockResolvedValueOnce({
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
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No connection configured')
    expect(w.text()).toContain('Connect')
  })

  it('shows enabled/total counts for export jobs and import definitions', async () => {
    vi.mocked(exportDefinitionsApi.listExportDefinitions).mockResolvedValueOnce([exportDef(true), exportDef(false)])
    vi.mocked(importDefinitionsApi.listImportDefinitions).mockResolvedValueOnce([importDef(true)])
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('1')
    expect(w.text()).toContain('2 enabled')
    expect(w.text()).toContain('1 enabled')
  })

  it('renders quick links to the main areas', async () => {
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Export Jobs')
    expect(w.text()).toContain('Import Jobs')
    expect(w.text()).toContain('Source Schema')
  })

  it('shows a User the connection without its account or an Edit link, and only the areas they may open', async () => {
    await signInAs('User', ['exportJobs.view'])
    vi.mocked(connectionApi.getConnectionStatus).mockResolvedValue(CONFIGURED)
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Connected to erp')
    expect(connectionApi.getConnection).not.toHaveBeenCalled()
    expect(w.text()).not.toContain('Edit')
    expect(w.text()).toContain('Export Jobs')
    expect(w.text()).not.toContain('Import Jobs')
    expect(importDefinitionsApi.listImportDefinitions).not.toHaveBeenCalled()
  })

  it('tells a User without a connection to ask an Admin', async () => {
    await signInAs('User')
    const w = mount(DashboardView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Ask an Admin')
  })
})
