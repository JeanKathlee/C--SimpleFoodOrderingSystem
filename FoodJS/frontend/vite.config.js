import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api/users': 'http://localhost:3002',
      '/api': 'http://localhost:3001',
    },
  },
})
