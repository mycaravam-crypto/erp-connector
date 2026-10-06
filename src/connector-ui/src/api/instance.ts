import { authHeaders } from './auth'

/** This connector's identity, as stamped into every export manifest's `Producer` block. */
export interface ConnectorInstance {
  application: string
  version: string
  instanceId: string
}

export async function getConnectorInstance(): Promise<ConnectorInstance> {
  const res = await fetch('/api/settings/instance', { headers: authHeaders() })
  if (!res.ok) throw new Error(`Failed to load connector instance (HTTP ${res.status})`)
  return res.json() as Promise<ConnectorInstance>
}

/** Replaces this connector's instance ID with a new one (for an installation set up from a copy of another's
 * database). Returns the updated identity. */
export async function regenerateConnectorInstanceId(): Promise<ConnectorInstance> {
  const res = await fetch('/api/settings/instance/regenerate', { method: 'POST', headers: authHeaders() })
  if (!res.ok) throw new Error(`Failed to regenerate instance ID (HTTP ${res.status})`)
  return res.json() as Promise<ConnectorInstance>
}
