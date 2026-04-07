import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const backendTarget = 'https://localhost:7109'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
      '/Game': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
      '/Matchmaking': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
      '/Inventory': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
      '/DatabaseTest': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
      },
      '/gamehub': {
        target: backendTarget,
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
})
