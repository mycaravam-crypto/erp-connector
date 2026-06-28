import { createRouter, createWebHistory } from 'vue-router'
import ExportList from '../views/ExportList.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'exports',
      component: ExportList,
    },
    {
      path: '/exports/:seqNo',
      name: 'export-detail',
      component: () => import('../views/ExportDetail.vue'),
    },
  ],
})

export default router
