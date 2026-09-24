import { syncSesionesPendientes, syncCapturasPendientes } from './uploadSync';
import { ApiError } from '../api/client';

export interface SyncLoopOptions {
  getToken: () => string | null;
  onUnauthorized: () => void;
}

export interface SyncLoopHandle {
  stop: () => void;
  runOnce: () => Promise<void>;
}

/**
 * Sincronizacion 100% manual (pedido del dueño del proyecto, 2026-09-22):
 * antes esto corria solo cada 30s y al reconectar, pero eso podia subir un
 * sector a medio contar. Ahora `runOnce` solo se dispara desde el boton
 * "Sincronizar ahora" -- el capturador decide cuando terminó de contar el
 * sector y recien ahí sincroniza.
 */
export function startSyncLoop(options: SyncLoopOptions): SyncLoopHandle {
  let stopped = false;
  let running = false;

  async function runOnce(): Promise<void> {
    if (stopped) return;
    // Guarda de concurrencia: dos clicks rapidos en "Sincronizar ahora" no
    // deben subir dos veces las mismas filas pending.
    if (running) return;
    const token = options.getToken();
    if (!token) return;
    running = true;
    try {
      await syncSesionesPendientes(token);
      await syncCapturasPendientes(token);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        stopped = true;
        options.onUnauthorized();
      }
    } finally {
      running = false;
    }
  }

  function stop() {
    stopped = true;
  }

  return { stop, runOnce };
}
