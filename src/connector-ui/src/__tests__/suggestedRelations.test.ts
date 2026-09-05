import { describe, it, expect } from 'vitest'
import { findSuggestedRelations } from '@/lib/suggestedRelations'
import type { SourceSchema } from '@/api/connection'

const schema: SourceSchema = {
  connectionLabel: 'test',
  tables: [
    {
      name: 'item',
      description: '',
      columns: [
        { name: 'id', type: 'uuid', nullable: false, primaryKey: true, foreignKeyTable: null, foreignKeyColumn: null },
        {
          name: 'manufacturer_id',
          type: 'uuid',
          nullable: true,
          primaryKey: false,
          foreignKeyTable: 'manufacturer',
          foreignKeyColumn: 'id',
        },
      ],
    },
    {
      name: 'manufacturer',
      description: '',
      columns: [
        { name: 'id', type: 'uuid', nullable: false, primaryKey: true, foreignKeyTable: null, foreignKeyColumn: null },
      ],
    },
    {
      name: 'address',
      description: '',
      columns: [
        { name: 'id', type: 'uuid', nullable: false, primaryKey: true, foreignKeyTable: null, foreignKeyColumn: null },
        {
          name: 'item_id',
          type: 'uuid',
          nullable: true,
          primaryKey: false,
          foreignKeyTable: 'item',
          foreignKeyColumn: 'id',
        },
      ],
    },
  ],
}

describe('findSuggestedRelations', () => {
  it('suggests a forward FK (this table -> related PK) as an object', () => {
    const suggestions = findSuggestedRelations(schema, 'item', [])
    expect(suggestions).toContainEqual({
      relatedTable: 'manufacturer',
      joinKey: 'id',
      sourceJoinKey: 'manufacturer_id',
      kind: 'object',
    })
  })

  it('suggests a reverse FK (another table -> this PK) as an array', () => {
    const suggestions = findSuggestedRelations(schema, 'item', [])
    expect(suggestions).toContainEqual({
      relatedTable: 'address',
      joinKey: 'item_id',
      sourceJoinKey: 'id',
      kind: 'array',
    })
  })

  it('returns both directions together for a table with FKs in and out', () => {
    const suggestions = findSuggestedRelations(schema, 'item', [])
    expect(suggestions).toHaveLength(2)
  })

  it('excludes a suggestion already present in existing', () => {
    const suggestions = findSuggestedRelations(schema, 'item', [
      { relatedTable: 'manufacturer', joinKey: 'id', sourceJoinKey: 'manufacturer_id' },
    ])
    expect(suggestions).toEqual([{ relatedTable: 'address', joinKey: 'item_id', sourceJoinKey: 'id', kind: 'array' }])
  })

  it('returns nothing for a null table or schema', () => {
    expect(findSuggestedRelations(schema, null, [])).toEqual([])
    expect(findSuggestedRelations(null, 'item', [])).toEqual([])
  })

  it('suggests only the reverse direction for a table with no forward FKs of its own', () => {
    expect(findSuggestedRelations(schema, 'manufacturer', [])).toEqual([
      { relatedTable: 'item', joinKey: 'manufacturer_id', sourceJoinKey: 'id', kind: 'array' },
    ])
  })
})
