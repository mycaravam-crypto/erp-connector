import { vi } from 'vitest'
import * as authApi from '@/api/auth'
import { loadCurrentUser } from '@/composables/useCurrentUser'
import { Permission } from '@/lib/permissions'

/** Sets the shared current user (useCurrentUser) for a component test: an Admin holds every permission, a User
 *  only `permissions`. */
export async function signInAs(role: 'Admin' | 'User', permissions: string[] = []): Promise<void> {
  vi.spyOn(authApi, 'fetchCurrentUser').mockResolvedValue({
    username: role === 'Admin' ? 'alice' : 'carol',
    role,
    permissions: role === 'Admin' ? Object.values(Permission) : permissions,
  })
  await loadCurrentUser()
}
