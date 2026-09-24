import { describe, it, expect, beforeEach } from 'vitest';
import { db } from '../../src/db/schema';
import {
  upsertProductos, getProductoByBarcode, createSesion, getSesion,
  updateSesion, getSesionesPendientes, createCaptura, getCapturasPendientes,
  getPendingCounts, getProductosCount, reintentarErrores,
  getSesionesLimpiables, limpiarSesionesSincronizadas, getCapturasBySesion,
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

  it('getProductosCount informa si el maestro local esta vacio', async () => {
    expect(await getProductosCount()).toBe(0);
    await upsertProductos([
      { id: 1, barcode: '1', productCode: 'P1', description: null, brand: null, line: null },
      { id: 2, barcode: '2', productCode: 'P2', description: null, brand: null, line: null },
    ]);
    expect(await getProductosCount()).toBe(2);
  });

  it('reintentarErrores devuelve a pending las filas en error y limpia lastError', async () => {
    await createSesion({
      id: 'sesion-err', branchId: 10, inventoryNumber: 'INV-E', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'error', lastError: 'boom',
    });
    await createSesion({
      id: 'sesion-ok', branchId: 10, inventoryNumber: 'INV-O', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'cap-err', sessionId: 'sesion-err', sectorId: 1, barcode: '9', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'error', lastError: 'boom',
    });

    const movidas = await reintentarErrores();

    expect(movidas).toEqual({ sesiones: 1, capturas: 1 });
    const sesionErr = await getSesion('sesion-err');
    expect(sesionErr?.syncStatus).toBe('pending');
    expect(sesionErr?.lastError).toBeNull();
    expect((await getSesion('sesion-ok'))?.syncStatus).toBe('synced');
    expect((await getCapturasPendientes()).map((c) => c.id)).toEqual(['cap-err']);
  });

  it('getSesionesLimpiables solo incluye cerradas+sincronizadas sin ninguna captura pendiente', async () => {
    // Limpiable: cerrada, sincronizada, y su unica captura tambien sincronizo.
    await createSesion({
      id: 'ses-limpia', branchId: 1, inventoryNumber: 'INV-1', startedAt: new Date().toISOString(),
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'cap-1', sessionId: 'ses-limpia', sectorId: 1, barcode: '1', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'synced', lastError: null,
    });

    // No limpiable: cerrada y sincronizada, PERO le quedo una captura pending
    // (llego despues de que la sesion ya se marco synced).
    await createSesion({
      id: 'ses-con-pendiente', branchId: 1, inventoryNumber: 'INV-2', startedAt: new Date().toISOString(),
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'cap-2', sessionId: 'ses-con-pendiente', sectorId: 1, barcode: '2', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });

    // No limpiable: todavia activa.
    await createSesion({
      id: 'ses-activa', branchId: 1, inventoryNumber: 'INV-3', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });

    const limpiables = await getSesionesLimpiables();
    expect(limpiables.map((s) => s.id)).toEqual(['ses-limpia']);
  });

  it('limpiarSesionesSincronizadas borra la sesion limpiable y sus capturas, sin tocar el resto', async () => {
    await createSesion({
      id: 'ses-limpia', branchId: 1, inventoryNumber: 'INV-1', startedAt: new Date().toISOString(),
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'cap-1', sessionId: 'ses-limpia', sectorId: 1, barcode: '1', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'synced', lastError: null,
    });
    await createSesion({
      id: 'ses-con-pendiente', branchId: 1, inventoryNumber: 'INV-2', startedAt: new Date().toISOString(),
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'cap-2', sessionId: 'ses-con-pendiente', sectorId: 1, barcode: '2', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });

    const resultado = await limpiarSesionesSincronizadas();

    expect(resultado).toEqual({ sesiones: 1, capturas: 1 });
    expect(await getSesion('ses-limpia')).toBeUndefined();
    expect(await getCapturasBySesion('ses-limpia')).toHaveLength(0);
    // La que tenia una captura pendiente queda intacta, con su captura.
    expect(await getSesion('ses-con-pendiente')).not.toBeUndefined();
    expect(await getCapturasBySesion('ses-con-pendiente')).toHaveLength(1);
  });
});
