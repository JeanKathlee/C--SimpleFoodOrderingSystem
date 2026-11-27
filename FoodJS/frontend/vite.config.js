import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import process from 'node:process'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const csharpApi = env.CSHARP_API_URL || 'http://localhost:3002'
  return {
  plugins: [react()],
  server: {
    proxy: {
      // React is the View; every API request uses the ASP.NET Core MVC/Web API backend.
      '/api': csharpApi,
    },
  },
  }
})
