import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ImportDefinitionsView from '@/views/ImportDefinitionsView.vue'
import * as importDefinitionsApi from '@/api/importDefinitions'
import type { ImportDefinitionSummary } from '@/api/importDefinitions'

async function buildRouter() {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/import-definitions', name: 'import-definitions', component: ImportDefinitionsView },
      { path: '/import-definitions/:id', name: 'import-definition-edit', component: { template: '<div/>' } },
    ],
  })
  await r.push('/import-definitions')
  return r
}

const DEFINITIONS: ImportDefinitionSummary[] = [
  {
    id: 1,
    name: 'Vendor Confirmations',
    description: null,
    rootTable: 'masterdata',
    unmatchedRootPolicy: 'reject',
    isEnabled: false,
    configVersion: 1,
    createdBy: 'alice',
    createdAt: '2026-09-01T00:00:00Z',
    updatedBy: null,
    updatedAt: null,
  },
]

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(importDefinitionsApi, 'listImportDefinitionRuns').mockResolvedValue([])
})

describe('ImportDefinitionsView', () => {
  it('shows loading state initially', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockReturnValue(new Promise(() => {}))
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows an error message when the API rejects', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockRejectedValueOnce(new Error('network'))
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Could not load import definitions')
  })

  it('shows an empty-state message when there are no definitions', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValueOnce([])
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('No import definitions yet.')
  })

  it('lists definitions with an edit link', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Vendor Confirmations')
    expect(w.text()).toContain('masterdata')
    expect(w.find('a[href="/import-definitions/1"]').exists()).toBe(true)
  })

  it('has a link to create a new definition', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValueOnce([])
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.find('a[href="/import-definitions/new"]').exists()).toBe(true)
  })

  it('shows the last run status fetched alongside the list', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    vi.spyOn(importDefinitionsApi, 'listImportDefinitionRuns').mockResolvedValueOnce([
      {
        id: 1,
        configVersion: 1,
        startedAt: '2026-09-01T00:00:00Z',
        finishedAt: '2026-09-01T00:00:01Z',
        status: 'PendingReview',
        recordCount: 3,
        matchedCount: 3,
        changedCount: 2,
        unchangedCount: 1,
        rejectedCount: 0,
        conflictCount: 0,
        invalidCount: 0,
        errorMessage: null,
        triggeredBy: 'watcher',
        operatedBy: null,
        approvedBy: null,
        releasedAt: null,
      },
    ])
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('PendingReview')
  })

  it('toggles the enabled state via the checkbox', async () => {
    vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValueOnce(DEFINITIONS)
    const enableSpy = vi
      .spyOn(importDefinitionsApi, 'setImportDefinitionEnabled')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITIONS[0]!, isEnabled: true } as never })
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    expect(enableSpy).toHaveBeenCalledWith(1, true)
  })

  it('duplicates a definition and reloads the list', async () => {
    const listSpy = vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValue(DEFINITIONS)
    const duplicateSpy = vi
      .spyOn(importDefinitionsApi, 'duplicateImportDefinition')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITIONS[0]!, id: 2 } as never })
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const duplicateBtn = w.findAll('button').find((b) => b.text() === 'Duplicate')!
    await duplicateBtn.trigger('click')
    await flushPromises()

    expect(duplicateSpy).toHaveBeenCalledWith(1)
    expect(listSpy).toHaveBeenCalledTimes(2)
  })

  it('deletes a definition after confirmation', async () => {
    const listSpy = vi.spyOn(importDefinitionsApi, 'listImportDefinitions').mockResolvedValue(DEFINITIONS)
    const deleteSpy = vi.spyOn(importDefinitionsApi, 'deleteImportDefinition').mockResolvedValueOnce(true)
    const w = mount(ImportDefinitionsView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.findAll('button').find((b) => b.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(w.findAll('button').some((b) => b.text() === 'Confirm')).toBe(true)

    await w.findAll('button').find((b) => b.text() === 'Confirm')!.trigger('click')
    await flushPromises()

    expect(deleteSpy).toHaveBeenCalledWith(1)
    expect(listSpy).toHaveBeenCalledTimes(2)
  })
})
