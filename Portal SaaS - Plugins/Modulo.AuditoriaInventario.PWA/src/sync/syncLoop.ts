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

  async function runOnce(): Promise<void> {
    if (stopped) return;
    const token = options.getToken();
    if (!token) return;
    try {
      await syncSesionesPendientes(token);
      await syncCapturasPendientes(token);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        stopped = true;
        options.onUnauthorized();
      }
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
