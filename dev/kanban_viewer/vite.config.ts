import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:8765',
        changeOrigin: false,
      },
    },
  },
  test: {
    projects: [
      {
        test: {
          name: 'browser',
          include: ['src/**/*.test.{ts,tsx}'],
          environment: 'jsdom',
          setupFiles: './src/test/setup.ts',
          css: true,
        },
      },
      {
        test: {
          name: 'server',
          include: ['tests/**/*.test.ts'],
          environment: 'node',
        },
      },
    ],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'json-summary'],
      include: ['src/**/*.{ts,tsx}', 'server/**/*.ts', 'shared/**/*.ts'],
      exclude: ['src/main.tsx', 'server/index.ts'],
      thresholds: { statements: 90, lines: 90, functions: 90, branches: 85 },
    },
  },
})
