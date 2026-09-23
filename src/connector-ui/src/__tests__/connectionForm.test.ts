import { describe, it, expect } from 'vitest'
import { DataSourceType } from '@/api/connection'
import {
  emptyForm,
  formFromStored,
  storedConnectionLabel,
  toConnectionConfig,
  validateConnectionForm,
  withSourceType,
} from '@/lib/connectionForm'

describe('connectionForm', () => {
  it('withSourceType moves a default port to the new default and keeps a custom one', () => {
    expect(withSourceType(emptyForm(), 'mariadb').port).toBe('3306')
    expect(withSourceType({ ...emptyForm(), port: '6000' }, 'mariadb').port).toBe('6000')
    expect(withSourceType({ ...emptyForm(), port: '' }, 'postgres').port).toBe('5432')
  })

  it('withSourceType drops the PostgreSQL-only "Allow" TLS mode for MariaDB', () => {
    expect(withSourceType({ ...emptyForm(), sslMode: 'Allow' }, 'mariadb').sslMode).toBe('')
    expect(withSourceType({ ...emptyForm(), sslMode: 'Require' }, 'mariadb').sslMode).toBe('Require')
  })

  it('formFromStored treats a config without a type as PostgreSQL and never fills the password', () => {
    const form = formFromStored({ host: 'h', port: 5432, database: 'd', username: 'u', sslMode: null, hasPassword: true })
    expect(form.sourceType).toBe('postgres')
    expect(form.password).toBe('')
  })

  it('formFromStored maps ServiceNow types to the access method', () => {
    const base = { host: null, port: null, database: null, instanceUrl: 'https://x.service-now.com', username: 'u', sslMode: null }
    expect(formFromStored({ ...base, type: DataSourceType.ServiceNowTableApi }).accessMethod).toBe('table')
    expect(formFromStored({ ...base, type: DataSourceType.ServiceNowSqlApi }).accessMethod).toBe('sql')
    expect(storedConnectionLabel(base)).toBe('https://x.service-now.com')
  })

  it('toConnectionConfig nulls the fields the source type does not use', () => {
    const config = toConnectionConfig({
      ...emptyForm(),
      sourceType: 'servicenow',
      host: 'leftover',
      instanceUrl: ' https://x.service-now.com ',
      username: 'u',
    })
    expect(config).toMatchObject({
      type: DataSourceType.ServiceNowTableApi,
      host: null,
      port: null,
      database: null,
      sslMode: null,
      instanceUrl: 'https://x.service-now.com',
    })
  })

  it('validateConnectionForm requires the fields of the chosen type only', () => {
    expect(Object.keys(validateConnectionForm(emptyForm()))).toEqual(['host', 'database', 'username'])
    expect(Object.keys(validateConnectionForm({ ...emptyForm(), sourceType: 'servicenow' }))).toEqual([
      'instanceUrl',
      'username',
    ])
  })
})
