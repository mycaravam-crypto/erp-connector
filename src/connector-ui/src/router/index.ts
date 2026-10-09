import { createRouter, createWebHistory, type RouteLocationNormalized } from 'vue-router'
import { isLoggedIn, clearSession } from '@/api/auth'
import { isConnectionConfigured } from '@/api/connection'
import DashboardView from '../views/DashboardView.vue'
import ConnectionView from '../views/ConnectionView.vue'
import SourceSchemaView from '../views/SourceSchemaView.vue'
import SchemaView from '../views/SchemaView.vue'
import ExportView from '../views/ExportView.vue'
import ExportDetail from '../views/ExportDetail.vue'
import LoginView from '../views/LoginView.vue'
import SettingsView from '../views/SettingsView.vue'
import IcdSchemaView from '../views/IcdSchemaView.vue'
import AuditView from '../views/AuditView.vue'
import ExportDefinitionsView from '../views/ExportDefinitionsView.vue'
import ExportDefinitionEditView from '../views/ExportDefinitionEditView.vue'
import ImportDefinitionsView from '../views/ImportDefinitionsView.vue'
import ImportDefinitionEditView from '../views/ImportDefinitionEditView.vue'
import NotFoundView from '../views/NotFoundView.vue'
import { Permission as P, SETTINGS_PERMISSIONS, type RouteAccess } from '@/lib/permissions'
import { canAccess, canOpen, loadCurrentUser, resetCurrentUser } from '@/composables/useCurrentUser'

declare module 'vue-router' {
  interface RouteMeta {
    /** Who may open the route; see RouteAccess. Absent means every signed-in user. */
    access?: RouteAccess
    /** Replaces `access` when the route's :id param is "new", i.e. the page creates something. */
    createAccess?: RouteAccess
  }
}

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/login', name: 'login', component: LoginView },
    { path: '/', name: 'dashboard', component: DashboardView },
    { path: '/connect', name: 'connect', component: ConnectionView, meta: { access: { admin: true } } },
    {
      path: '/source-schema',
      name: 'source-schema',
      component: SourceSchemaView,
      meta: { access: { anyOf: [P.SourceSchemaView] } },
    },
    {
      path: '/export-schema',
      name: 'export-schema',
      component: SchemaView,
      meta: { access: { anyOf: [P.ExportMappingView] } },
    },
    { path: '/exports', name: 'exports', component: ExportView, meta: { access: { anyOf: [P.ManagedExportView] } } },
    {
      path: '/exports/:seqNo',
      name: 'export-detail',
      component: ExportDetail,
      meta: { access: { anyOf: [P.ManagedExportView] } },
    },
    { path: '/settings', name: 'settings', component: SettingsView, meta: { access: { anyOf: SETTINGS_PERMISSIONS } } },
    { path: '/icd-schema', name: 'icd-schema', component: IcdSchemaView, meta: { access: { anyOf: [P.IcdSchemaView] } } },
    { path: '/audit', name: 'audit', component: AuditView, meta: { access: { anyOf: [P.AuditView] } } },
    {
      path: '/export-definitions',
      name: 'export-definitions',
      component: ExportDefinitionsView,
      meta: { access: { anyOf: [P.ExportJobsView] } },
    },
    {
      path: '/export-definitions/:id',
      name: 'export-definition-edit',
      component: ExportDefinitionEditView,
      meta: { access: { anyOf: [P.ExportJobsView] }, createAccess: { anyOf: [P.ExportJobsCreate] } },
    },
    {
      path: '/import-definitions',
      name: 'import-definitions',
      component: ImportDefinitionsView,
      meta: { access: { anyOf: [P.ImportJobsView] } },
    },
    {
      path: '/import-definitions/:id',
      name: 'import-definition-edit',
      component: ImportDefinitionEditView,
      meta: { access: { anyOf: [P.ImportJobsView] }, createAccess: { anyOf: [P.ImportJobsCreate] } },
    },
    // legacy redirects
    { path: '/schema', redirect: '/export-schema' },
    { path: '/erp-database', redirect: '/icd-schema' },
    { path: '/erp', redirect: '/icd-schema' },
    { path: '/pipeline', redirect: '/exports' },
    // catch-all — must be last
    { path: '/:pathMatch(.*)*', name: 'not-found', component: NotFoundView },
  ],
})

// Routes that require a stored ERP connection before they're useful. The dashboard is the / landing
// page once a connection exists — with no connection yet, it has nothing to show, so first-run visitors
// land on Connect (Step 1). Only Admins can open Connect; everyone else sees the dashboard's "ask an Admin" note.
const REQUIRES_CONNECTION = new Set(['source-schema', 'export-schema', 'dashboard'])

function needsLogin(routeName: unknown): boolean {
  return routeName !== 'login' && !isLoggedIn()
}

async function needsConnection(routeName: unknown): Promise<boolean> {
  if (!routeName || !REQUIRES_CONNECTION.has(String(routeName))) return false
  if (!canAccess({ admin: true })) return false
  return !(await isConnectionConfigured())
}

// Reloads the user's role and permissions on every navigation (see useCurrentUser), so a change an Admin made
// applies on the next click. A route the user may not open sends them to the dashboard, which everyone can see.
async function checkAccess(to: RouteLocationNormalized) {
  let user
  try {
    user = await loadCurrentUser()
  } catch {
    return // backend unreachable: keep the last known permissions rather than signing the user out
  }
  if (user === null) {
    clearSession()
    return { name: 'login' }
  }
  if (!canOpen(to)) return { name: 'dashboard' }
}

router.beforeEach(async (to) => {
  if (needsLogin(to.name)) {
    resetCurrentUser()
    return { name: 'login' }
  }
  if (to.name !== 'login') {
    const redirect = await checkAccess(to)
    if (redirect) return redirect
  }
  if (await needsConnection(to.name)) return { name: 'connect', query: { notice: 'needs-connection' } }
})

export default router
