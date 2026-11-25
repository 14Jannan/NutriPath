/// <reference types="vitest/config" />
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import path from 'node:path';

// In development the portal and the API share one origin through this proxy, so the admin
// refresh cookie stays same-site (SameSite=Strict) and no CORS is needed. In production, serve
// the built files from the same origin as the API (see docs/admin-portal.md).
const apiTarget = process.env.NUTRIPATH_API_URL ?? 'http://localhost:5092';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'src') } },
  server: {
    port: 5173,
    proxy: { '/api': { target: apiTarget, changeOrigin: false } },
  },
  // No manualChunks: Vite 8 also applies it to dev dependency pre-bundling, where it split React
  // into two copies ("Invalid hook call"). Pages are lazy-loaded, so default chunking is enough.
  build: { sourcemap: false },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    // Pages are lazy chunks and dialogs animate; the first test to load a page can take a few seconds.
    testTimeout: 20000,
  },
});
