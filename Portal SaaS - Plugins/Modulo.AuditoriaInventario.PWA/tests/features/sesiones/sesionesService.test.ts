import { describe, it, expect, beforeEach } from 'vitest';
import { crearSesion, cerrarSesion } from '../../../src/features/sesiones/sesionesService';
import { getSesion } from '../../../src/db/repositories';
import { db } from '../../../src/db/schema';

beforeEach(async () => {
  await db.sesiones.clear();
});

describe('sesionesService', () => {
  it('crearSesion genera un GUID, queda ACTIVE y pending', async () => {
    const sesion = await crearSesion({ branchId: 1, inventoryNumber: 'INV-1', validateAgainstMaster: true });
    expect(sesion.id).toMatch(/^[0-9a-f-]{36}$/);
    expect(sesion.status).toBe('ACTIVE');
    expect(sesion.syncStatus).toBe('pending');
  });

  it('cerrarSesion pasa a CLOSED y vuelve a pending aunque ya estuviera synced', async () => {
    const sesion = await crearSesion({ branchId: 1, inventoryNumber: 'INV-2', validateAgainstMaster: false });
    await db.sesiones.update(sesion.id, { syncStatus: 'synced' });

    await cerrarSesion(sesion.id);

    const actualizada = await getSesion(sesion.id);
    expect(actualizada?.status).toBe('CLOSED');
    expect(actualizada?.syncStatus).toBe('pending');
  });
});
