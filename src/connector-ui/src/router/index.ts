import { createRouter, createWebHistory } from 'vue-router'
import ExportList from '../views/ExportList.vue'
import ExportDetail from '../views/ExportDetail.vue'

// Iteration 1: zwei Seiten, direktes Import ohne Lazy-Chunks (YAGNI).
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
      component: ExportDetail,
    },
  ],
})

export default router
