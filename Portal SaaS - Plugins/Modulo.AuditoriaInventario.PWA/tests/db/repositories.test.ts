import { describe, it, expect, beforeEach } from 'vitest';
import { db } from '../../src/db/schema';
import {
  upsertProductos, getProductoByBarcode, createSesion, getSesion,
  updateSesion, getSesionesPendientes, createCaptura, getCapturasPendientes,
  getPendingCounts,
} from '../../src/db/repositories';

beforeEach(async () => {
  await db.productos.clear();
  await db.sesiones.clear();
  await db.capturas.clear();
});

describe('repositories', () => {
  it('upsertProductos + getProductoByBarcode', async () => {
    await upsertProductos([
      { id: 1, barcode: '7801234567890', productCode: 'P001', description: 'Zapatilla', brand: 'Nike', line: 'Running' },
    ]);
    const found = await getProductoByBarcode('7801234567890');
    expect(found?.productCode).toBe('P001');
  });

  it('createSesion queda pending por defecto y getSesionesPendientes la incluye', async () => {
    await createSesion({
      id: 'sesion-1', branchId: 10, inventoryNumber: 'INV-001', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    const pendientes = await getSesionesPendientes();
    expect(pendientes.map((s) => s.id)).toEqual(['sesion-1']);
  });

  it('updateSesion cambia status y syncStatus', async () => {
    await createSesion({
      id: 'sesion-2', branchId: 10, inventoryNumber: 'INV-002', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: false, syncStatus: 'synced', lastError: null,
    });
    await updateSesion('sesion-2', { status: 'CLOSED', syncStatus: 'pending' });
    const sesion = await getSesion('sesion-2');
    expect(sesion?.status).toBe('CLOSED');
    expect(sesion?.syncStatus).toBe('pending');
  });

  it('getPendingCounts refleja capturas pendientes', async () => {
    await createCaptura({
      id: 'cap-1', sessionId: 'sesion-1', sectorId: 1, barcode: '123', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });
    const counts = await getPendingCounts();
    expect(counts.capturas).toBe(1);
    const pendientes = await getCapturasPendientes();
    expect(pendientes).toHaveLength(1);
  });
});
