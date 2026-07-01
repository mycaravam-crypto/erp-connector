import { createRouter, createWebHistory } from 'vue-router'
import { isLoggedIn } from '@/api/auth'
import ConnectionView from '../views/ConnectionView.vue'
import SourceSchemaView from '../views/SourceSchemaView.vue'
import SchemaView from '../views/SchemaView.vue'
import ExportView from '../views/ExportView.vue'
import ExportDetail from '../views/ExportDetail.vue'
import LoginView from '../views/LoginView.vue'
import SettingsView from '../views/SettingsView.vue'
import ErpDatabaseView from '../views/ErpDatabaseView.vue'
import IcdSchemaView from '../views/IcdSchemaView.vue'

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
    { path: '/erp-database', name: 'erp-database', component: ErpDatabaseView },
    { path: '/icd-schema', name: 'icd-schema', component: IcdSchemaView },
    // legacy redirects
    { path: '/schema', redirect: '/export-schema' },
    { path: '/erp', redirect: '/erp-database' },
    { path: '/pipeline', redirect: '/exports' },
  ],
})

router.beforeEach((to) => {
  if (to.name !== 'login' && !isLoggedIn()) return { name: 'login' }
})

export default router
