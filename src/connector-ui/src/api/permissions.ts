import { authHeaders } from './auth'

export interface PermissionInfo {
  key: string
  label: string
  description: string
}

/** The permissions of one menu item. A group's `.view` permission decides whether the item is shown, and every
 *  other permission in the group implies it. */
export interface PermissionGroup {
  key: string
  label: string
  permissions: PermissionInfo[]
}

export interface RolePermissions {
  catalogue: PermissionGroup[]
  userPermissions: string[]
}

/** The grantable permissions and what the User role currently holds (Admin only). */
export async function getRolePermissions(): Promise<RolePermissions> {
  const res = await fetch('/api/settings/permissions', { headers: authHeaders() })
  if (!res.ok) throw new Error(`Error ${res.status}`)
  return res.json() as Promise<RolePermissions>
}

/** Replaces the User role's permissions; resolves to what the server stored (unknown keys dropped, implied
 *  `.view` permissions added). */
export async function saveRolePermissions(
  userPermissions: string[],
): Promise<{ ok: true; data: RolePermissions } | { ok: false; error: string }> {
  const res = await fetch('/api/settings/permissions', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ userPermissions }),
  })
  if (res.ok) return { ok: true, data: (await res.json()) as RolePermissions }
  return { ok: false, error: (await res.text().catch(() => '')) || `Error ${res.status}` }
}
