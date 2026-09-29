/**
 * Version + hora de arranque del build actual -- fijadas por vite.config.ts al
 * momento de `npm run dev`/`npm run build` (ver define ahí). Pensado para que el
 * capturador pueda confirmar a simple vista, desde Login, que está viendo la
 * versión recién desplegada y no una pestaña/puerto viejo con hot-reload
 * desconectado (confusión real 2026-09-29 entre :5173 y :5174 con dos `npm run
 * dev` sueltos al mismo tiempo).
 */
export const APP_VERSION = __APP_VERSION__;

export const BUILD_LABEL = (() => {
  const fecha = new Date(__BUILD_TIME__);
  const hora = fecha.toLocaleTimeString('es-CL', { hour: '2-digit', minute: '2-digit' });
  const dia = fecha.toLocaleDateString('es-CL', { day: '2-digit', month: '2-digit' });
  return `v${APP_VERSION} — compilado ${dia} ${hora}`;
})();
