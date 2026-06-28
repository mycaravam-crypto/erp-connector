import { createRouter, createWebHistory } from 'vue-router'
import { isLoggedIn } from '@/api/auth'
import ExportList from '../views/ExportList.vue'
import ExportDetail from '../views/ExportDetail.vue'
import LoginView from '../views/LoginView.vue'
import ErpDatabaseView from '../views/ErpDatabaseView.vue'
import SchemaView from '../views/SchemaView.vue'
import PipelineView from '../views/PipelineView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: LoginView,
    },
    {
      path: '/',
      name: 'exports',
      component: ExportList,
    },
    {
      path: '/exports/:seqNo',
      name: 'export-detail',
      component: ExportDetail,
    },
    {
      path: '/erp',
      name: 'erp-database',
      component: ErpDatabaseView,
    },
    {
      path: '/schema',
      name: 'schema',
      component: SchemaView,
    },
    {
      path: '/pipeline',
      name: 'pipeline',
      component: PipelineView,
    },
  ],
})

router.beforeEach((to) => {
  if (to.name !== 'login' && !isLoggedIn()) return { name: 'login' }
})

export default router
