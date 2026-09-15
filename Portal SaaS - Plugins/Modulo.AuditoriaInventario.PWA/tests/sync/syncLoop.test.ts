import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { startSyncLoop } from '../../src/sync/syncLoop';
import * as uploadSync from '../../src/sync/uploadSync';
import { ApiError } from '../../src/api/client';

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('startSyncLoop', () => {
  it('sincroniza en cada intervalo mientras hay conexion', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn(), intervalMs: 1000 });
    await vi.advanceTimersByTimeAsync(1000);

    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);
    handle.stop();
  });

  it('el evento online dispara una sincronizacion inmediata', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn(), intervalMs: 999999 });
    window.dispatchEvent(new Event('online'));
    await vi.waitFor(() => expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1));

    handle.stop();
  });

  it('un 401 detiene el loop y llama onUnauthorized', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockRejectedValue(new ApiError(401, 'expirado'));
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);
    const onUnauthorized = vi.fn();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized, intervalMs: 1000 });
    await vi.advanceTimersByTimeAsync(1000);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1000);
    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);

    handle.stop();
  });
});
