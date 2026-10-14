import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';
import packageJson from './package.json';

// Publicación en IIS (ver docs/10-RUNBOOK-IIS-PILOTO.md en "Portal SaaS - Core"):
// el sitio real cuelga PortalSaas.Host OnPremise como IIS Application "/Depor" de
// un sitio compartido (depor-sl.sapenlanube.com:9003), no un dominio propio. Esta
// PWA se publica como OTRA IIS Application hermana en el mismo sitio -- mismo
// criterio que "/Saas_Central"/"/Depor" (cada rol su propia app, mismo puerto).
// SOLO aplica al build de producción (`npm run build`) -- en dev (`npm run dev`)
// Vite sigue sirviendo desde la raíz, sin esto el flujo de testing local de
// siempre (localhost:5173) se rompía. Tiene que ser EXACTO al alias real de la
// IIS Application (2026-10-14: quedó creada como "Depor_pwa", no "Depor-Captura"
// -- si se vuelve a renombrar en IIS, cambiar acá también). Debe empezar y
// terminar con "/".
const RUTA_BASE_PRODUCCION = '/Depor_pwa/';

export default defineConfig(({ command }) => {
  const esBuildDeProduccion = command === 'build';
  const base = esBuildDeProduccion ? RUTA_BASE_PRODUCCION : '/';

  return {
    base,
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
          start_url: base,
          scope: base,
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
  };
});
