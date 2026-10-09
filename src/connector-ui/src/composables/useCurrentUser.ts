import { ref, computed } from 'vue'
import type { RouteLocation } from 'vue-router'
import { fetchCurrentUser, type CurrentUser } from '@/api/auth'
import type { Permission, RouteAccess } from '@/lib/permissions'

/** The signed-in user's role and permissions, shared app-wide like useBranding's state. The router guard reloads
 *  it on every navigation, so an Admin's change to a role or the permission screen shows up on the user's next
 *  click. Only hides what the user can't use — the server checks every permission again itself. */
const currentUser = ref<CurrentUser | null>(null)

/** Reloads the current user; resolves to null when the session is no longer valid. */
export async function loadCurrentUser(): Promise<CurrentUser | null> {
  currentUser.value = await fetchCurrentUser()
  return currentUser.value
}

export function resetCurrentUser(): void {
  currentUser.value = null
}

function can(permission: Permission): boolean {
  return currentUser.value?.permissions.includes(permission) ?? false
}

/** Whether the current user may open a route with this access rule. */
export function canAccess(access: RouteAccess | undefined): boolean {
  if (!access) return true
  if (access.admin) return currentUser.value?.role === 'Admin'
  return access.anyOf?.some(can) ?? true
}

/** Whether the current user may open a resolved route: its `meta.access`, or `meta.createAccess` when its :id
 *  param is "new" (the page creates something). */
export function canOpen(to: RouteLocation): boolean {
  return canAccess(to.params.id === 'new' && to.meta.createAccess ? to.meta.createAccess : to.meta.access)
}

export function useCurrentUser() {
  return {
    currentUser,
    isAdmin: computed(() => currentUser.value?.role === 'Admin'),
    can,
  }
}
