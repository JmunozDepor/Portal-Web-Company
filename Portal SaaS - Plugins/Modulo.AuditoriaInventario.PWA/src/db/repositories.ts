import { db } from './schema';
import type { AuthConfigRow, ProductoRow, SucursalRow, SectorRow, SesionRow, CapturaRow } from './schema';

export async function getAuthConfig(): Promise<AuthConfigRow | undefined> {
  return db.authConfig.get('singleton');
}

export async function setAuthConfig(cfg: Omit<AuthConfigRow, 'id'>): Promise<void> {
  await db.authConfig.put({ id: 'singleton', ...cfg });
}

export async function clearAuthConfig(): Promise<void> {
  await db.authConfig.delete('singleton');
}

export async function upsertProductos(items: ProductoRow[]): Promise<void> {
  await db.productos.bulkPut(items);
}

export async function getProductoByBarcode(barcode: string): Promise<ProductoRow | undefined> {
  return db.productos.where('barcode').equals(barcode).first();
}

export async function getProductosCount(): Promise<number> {
  return db.productos.count();
}

export async function upsertSucursales(items: SucursalRow[]): Promise<void> {
  await db.sucursales.bulkPut(items);
}

export async function getSucursales(): Promise<SucursalRow[]> {
  return db.sucursales.toArray();
}

export async function upsertSectores(items: SectorRow[]): Promise<void> {
  await db.sectores.bulkPut(items);
}

export async function getSectoresByBranch(branchId: number): Promise<SectorRow[]> {
  return db.sectores.where('branchId').equals(branchId).toArray();
}

export async function createSesion(row: SesionRow): Promise<void> {
  await db.sesiones.add(row);
}

export async function updateSesion(id: string, changes: Partial<SesionRow>): Promise<void> {
  await db.sesiones.update(id, changes);
}

export async function getSesion(id: string): Promise<SesionRow | undefined> {
  return db.sesiones.get(id);
}

export async function getSesiones(): Promise<SesionRow[]> {
  return db.sesiones.toArray();
}

export async function getSesionesPendientes(): Promise<SesionRow[]> {
  return db.sesiones.where('syncStatus').equals('pending').toArray();
}

export async function createCaptura(row: CapturaRow): Promise<void> {
  await db.capturas.add(row);
}

export async function updateCaptura(id: string, changes: Partial<CapturaRow>): Promise<void> {
  await db.capturas.update(id, changes);
}

export async function getCapturasBySesion(sessionId: string): Promise<CapturaRow[]> {
  return db.capturas.where('sessionId').equals(sessionId).toArray();
}

/**
 * Elimina una captura individual -- solo tiene sentido llamarla mientras
 * syncStatus !== 'synced' (verificado por el caller): una vez que el
 * servidor ya la recibio no hay forma de avisarle que se borro (la API no
 * tiene endpoint de borrado de capturas).
 */
export async function deleteCaptura(id: string): Promise<void> {
  await db.capturas.delete(id);
}

/**
 * Suma de `quantity` por sessionId, en una sola consulta (evita N+1 al
 * listar sesiones con su total contado).
 */
export async function getCapturaTotalsBySesionIds(
  sessionIds: string[],
): Promise<Record<string, number>> {
  if (sessionIds.length === 0) return {};
  const filas = await db.capturas.where('sessionId').anyOf(sessionIds).toArray();
  const totales: Record<string, number> = {};
  for (const fila of filas) {
    totales[fila.sessionId] = (totales[fila.sessionId] ?? 0) + fila.quantity;
  }
  return totales;
}

export async function getCapturasPendientes(): Promise<CapturaRow[]> {
  return db.capturas.where('syncStatus').equals('pending').toArray();
}

/**
 * Devuelve las filas en estado 'error' a 'pending' para que el proximo tick de
 * sincronizacion vuelva a intentarlas. Es el unico camino de reintento y lo
 * dispara el boton "Sincronizar ahora".
 */
export async function reintentarErrores(): Promise<{ sesiones: number; capturas: number }> {
  const sesiones = await db.sesiones.where('syncStatus').equals('error')
    .modify({ syncStatus: 'pending', lastError: null });
  const capturas = await db.capturas.where('syncStatus').equals('error')
    .modify({ syncStatus: 'pending', lastError: null });
  return { sesiones, capturas };
}

/**
 * Sesiones que se pueden borrar del equipo sin perder nada: CERRADAS,
 * sincronizadas, Y con TODAS sus capturas tambien sincronizadas (una
 * sesion 'synced' puede igual tener capturas 'pending' que llegaron
 * despues -- no alcanza con mirar el estado de la sesion sola).
 */
export async function getSesionesLimpiables(): Promise<SesionRow[]> {
  const cerradas = await db.sesiones
    .where('status').equals('CLOSED')
    .and((s) => s.syncStatus === 'synced')
    .toArray();

  const limpiables: SesionRow[] = [];
  for (const sesion of cerradas) {
    const pendientes = await db.capturas
      .where('sessionId').equals(sesion.id)
      .and((c) => c.syncStatus !== 'synced')
      .count();
    if (pendientes === 0) limpiables.push(sesion);
  }
  return limpiables;
}

/**
 * Borra del equipo las sesiones devueltas por getSesionesLimpiables() junto
 * con sus capturas -- los datos ya estan en el servidor, esto solo libera
 * espacio local. Nunca toca una sesion/captura que no haya sincronizado.
 */
export async function limpiarSesionesSincronizadas(): Promise<{ sesiones: number; capturas: number }> {
  const limpiables = await getSesionesLimpiables();
  let totalCapturas = 0;
  for (const sesion of limpiables) {
    totalCapturas += await db.capturas.where('sessionId').equals(sesion.id).count();
    await db.capturas.where('sessionId').equals(sesion.id).delete();
    await db.sesiones.delete(sesion.id);
  }
  return { sesiones: limpiables.length, capturas: totalCapturas };
}

export async function getPendingCounts(): Promise<{ sesiones: number; capturas: number; errores: number }> {
  const [sesionesPend, capturasPend, sesionesErr, capturasErr] = await Promise.all([
    db.sesiones.where('syncStatus').equals('pending').count(),
    db.capturas.where('syncStatus').equals('pending').count(),
    db.sesiones.where('syncStatus').equals('error').count(),
    db.capturas.where('syncStatus').equals('error').count(),
  ]);
  return { sesiones: sesionesPend, capturas: capturasPend, errores: sesionesErr + capturasErr };
}
