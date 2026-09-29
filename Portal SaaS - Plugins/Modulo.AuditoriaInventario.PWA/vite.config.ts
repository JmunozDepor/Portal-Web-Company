import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';
import packageJson from './package.json';

export default defineConfig({
  // __APP_VERSION__/__BUILD_TIME__ quedan fijos al momento en que arranca `npm run
  // dev`/`npm run build` -- se muestran en Login/TopBar (ver src/version.ts) para que
  // el capturador pueda confirmar a simple vista que está viendo la versión que se
  // acaba de desplegar, y no una pestaña/puerto viejo con hot-reload desconectado
  // (2026-09-29: confusión real entre :5173 y :5174 con dos `npm run dev` sueltos).
  define: {
    __APP_VERSION__: JSON.stringify(packageJson.version),
    __BUILD_TIME__: JSON.stringify(new Date().toISOString()),
  },
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
