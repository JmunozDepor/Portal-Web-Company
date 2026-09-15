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

export async function getCapturasPendientes(): Promise<CapturaRow[]> {
  return db.capturas.where('syncStatus').equals('pending').toArray();
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
