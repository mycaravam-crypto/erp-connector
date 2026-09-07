import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createRouter, createMemoryHistory } from 'vue-router'
import ImportDefinitionEditView from '@/views/ImportDefinitionEditView.vue'
import * as importDefinitionsApi from '@/api/importDefinitions'
import * as authApi from '@/api/auth'
import * as connectionApi from '@/api/connection'
import type { ImportDefinition } from '@/api/importDefinitions'
import type { SourceSchema } from '@/api/connection'

// Awaits the initial navigation before returning, same reasoning as ExportDefinitionEditView.test.ts:
// reading route.params synchronously at setup time would otherwise race the pending push.
async function buildRouter(id: number | string = 1) {
  const r = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/import-definitions', name: 'import-definitions', component: { template: '<div/>' } },
      { path: '/import-definitions/:id', name: 'import-definition-edit', component: ImportDefinitionEditView },
    ],
  })
  await r.push(`/import-definitions/${id}`)
  return r
}

const SCHEMA: SourceSchema = {
  connectionLabel: 'test',
  tables: [
    {
      name: 'masterdata',
      description: '',
      columns: [
        { name: 'guid', type: 'uuid', nullable: false, primaryKey: true, foreignKeyTable: null, foreignKeyColumn: null },
        { name: 'status', type: 'text', nullable: true, primaryKey: false, foreignKeyTable: null, foreignKeyColumn: null },
      ],
    },
  ],
}

const DEFINITION: ImportDefinition = {
  id: 1,
  name: 'Vendor Confirmations',
  description: null,
  rootTable: 'masterdata',
  rootMatchColumn: 'guid',
  unmatchedRootPolicy: 'reject',
  allowedWritableColumns: ['status'],
  isEnabled: false,
  configVersion: 1,
  createdBy: 'alice',
  createdAt: '2026-09-01T00:00:00Z',
  updatedBy: null,
  updatedAt: null,
  integrationKey: null,
  contractVersion: null,
  rootNode: {
    sourceKey: 'root',
    kind: 'root',
    targetColumn: null,
    relatedTable: null,
    joinKey: null,
    sourceJoinKey: null,
    onMissingChild: 'reject',
    mapping: null,
    enabled: true,
    children: [
      {
        sourceKey: 'guid',
        kind: 'scalar-field',
        targetColumn: 'guid',
        relatedTable: null,
        joinKey: null,
        sourceJoinKey: null,
        onMissingChild: 'reject',
        mapping: { defaultValue: null, transform: 'none', transformArg: null, dataType: 'string' },
        enabled: true,
        children: [],
      },
      {
        sourceKey: 'status',
        kind: 'scalar-field',
        targetColumn: 'status',
        relatedTable: null,
        joinKey: null,
        sourceJoinKey: null,
        onMissingChild: 'reject',
        mapping: { defaultValue: null, transform: 'none', transformArg: null, dataType: 'string' },
        enabled: true,
        children: [],
      },
    ],
  },
}

beforeEach(() => {
  vi.restoreAllMocks()
  vi.spyOn(connectionApi, 'getSourceSchema').mockResolvedValue(SCHEMA)
  vi.spyOn(importDefinitionsApi, 'listImportDefinitionRuns').mockResolvedValue([])
})

describe('ImportDefinitionEditView', () => {
  it('shows loading state initially', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockReturnValue(new Promise(() => {}))
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    expect(w.text()).toContain('Loading')
  })

  it('shows a not-found message when the definition does not exist', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(null)
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('Import definition not found.')
  })

  it('renders the root table, match column, and allowed columns from the loaded definition', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(DEFINITION)
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect((w.find('select[aria-label="Root table"]').element as HTMLSelectElement).value).toBe('masterdata')
    expect((w.find('input[aria-label="Root match column"]').element as HTMLInputElement).value).toBe('guid')
    expect(w.text()).toContain('status')
  })

  it('flags a tree target column missing from the allowlist', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce({
      ...DEFINITION,
      allowedWritableColumns: [],
    })
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()
    expect(w.text()).toContain('not in this allowlist')
    expect(w.text()).toContain('status')
  })

  it('saves the edited name and allowed columns', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(DEFINITION)
    const updateSpy = vi
      .spyOn(importDefinitionsApi, 'updateImportDefinition')
      .mockResolvedValueOnce({ ok: true, data: { ...DEFINITION, name: 'Renamed Import' } })
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.find('input[aria-label="Name"]').setValue('Renamed Import')
    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(updateSpy).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ name: 'Renamed Import', allowedWritableColumns: ['status'] }),
    )
    expect(w.text()).toContain('Saved.')
  })

  it('shows the server error message when save fails', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(importDefinitionsApi, 'updateImportDefinition').mockResolvedValueOnce({
      ok: false,
      error: 'RootMatchColumn is required and must be a valid identifier.',
    })
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    const saveBtn = w.findAll('button').find((b) => b.text() === 'Save')!
    await saveBtn.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('RootMatchColumn is required and must be a valid identifier.')
  })

  it('previews a sample inbound file and renders the returned plan', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(importDefinitionsApi, 'previewImportDefinition').mockResolvedValueOnce({
      ok: true,
      data: {
        recordCount: 1,
        matchedCount: 1,
        changedCount: 1,
        unchangedCount: 0,
        rejectedCount: 0,
        invalidCount: 0,
        operations: [
          {
            correlationValue: 'abc',
            table: 'masterdata',
            keyColumn: 'guid',
            keyValue: 'abc',
            column: 'status',
            expectedOldValue: 'pending',
            newValue: 'confirmed',
          },
        ],
      },
    })
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.find('textarea[aria-label="Sample inbound JSON"]').setValue('{"schemaVersion":1,"records":[]}')
    const previewBtn = w.findAll('button').find((b) => b.text() === 'Preview')!
    await previewBtn.trigger('click')
    await flushPromises()

    expect(importDefinitionsApi.previewImportDefinition).toHaveBeenCalledWith(1, '{"schemaVersion":1,"records":[]}')
    expect(w.text()).toContain('confirmed')
    expect(w.text()).toContain('1 changed')
  })

  it('renders the run history from the backend', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(importDefinitionsApi, 'listImportDefinitionRuns').mockResolvedValueOnce([
      {
        id: 9,
        configVersion: 1,
        startedAt: '2026-09-01T00:00:00Z',
        finishedAt: '2026-09-01T00:00:01Z',
        status: 'PendingReview',
        recordCount: 1,
        matchedCount: 1,
        changedCount: 1,
        unchangedCount: 0,
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
    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    expect(w.text()).toContain('watcher')
    expect(w.text()).toContain('PendingReview')
  })

  it('opens the review dialog for a pending run and releases it', async () => {
    vi.spyOn(importDefinitionsApi, 'getImportDefinition').mockResolvedValueOnce(DEFINITION)
    vi.spyOn(importDefinitionsApi, 'listImportDefinitionRuns').mockResolvedValue([
      {
        id: 9,
        configVersion: 1,
        startedAt: '2026-09-01T00:00:00Z',
        finishedAt: '2026-09-01T00:00:01Z',
        status: 'PendingReview',
        recordCount: 1,
        matchedCount: 1,
        changedCount: 1,
        unchangedCount: 0,
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
    vi.spyOn(authApi, 'getUsername').mockReturnValue('alice')
    vi.spyOn(importDefinitionsApi, 'getImportRun').mockResolvedValueOnce({
      id: 9,
      importDefinitionId: 1,
      importDefinitionName: 'Vendor Confirmations',
      configVersion: 1,
      sourceFileName: 'vendor.json',
      startedAt: '2026-09-01T00:00:00Z',
      finishedAt: '2026-09-01T00:00:01Z',
      status: 'PendingReview',
      recordCount: 1,
      matchedCount: 1,
      changedCount: 1,
      unchangedCount: 0,
      rejectedCount: 0,
      conflictCount: 0,
      invalidCount: 0,
      errorMessage: null,
      triggeredBy: 'watcher',
      operatedBy: null,
      approvedBy: null,
      releasedAt: null,
      operations: [
        {
          correlationValue: 'abc',
          table: 'masterdata',
          keyColumn: 'guid',
          keyValue: 'abc',
          column: 'status',
          expectedOldValue: 'pending',
          newValue: 'confirmed',
        },
      ],
    })
    const releaseSpy = vi.spyOn(importDefinitionsApi, 'releaseImportRun').mockResolvedValueOnce({
      ok: true,
      data: {
        id: 9,
        importDefinitionId: 1,
        status: 'Released',
        recordCount: 1,
        matchedCount: 1,
        changedCount: 1,
        unchangedCount: 0,
        rejectedCount: 0,
        conflictCount: 0,
        invalidCount: 0,
        operatedBy: 'alice',
        approvedBy: 'bob',
        releasedAt: '2026-09-01T00:05:00Z',
        errorMessage: null,
      },
    })

    const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter()] } })
    await flushPromises()

    await w.findAll('button').find((b) => b.text() === 'Review')!.trigger('click')
    await flushPromises()

    expect(w.text()).toContain('confirmed')
    expect(w.text()).toContain('1 changed')

    await w.find('[role="dialog"] input').setValue('bob')
    const confirmBtn = w.findAll('button').find((b) => b.text().includes('Confirm Release'))!
    await confirmBtn.trigger('click')
    await flushPromises()

    expect(releaseSpy).toHaveBeenCalledWith(9, 'bob')
  })

  describe('create mode (id = "new")', () => {
    it('offers Create from export vs Start blank before showing the create form', async () => {
      const getSpy = vi.spyOn(importDefinitionsApi, 'getImportDefinition')
      const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter('new')] } })
      await flushPromises()

      expect(getSpy).not.toHaveBeenCalled()
      expect(w.text()).toContain('New Import Definition')
      expect(w.text()).toContain('Start from')
      expect(w.findAll('button').some((b) => b.text() === 'Create')).toBe(false)

      await w.findAll('button').find((b) => b.text() === 'Start blank')!.trigger('click')
      expect(w.findAll('button').some((b) => b.text() === 'Create')).toBe(true)
    })

    it('creates the definition and navigates to its edit page', async () => {
      const createSpy = vi
        .spyOn(importDefinitionsApi, 'createImportDefinition')
        .mockResolvedValueOnce({ ok: true, data: { ...DEFINITION, id: 42, name: 'New One' } })
      const router = await buildRouter('new')
      const w = mount(ImportDefinitionEditView, { global: { plugins: [router] } })
      await flushPromises()

      await w.findAll('button').find((b) => b.text() === 'Start blank')!.trigger('click')
      await w.find('select[aria-label="Root table"]').setValue('masterdata')
      await w.find('input[aria-label="Name"]').setValue('New One')
      const createBtn = w.findAll('button').find((b) => b.text() === 'Create')!
      await createBtn.trigger('click')
      await flushPromises()

      expect(createSpy).toHaveBeenCalledWith(expect.objectContaining({ name: 'New One', rootTable: 'masterdata' }))
      expect(router.currentRoute.value.params.id).toBe('42')
    })

    it('accepts a "Create from export" suggestion and prefills the root table, match column, and tree', async () => {
      vi.spyOn(importDefinitionsApi, 'suggestImportMappingFromExport').mockResolvedValueOnce({
        suggestion: {
          exportDefinitionId: 7,
          exportDefinitionName: 'CI confirmation export',
          integrationKey: 'ci-confirmation',
          contractVersion: 1,
          rootTable: 'masterdata',
          rootMatchColumn: 'guid',
          rootMatchSourceKey: 'guidField',
          candidateFields: [{ sourceKey: 'status', targetColumn: 'status' }],
        },
        reason: null,
      })
      const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter('new')] } })
      await flushPromises()

      await w.find('textarea[aria-label="Sample inbound JSON"]').setValue('{"provenance":{"integrationKey":"ci-confirmation","contractVersion":1}}')
      await w.findAll('button').find((b) => b.text() === 'Check for match')!.trigger('click')
      await flushPromises()

      expect(w.text()).toContain('CI confirmation export')
      await w.findAll('button').find((b) => b.text() === 'Create from export')!.trigger('click')
      await flushPromises()

      expect((w.find('input[aria-label="Root match column"]').element as HTMLInputElement).value).toBe('guid')
      expect(w.text()).toContain('Paired with')
      expect(w.text()).toContain('ci-confirmation v1')
    })

    it('shows the server-provided reason when a sample carries a provenance pair but nothing usable matches', async () => {
      vi.spyOn(importDefinitionsApi, 'suggestImportMappingFromExport').mockResolvedValueOnce({
        suggestion: null,
        reason:
          'Export "CI confirmation export" matches this integration key and version, but has no ' +
          'Correlation key field set. Add one under that export\'s "Integration tagging" section, then ' +
          'check this sample again.',
      })
      const w = mount(ImportDefinitionEditView, { global: { plugins: [await buildRouter('new')] } })
      await flushPromises()

      await w.find('textarea[aria-label="Sample inbound JSON"]').setValue('{"provenance":{"integrationKey":"ci-confirmation","contractVersion":1}}')
      await w.findAll('button').find((b) => b.text() === 'Check for match')!.trigger('click')
      await flushPromises()

      expect(w.text()).toContain('Correlation key field set')
      expect(w.text()).not.toContain('No matching export found for this sample')
    })
  })
})
