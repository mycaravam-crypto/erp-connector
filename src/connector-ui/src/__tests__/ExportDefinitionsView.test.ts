import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ExportDefinitionsView from '@/views/ExportDefinitionsView.vue'
import * as exportDefinitionsApi from '@/api/exportDefinitions'
import type { ExportDefinitionSummary } from '@/api/exportDefinitions'

function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/export-definitions', name: 'export-definitions', component: ExportDefinitionsView },
      { path: '/export-definitions/:id', name: 'export-definition-edit', component: { template: '<div/>' } },
    ],
  })
  r.push('/export-definitions')
  return r
}

const DEFINITIONS: ExportDefinitionSummary[] = [
  {
    id: 1,
    name: 'Legacy Export',
    description: null,
    rootTable: 'masterdata',
    outputFormat: 'json',
    isEnabled: false,
    schedule: null,
    configVersion: 1,
    createdBy: 'migration',
    createdAt: '2026-08-01T00:00:00Z',
    updatedBy: null,
    updatedAt: null,
  },
]

beforeEach(() => vi.restoreAllMocks())

describe('ExportDefinitionsView', () => {
  it('shows loading state initially', () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockReturnValue(new Promise(() => {}))
    const w = mount(ExportDefinitionsView, { global: { plugins: [buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows an error message when the API rejects', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockRejectedValueOnce(new Error('network'))
    const w = mount(ExportDefinitionsView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not load export definitions')
  })

  it('shows an empty-state message when there are no definitions', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce([])
    const w = mount(ExportDefinitionsView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No export definitions yet.')
  })

  it('lists definitions with an edit link', async () => {
    vi.spyOn(exportDefinitionsApi, 'listExportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    const w = mount(ExportDefinitionsView, { global: { plugins: [buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Legacy Export')
    expect(w.text()).toContain('masterdata')
    expect(w.find('a[href="/export-definitions/1"]').exists()).toBe(true)
  })
})
