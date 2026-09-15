import { describe, it, expect, vi, beforeEach } from 'vitest';
import { syncSesionesPendientes, syncCapturasPendientes } from '../../src/sync/uploadSync';
import * as endpoints from '../../src/api/endpoints';
import { ApiError } from '../../src/api/client';
import { db } from '../../src/db/schema';
import { createSesion, createCaptura, getSesion, getCapturasBySesion } from '../../src/db/repositories';

beforeEach(async () => {
  await db.sesiones.clear();
  await db.capturas.clear();
});

describe('syncSesionesPendientes', () => {
  it('marca synced una sesion pendiente cuando el servidor responde 200', async () => {
    await createSesion({
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockResolvedValue(undefined);

    await syncSesionesPendientes('tok');

    const sesion = await getSesion('s1');
    expect(sesion?.syncStatus).toBe('synced');
  });

  it('marca error (con mensaje) una sesion cuando el servidor responde 400', async () => {
    await createSesion({
      id: 's2', branchId: 1, inventoryNumber: 'INV-2', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockRejectedValue(new ApiError(400, 'InventoryNumber duplicado'));

    await syncSesionesPendientes('tok');

    const sesion = await getSesion('s2');
    expect(sesion?.syncStatus).toBe('error');
    expect(sesion?.lastError).toBe('InventoryNumber duplicado');
  });

  it('deja pending una sesion si hay un error de red', async () => {
    await createSesion({
      id: 's3', branchId: 1, inventoryNumber: 'INV-3', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockRejectedValue(new Error('network down'));

    await syncSesionesPendientes('tok');

    const sesion = await getSesion('s3');
    expect(sesion?.syncStatus).toBe('pending');
  });

  it('un 401 se relanza sin marcar la fila como error', async () => {
    await createSesion({
      id: 's4', branchId: 1, inventoryNumber: 'INV-4', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockRejectedValue(new ApiError(401, 'expirado'));

    await expect(syncSesionesPendientes('tok')).rejects.toMatchObject({ status: 401 });

    const sesion = await getSesion('s4');
    expect(sesion?.syncStatus).toBe('pending');
  });
});

describe('syncCapturasPendientes', () => {
  it('divide en lotes de 100 y sincroniza todo', async () => {
    for (let i = 0; i < 101; i++) {
      await createCaptura({
        id: `cap-${i}`, sessionId: 's1', sectorId: 1, barcode: `B${i}`, productCode: null,
        quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
      });
    }
    const spy = vi.spyOn(endpoints, 'uploadCapturasBatch').mockResolvedValue({ processed: 100 });

    await syncCapturasPendientes('tok');

    expect(spy).toHaveBeenCalledTimes(2);
    expect(spy.mock.calls[0][1]).toHaveLength(100);
    expect(spy.mock.calls[1][1]).toHaveLength(1);
    const capturas = await getCapturasBySesion('s1');
    expect(capturas.every((c) => c.syncStatus === 'synced')).toBe(true);
  });

  it('marca todo el lote como error si el servidor responde 400', async () => {
    await createCaptura({
      id: 'cap-x', sessionId: 's1', sectorId: 1, barcode: 'X', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'uploadCapturasBatch').mockRejectedValue(new ApiError(400, 'SectorId invalido'));

    await syncCapturasPendientes('tok');

    const capturas = await getCapturasBySesion('s1');
    expect(capturas[0].syncStatus).toBe('error');
    expect(capturas[0].lastError).toBe('SectorId invalido');
  });
});
