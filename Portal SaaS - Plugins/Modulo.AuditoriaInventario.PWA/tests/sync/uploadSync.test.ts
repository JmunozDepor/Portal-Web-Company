import { describe, it, expect, vi, beforeEach } from 'vitest';
import { syncSesionesPendientes, syncCapturasPendientes } from '../../src/sync/uploadSync';
import * as endpoints from '../../src/api/endpoints';
import { ApiError } from '../../src/api/client';
import { db } from '../../src/db/schema';
import {
  createSesion, createCaptura, getSesion, getCapturasBySesion, updateSesion, reintentarErrores,
} from '../../src/db/repositories';

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

  it('un 401 se relanza sin marcar las capturas como error', async () => {
    await createCaptura({
      id: 'cap-401', sessionId: 's1', sectorId: 1, barcode: '401', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'uploadCapturasBatch').mockRejectedValue(new ApiError(401, 'expirado'));

    await expect(syncCapturasPendientes('tok')).rejects.toMatchObject({ status: 401 });

    const capturas = await getCapturasBySesion('s1');
    expect(capturas[0].syncStatus).toBe('pending');
  });

  it('deja pending las capturas si hay un error de red', async () => {
    await createCaptura({
      id: 'cap-net', sessionId: 's1', sectorId: 1, barcode: 'NET', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'uploadCapturasBatch').mockRejectedValue(new Error('network down'));

    await syncCapturasPendientes('tok');

    const capturas = await getCapturasBySesion('s1');
    expect(capturas[0].syncStatus).toBe('pending');
  });

  it('marca como error solo el lote que falla, no todas las capturas pendientes', async () => {
    // Create 150 capturas in separate sessions: 100 in session1, 50 in session2
    // This lets us verify batch-scoping without needing complex mock logic
    for (let i = 0; i < 100; i++) {
      await createCaptura({
        id: `cap-b1-${i}`, sessionId: 'sess1', sectorId: 1, barcode: `B1-${i}`, productCode: null,
        quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
      });
    }
    for (let i = 0; i < 50; i++) {
      await createCaptura({
        id: `cap-b2-${i}`, sessionId: 'sess2', sectorId: 1, barcode: `B2-${i}`, productCode: null,
        quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
      });
    }

    // Mock: first batch succeeds, second batch fails
    let callCount = 0;
    vi.spyOn(endpoints, 'uploadCapturasBatch').mockImplementation(async () => {
      callCount++;
      if (callCount === 1) {
        return { processed: 100 };
      }
      throw new ApiError(400, 'lote invalido');
    });

    await syncCapturasPendientes('tok');

    // Verify first batch synced (by session)
    const batch1 = await getCapturasBySesion('sess1');
    expect(batch1).toHaveLength(100);
    expect(batch1.every((c) => c.syncStatus === 'synced')).toBe(true);

    // Verify second batch errored (by session)
    const batch2 = await getCapturasBySesion('sess2');
    expect(batch2).toHaveLength(50);
    expect(batch2.every((c) => c.syncStatus === 'error')).toBe(true);
    expect(batch2[0].lastError).toBe('lote invalido');
  });
});

function sesion(id: string, overrides: Partial<Parameters<typeof createSesion>[0]> = {}) {
  return {
    id, branchId: 1, inventoryNumber: `INV-${id}`, startedAt: '2026-01-01T00:00:00Z',
    status: 'ACTIVE' as const, validateAgainstMaster: true,
    syncStatus: 'pending' as const, lastError: null, ...overrides,
  };
}

function captura(id: string, sessionId: string, overrides: Partial<Parameters<typeof createCaptura>[0]> = {}) {
  return {
    id, sessionId, sectorId: 1, barcode: `B-${id}`, productCode: null, quantity: 1,
    inMaster: null, capturedAt: '2026-01-01T00:00:00Z',
    syncStatus: 'pending' as const, lastError: null, ...overrides,
  };
}

describe('capturas de sesiones en error (I-3)', () => {
  it('no sube capturas cuya sesion quedo marcada en error', async () => {
    await createSesion(sesion('s-err', { syncStatus: 'error', lastError: 'duplicado' }));
    await createSesion(sesion('s-ok', { syncStatus: 'synced' }));
    await createCaptura(captura('c-err', 's-err'));
    await createCaptura(captura('c-ok', 's-ok'));
    const spy = vi.spyOn(endpoints, 'uploadCapturasBatch').mockResolvedValue({ processed: 1 });

    await syncCapturasPendientes('tok');

    // Sin el filtro las dos irian en el mismo lote y un 4xx arrastraria a 'c-ok'.
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy.mock.calls[0][1].map((c) => c.id)).toEqual(['c-ok']);
    expect((await getCapturasBySesion('s-err'))[0].syncStatus).toBe('pending');
    expect((await getCapturasBySesion('s-ok'))[0].syncStatus).toBe('synced');
  });

  it('reintentarErrores devuelve las filas a pending y la siguiente corrida las sube', async () => {
    await createSesion(sesion('s-r', { syncStatus: 'error', lastError: 'transitorio' }));
    await createCaptura(captura('c-r', 's-r', { syncStatus: 'error', lastError: 'transitorio' }));

    const movidas = await reintentarErrores();
    expect(movidas).toEqual({ sesiones: 1, capturas: 1 });

    vi.spyOn(endpoints, 'upsertSesion').mockResolvedValue(undefined);
    const spy = vi.spyOn(endpoints, 'uploadCapturasBatch').mockResolvedValue({ processed: 1 });

    await syncSesionesPendientes('tok');
    await syncCapturasPendientes('tok');

    expect((await getSesion('s-r'))?.syncStatus).toBe('synced');
    expect(spy.mock.calls[0][1].map((c) => c.id)).toEqual(['c-r']);
    expect((await getCapturasBySesion('s-r'))[0].syncStatus).toBe('synced');
  });
});

describe('carrera cierre-vs-subida (I-5)', () => {
  it('no pisa un cierre de sesion ocurrido mientras el POST estaba en vuelo', async () => {
    await createSesion(sesion('s-race'));
    vi.spyOn(endpoints, 'upsertSesion').mockImplementation(async () => {
      // El operador presiona "Cerrar sesion" mientras la peticion viaja.
      await updateSesion('s-race', { status: 'CLOSED', syncStatus: 'pending' });
    });

    await syncSesionesPendientes('tok');

    const row = await getSesion('s-race');
    expect(row?.status).toBe('CLOSED');
    // Debe seguir pending para que el proximo tick suba el estado CLOSED.
    expect(row?.syncStatus).toBe('pending');
  });

  it('marca synced normalmente cuando la fila no cambio durante el envio', async () => {
    await createSesion(sesion('s-quieta'));
    vi.spyOn(endpoints, 'upsertSesion').mockResolvedValue(undefined);

    await syncSesionesPendientes('tok');

    expect((await getSesion('s-quieta'))?.syncStatus).toBe('synced');
  });
});
