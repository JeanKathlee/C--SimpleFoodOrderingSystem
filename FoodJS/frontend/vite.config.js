import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import process from 'node:process'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const csharpApi = env.CSHARP_API_URL || 'http://localhost:3002'
  const referenceApi = env.REFERENCE_API_URL || 'http://localhost:3001'
  return {
  plugins: [react()],
  server: {
    proxy: {
      // ARATIA's assigned APIs, including checkout's POST /api/orders.
      '^/api/(menu|orders)(?:/|\\?|$)': csharpApi,
      // Teammate-owned APIs continue using their current implementation.
      '/api': referenceApi,
    },
  },
  }
})
