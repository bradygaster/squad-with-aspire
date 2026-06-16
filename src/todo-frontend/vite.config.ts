import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const todoApiUrl =
  process.env.services__todo_api__https__0 ??
  process.env.services__todo_api__http__0 ??
  'http://localhost:5002'

export default defineConfig({
  plugins: [react()],
  server: {
    port: parseInt(process.env.PORT ?? '5174'),
    proxy: {
      '/api': {
        target: todoApiUrl,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
