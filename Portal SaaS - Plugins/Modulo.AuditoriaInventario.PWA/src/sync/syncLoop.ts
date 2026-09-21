import { syncSesionesPendientes, syncCapturasPendientes } from './uploadSync';
import { ApiError } from '../api/client';

export interface SyncLoopOptions {
  getToken: () => string | null;
  onUnauthorized: () => void;
  intervalMs?: number;
}

export interface SyncLoopHandle {
  stop: () => void;
  runOnce: () => Promise<void>;
}

export function startSyncLoop(options: SyncLoopOptions): SyncLoopHandle {
  const intervalMs = options.intervalMs ?? 30000;
  let stopped = false;
  let running = false;

  async function runOnce(): Promise<void> {
    if (stopped) return;
    // Guarda de concurrencia: el tick del intervalo, el evento 'online' y el
    // boton manual pueden solaparse en una conexion lenta y subir dos veces las
    // mismas filas pending. Una invocacion solapada es un no-op.
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

  const onlineListener = () => {
    runOnce();
  };
  window.addEventListener('online', onlineListener);

  const intervalId = setInterval(() => {
    if (navigator.onLine) {
      runOnce();
    }
  }, intervalMs);

  function stop() {
    stopped = true;
    window.removeEventListener('online', onlineListener);
    clearInterval(intervalId);
  }

  return { stop, runOnce };
}
