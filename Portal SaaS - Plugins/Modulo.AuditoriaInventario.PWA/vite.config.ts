import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'Auditoría de Inventario - Captura',
        short_name: 'AuditoriaInv',
        start_url: '/',
        display: 'standalone',
        background_color: '#ffffff',
        theme_color: '#1a1a1a',
        icons: [],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg}'],
      },
    }),
  ],
  server: {
    proxy: {
      // Apunta a "Comercial Depor (OnPremise)" de build-all.ps1 -- es la instancia
      // que carga Modulo.AuditoriaInventario y tiene el CaptureUser real. El puerto
      // 5270 (perfil "http" por defecto de dotnet run) no es el que usa este repo:
      // build-all.ps1 fija 6001 (Central)/6002 (Comercial Depor) desde 2026-08-10.
      // 127.0.0.1 explícito, no "localhost": Node en Windows puede resolver
      // "localhost" a ::1 (IPv6) primero y fallar con ECONNREFUSED/AggregateError si
      // Kestrel no quedó escuchando ahí, aunque el mismo host responda por IPv4.
      '/api': {
        target: 'http://127.0.0.1:6002',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./tests/setup.ts'],
  },
});
