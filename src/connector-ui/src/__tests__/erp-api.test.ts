import { describe, it, expect, vi, beforeEach } from 'vitest'
import { listErpRecords, getSchema } from '@/api/erp'

function mockFetch(body: unknown, status = 200) {
  return vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce({
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as Response)
}

beforeEach(() => vi.restoreAllMocks())

describe('listErpRecords', () => {
  it('fetches /api/erp/records and returns the array', async () => {
    const payload = [
      {
        id: 'sc-rack-0001', serial: 'SN-RACK-0001', status: 'Active',
        commissionDate: '2023-03-01', articleName: 'Industrial Rack System',
        partNumber: 'P-RACK-42U', manufacturer: 'TechCorp GmbH',
        maintenancePlanStatus: 'Active', allocationChartRef: 'ALLOC-2023-V1',
        parentId: null, parentSerial: null,
        inScope: true, exclusionReason: null,
        technicianName: 'Klaus Bauer', storageLocation: 'Halle A, Reihe 3',
      },
    ]
    mockFetch(payload)
    const result = await listErpRecords()
    expect(result).toEqual(payload)
    expect(fetch).toHaveBeenCalledWith('/api/erp/records', expect.any(Object))
  })

  it('returns an empty array on non-ok response', async () => {
    mockFetch(null, 401)
    const result = await listErpRecords()
    expect(result).toEqual([])
  })
})

describe('getSchema', () => {
  it('fetches /api/schema and returns the definition', async () => {
    const payload = {
      version: '2.0',
      columns: [
        { name: 'guid', erpSource: 'systemconfiguration.id', type: 'UUID text', notes: 'Coalesce key', active: true },
      ],
    }
    mockFetch(payload)
    const result = await getSchema()
    expect(result).toEqual(payload)
    expect(fetch).toHaveBeenCalledWith('/api/schema', expect.any(Object))
  })

  it('returns null on non-ok response', async () => {
    mockFetch(null, 401)
    const result = await getSchema()
    expect(result).toBeNull()
  })
})
