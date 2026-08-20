import { createRouter, createWebHistory } from 'vue-router'
import { isLoggedIn } from '@/api/auth'
import { isConnectionConfigured } from '@/api/connection'
import ConnectionView from '../views/ConnectionView.vue'
import SourceSchemaView from '../views/SourceSchemaView.vue'
import SchemaView from '../views/SchemaView.vue'
import ExportView from '../views/ExportView.vue'
import ExportDetail from '../views/ExportDetail.vue'
import LoginView from '../views/LoginView.vue'
import SettingsView from '../views/SettingsView.vue'
import IcdSchemaView from '../views/IcdSchemaView.vue'
import AuditView from '../views/AuditView.vue'
import NotFoundView from '../views/NotFoundView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    { path: '/login', name: 'login', component: LoginView },
    { path: '/', redirect: '/connect' },
    { path: '/connect', name: 'connect', component: ConnectionView },
    { path: '/source-schema', name: 'source-schema', component: SourceSchemaView },
    { path: '/export-schema', name: 'export-schema', component: SchemaView },
    { path: '/exports', name: 'exports', component: ExportView },
    { path: '/exports/:seqNo', name: 'export-detail', component: ExportDetail },
    { path: '/settings', name: 'settings', component: SettingsView },
    { path: '/icd-schema', name: 'icd-schema', component: IcdSchemaView },
    { path: '/audit', name: 'audit', component: AuditView },
    // legacy redirects
    { path: '/schema', redirect: '/export-schema' },
    { path: '/erp-database', redirect: '/icd-schema' },
    { path: '/erp', redirect: '/icd-schema' },
    { path: '/pipeline', redirect: '/exports' },
    // catch-all — must be last
    { path: '/:pathMatch(.*)*', name: 'not-found', component: NotFoundView },
  ],
})

// Routes that require a stored ERP connection before they're useful.
const REQUIRES_CONNECTION = new Set(['source-schema', 'export-schema'])

function needsLogin(routeName: unknown): boolean {
  return routeName !== 'login' && !isLoggedIn()
}

async function needsConnection(routeName: unknown): Promise<boolean> {
  if (!routeName || !REQUIRES_CONNECTION.has(String(routeName))) return false
  return !(await isConnectionConfigured())
}

router.beforeEach(async (to) => {
  if (needsLogin(to.name)) return { name: 'login' }
  if (await needsConnection(to.name)) return { name: 'connect', query: { notice: 'needs-connection' } }
})

export default router
