import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { listarTareasDisponibles, unirseATarea, cerrarSesion } from '../../../src/features/sesiones/sesionesService';
import { getSesion, createSesion } from '../../../src/db/repositories';
import { db } from '../../../src/db/schema';
import type { SesionAbiertaDto } from '../../../src/api/types';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

beforeEach(async () => {
  await db.sesiones.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('sesionesService', () => {
  it('listarTareasDisponibles excluye las que el equipo ya tiene guardadas localmente', async () => {
    await createSesion({
      id: 'ya-local', branchId: 1, inventoryNumber: 'INV-YA-TENGO', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    const abiertas: SesionAbiertaDto[] = [
      { id: 'ya-local', branchId: 1, inventoryNumber: 'INV-YA-TENGO', startedAt: '2026-01-01T00:00:00Z', validateAgainstMaster: true },
      { id: 'nueva', branchId: 1, inventoryNumber: 'INV-NUEVA', startedAt: '2026-01-02T00:00:00Z', validateAgainstMaster: true },
    ];
    vi.stubGlobal('fetch', vi.fn(async () => jsonResponse(abiertas)));

    const disponibles = await listarTareasDisponibles('tok');

    expect(disponibles).toHaveLength(1);
    expect(disponibles[0].id).toBe('nueva');
  });

  it('unirseATarea guarda la tarea localmente como ACTIVE y ya synced (no pending)', async () => {
    const tarea: SesionAbiertaDto = {
      id: 'tarea-1', branchId: 2, inventoryNumber: 'INV-3', startedAt: '2026-01-03T00:00:00Z', validateAgainstMaster: false,
    };

    const sesion = await unirseATarea(tarea);

    expect(sesion.status).toBe('ACTIVE');
    expect(sesion.syncStatus).toBe('synced');
    const guardada = await getSesion('tarea-1');
    expect(guardada?.inventoryNumber).toBe('INV-3');
    expect(guardada?.validateAgainstMaster).toBe(false);
  });

  it('cerrarSesion pasa a CLOSED y vuelve a pending aunque ya estuviera synced', async () => {
    const sesion = await unirseATarea({
      id: 'tarea-2', branchId: 1, inventoryNumber: 'INV-4', startedAt: '2026-01-04T00:00:00Z', validateAgainstMaster: true,
    });

    await cerrarSesion(sesion.id);

    const actualizada = await getSesion(sesion.id);
    expect(actualizada?.status).toBe('CLOSED');
    expect(actualizada?.syncStatus).toBe('pending');
  });
});
