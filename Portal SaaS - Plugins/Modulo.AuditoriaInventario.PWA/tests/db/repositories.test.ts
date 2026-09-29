import { describe, it, expect, beforeEach } from 'vitest';
import { db } from '../../src/db/schema';
import {
  upsertProductos, getProductoByBarcode, createSesion, getSesion,
  updateSesion, getSesionesPendientes, createCaptura, getCapturasPendientes,
  getPendingCounts, getProductosCount, reintentarErrores,
  getSesionesLimpiables, limpiarSesionesSincronizadas, getCapturasBySesion,
  getAjustes, setValidarProducto, getCapturaTotalsBySesionYSectores,
  importarMaestroDesdeJson, getConfiguracionCaptura, setConfiguracionCaptura,
} from '../../src/db/repositories';

beforeEach(async () => {
  await db.productos.clear();
  await db.sesiones.clear();
  await db.capturas.clear();
  await db.ajustes.clear();
  await db.configuracionCaptura.clear();
  await db.sucursales.clear();
});

describe('repositories', () => {
  it('upsertProductos + getProductoByBarcode', async () => {
    await upsertProductos([
      { id: 1, barcode: '7801234567890', productCode: 'P001' },
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
      { id: 1, barcode: '1', productCode: 'P1' },
      { id: 2, barcode: '2', productCode: 'P2' },
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

  it('getAjustes devuelve validarProducto=true por defecto si nunca se guardo', async () => {
    expect(await getAjustes()).toEqual({ id: 'singleton', validarProducto: true });
  });

  it('setValidarProducto persiste el valor y getAjustes lo refleja', async () => {
    await setValidarProducto(false);
    expect(await getAjustes()).toEqual({ id: 'singleton', validarProducto: false });

    await setValidarProducto(true);
    expect(await getAjustes()).toEqual({ id: 'singleton', validarProducto: true });
  });

  it('getCapturaTotalsBySesionYSectores suma por sector, acotado a una sesion', async () => {
    await createCaptura({
      id: 'c1', sessionId: 'ses-a', sectorId: 1, barcode: '111', productCode: 'P1',
      quantity: 3, inMaster: true, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });
    await createCaptura({
      id: 'c2', sessionId: 'ses-a', sectorId: 1, barcode: '222', productCode: 'P2',
      quantity: 2, inMaster: true, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });
    await createCaptura({
      id: 'c3', sessionId: 'ses-a', sectorId: 2, barcode: '333', productCode: 'P3',
      quantity: 5, inMaster: true, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });
    // Misma sectorId pero otra sesion: no debe sumarse.
    await createCaptura({
      id: 'c4', sessionId: 'ses-b', sectorId: 1, barcode: '444', productCode: 'P4',
      quantity: 100, inMaster: true, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });

    const totales = await getCapturaTotalsBySesionYSectores('ses-a', [1, 2]);
    expect(totales).toEqual({ 1: 5, 2: 5 });
  });

  it('getCapturaTotalsBySesionYSectores devuelve {} si no hay sectorIds', async () => {
    expect(await getCapturaTotalsBySesionYSectores('ses-a', [])).toEqual({});
  });

  it('importarMaestroDesdeJson hace upsert de productos y sucursales', async () => {
    const resultado = await importarMaestroDesdeJson({
      productos: [{ id: 1, barcode: '7801234567890', productCode: 'P001' }],
      sucursales: [{ id: 10, branchCode: 'SUC-1', name: 'Sucursal Centro' }],
    });

    expect(resultado).toEqual({ productos: 1, sucursales: 1 });
    expect(await getProductoByBarcode('7801234567890')).toMatchObject({ productCode: 'P001' });
    expect(await db.sucursales.get(10)).toMatchObject({ name: 'Sucursal Centro' });
  });

  it('importarMaestroDesdeJson tolera payload vacio', async () => {
    expect(await importarMaestroDesdeJson({})).toEqual({ productos: 0, sucursales: 0 });
  });

  it('getConfiguracionCaptura devuelve los tres formatos habilitados por defecto si nunca se sincronizo', async () => {
    expect(await getConfiguracionCaptura()).toEqual({
      id: 'singleton', allowEan8: true, allowUpcA: true, allowEan13: true,
    });
  });

  it('setConfiguracionCaptura persiste lo que sincroniza el administrador', async () => {
    await setConfiguracionCaptura({ allowEan8: false, allowUpcA: true, allowEan13: true });
    expect(await getConfiguracionCaptura()).toEqual({
      id: 'singleton', allowEan8: false, allowUpcA: true, allowEan13: true,
    });
  });
});
