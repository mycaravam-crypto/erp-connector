import { fileURLToPath, URL } from 'node:url'

/// <reference types="vitest" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import vueDevTools from 'vite-plugin-vue-devtools'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    tailwindcss(),
    vue(),
    vueDevTools(),
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5189',
    },
  },
  // @ts-expect-error vitest v3 + vite v8 (rolldown) type incompatibility
  test: {
    environment: 'happy-dom',
    globals: true,
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
})
