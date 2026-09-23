import { DataSourceType, type ConnectionConfig, type ErpConnectionInfo } from '@/api/connection'

// The connection form's state and the pure rules around it (Arbeitsauftrag 8): which fields each source type
// uses, default ports, required-field validation, and the mapping to/from the backend's DataSourceConfig.

export type SourceType = 'postgres' | 'mariadb' | 'servicenow'
export type AccessMethod = 'table' | 'sql'

export const DEFAULT_PORTS = { postgres: '5432', mariadb: '3306' } as const

export interface ConnectionForm {
  sourceType: SourceType
  accessMethod: AccessMethod
  host: string
  port: string
  database: string
  instanceUrl: string
  username: string
  password: string
  /** Empty string means "use the default" (Prefer) — see SR-03. */
  sslMode: string
}

export function emptyForm(): ConnectionForm {
  return {
    sourceType: 'postgres',
    accessMethod: 'table',
    host: '',
    port: DEFAULT_PORTS.postgres,
    database: '',
    instanceUrl: '',
    username: '',
    password: '',
    sslMode: '',
  }
}

export const isRelational = (sourceType: SourceType) => sourceType !== 'servicenow'

/** Switches the source type. The port follows the new type's default unless the user typed their own, and a
 *  TLS mode the new type doesn't offer (MariaDB has no "Allow") falls back to the default. */
export function withSourceType(form: ConnectionForm, next: SourceType): ConnectionForm {
  const defaults: string[] = Object.values(DEFAULT_PORTS)
  const port = next !== 'servicenow' && (form.port === '' || defaults.includes(form.port)) ? DEFAULT_PORTS[next] : form.port
  const sslMode = next === 'mariadb' && form.sslMode === 'Allow' ? '' : form.sslMode
  return { ...form, sourceType: next, port, sslMode }
}

function sourceTypeOf(type: DataSourceType | undefined): Pick<ConnectionForm, 'sourceType' | 'accessMethod'> {
  if (type === DataSourceType.MariaDb) return { sourceType: 'mariadb', accessMethod: 'table' }
  if (type === DataSourceType.ServiceNowTableApi) return { sourceType: 'servicenow', accessMethod: 'table' }
  if (type === DataSourceType.ServiceNowSqlApi) return { sourceType: 'servicenow', accessMethod: 'sql' }
  // A config stored before `type` existed is PostgreSQL.
  return { sourceType: 'postgres', accessMethod: 'table' }
}

/** Form state for a stored connection. The password is never part of GET responses, so it stays empty. */
export function formFromStored(stored: ErpConnectionInfo): ConnectionForm {
  const { sourceType, accessMethod } = sourceTypeOf(stored.type)
  const defaultPort = sourceType === 'mariadb' ? DEFAULT_PORTS.mariadb : DEFAULT_PORTS.postgres
  return {
    sourceType,
    accessMethod,
    host: stored.host ?? '',
    port: stored.port == null ? defaultPort : String(stored.port),
    database: stored.database ?? '',
    instanceUrl: stored.instanceUrl ?? '',
    username: stored.username,
    password: '',
    sslMode: stored.sslMode ?? '',
  }
}

export function storedConnectionLabel(stored: ErpConnectionInfo): string {
  return stored.host ? `${stored.host}:${stored.port}/${stored.database}` : (stored.instanceUrl ?? '')
}

function dataSourceTypeOf(form: ConnectionForm): DataSourceType {
  if (form.sourceType === 'mariadb') return DataSourceType.MariaDb
  if (form.sourceType === 'postgres') return DataSourceType.PostgreSql
  return form.accessMethod === 'sql' ? DataSourceType.ServiceNowSqlApi : DataSourceType.ServiceNowTableApi
}

/** The POST body: only the fields the chosen source type uses, everything else null. */
export function toConnectionConfig(form: ConnectionForm): ConnectionConfig {
  const relational = isRelational(form.sourceType)
  return {
    type: dataSourceTypeOf(form),
    host: relational ? form.host.trim() : null,
    port: relational ? Number(form.port) : null,
    database: relational ? form.database.trim() : null,
    instanceUrl: relational ? null : form.instanceUrl.trim(),
    username: form.username.trim(),
    password: form.password,
    sslMode: relational ? form.sslMode : null,
  }
}

export function portError(form: ConnectionForm): string | null {
  if (!isRelational(form.sourceType)) return null
  const n = Number(form.port)
  return Number.isInteger(n) && n >= 1 && n <= 65535 ? null : 'Port must be a number between 1 and 65535.'
}

/** Per-field messages for missing/invalid required fields of the chosen source type. */
export function validateConnectionForm(form: ConnectionForm): Record<string, string> {
  const errors: Record<string, string> = {}
  const required = (key: keyof ConnectionForm, label: string) => {
    if (!form[key].trim()) errors[key] = `${label} is required.`
  }
  if (isRelational(form.sourceType)) {
    required('host', 'Host')
    required('database', 'Database')
  } else {
    required('instanceUrl', 'Instance URL')
    if (!errors.instanceUrl && !/^https:\/\/[^\s/]+/i.test(form.instanceUrl.trim()))
      errors.instanceUrl = 'Instance URL must start with https:// (e.g. https://acme.service-now.com).'
  }
  required('username', 'Username')
  return errors
}
