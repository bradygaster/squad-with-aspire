import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Aspire injects the messaging API URL via service reference environment variables.
// Format: services__messaging-api__https__0 or services__messaging-api__http__0
const messagingApiUrl =
  process.env.services__messaging_api__https__0 ??
  process.env.services__messaging_api__http__0 ??
  'http://localhost:5000'

export default defineConfig({
  plugins: [react()],
  server: {
    port: parseInt(process.env.PORT ?? '5173'),
    proxy: {
      '/api': {
        target: messagingApiUrl,
        changeOrigin: true,
        secure: false,
      },
    },
  },
})
