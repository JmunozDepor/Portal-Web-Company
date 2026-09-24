import { describe, it, expect, vi, afterEach } from 'vitest';
import { startSyncLoop } from '../../src/sync/syncLoop';
import * as uploadSync from '../../src/sync/uploadSync';
import { ApiError } from '../../src/api/client';

afterEach(() => {
  vi.restoreAllMocks();
});

describe('startSyncLoop', () => {
  it('no sincroniza sola: ni el paso del tiempo ni el evento online disparan nada', async () => {
    vi.useFakeTimers();
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn() });
    await vi.advanceTimersByTimeAsync(60000);
    window.dispatchEvent(new Event('online'));
    await vi.advanceTimersByTimeAsync(0);

    expect(uploadSync.syncSesionesPendientes).not.toHaveBeenCalled();
    handle.stop();
    vi.useRealTimers();
  });

  it('runOnce (boton "Sincronizar ahora") sube sesiones y capturas pendientes', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn() });
    await handle.runOnce();

    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);
    expect(uploadSync.syncCapturasPendientes).toHaveBeenCalledTimes(1);
    handle.stop();
  });

  it('un 401 detiene el loop y llama onUnauthorized', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockRejectedValue(new ApiError(401, 'expirado'));
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();
    const onUnauthorized = vi.fn();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized });
    await handle.runOnce();
    expect(onUnauthorized).toHaveBeenCalledTimes(1);

    // Detenido: un click posterior (usuario insistiendo) ya no llama a la red.
    await handle.runOnce();
    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);

    handle.stop();
  });

  it('una llamada solapada a runOnce es un no-op mientras hay una corrida en vuelo', async () => {
    let liberar: (() => void) | undefined;
    const enVuelo = new Promise<void>((resolve) => { liberar = resolve; });
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockImplementation(() => enVuelo);
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn() });

    const primera = handle.runOnce();
    // Doble click en "Sincronizar ahora" mientras la primera sigue esperando
    // la red: no debe disparar una segunda subida.
    await handle.runOnce();
    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);

    liberar!();
    await primera;

    // Terminada la primera, una nueva corrida vuelve a estar permitida.
    await handle.runOnce();
    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(2);

    handle.stop();
  });

  it('sin token todavia (auth aun no resuelta) no llama a la red', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();

    const handle = startSyncLoop({ getToken: () => null, onUnauthorized: vi.fn() });
    await handle.runOnce();

    expect(uploadSync.syncSesionesPendientes).not.toHaveBeenCalled();
    handle.stop();
  });
});
