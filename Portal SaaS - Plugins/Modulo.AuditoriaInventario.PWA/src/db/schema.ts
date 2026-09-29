import Dexie, { type Table } from 'dexie';

export interface AuthConfigRow {
  id: 'singleton';
  token: string;
  expiresAt: string;
  displayName: string;
  companyCode: string;
}

// Solo lo que la captura necesita para matchear un escaneo (Barcode -> ProductCode)
// -- description/brand/line del servidor se descartan al sincronizar, achica
// bastante el maestro local en equipos que cargan cientos de miles de filas.
export interface ProductoRow {
  id: number;
  barcode: string;
  productCode: string;
}

/** Ajustes del equipo, no de una sesion puntual -- una sola fila. */
export interface AjusteRow {
  id: 'singleton';
  validarProducto: boolean;
}

/**
 * Formatos de codigo de barra que la PWA acepta al escanear -- configurado por el
 * ADMINISTRADOR (Pages/ConfiguracionCaptura en el portal), no por el capturador en
 * el equipo. Se sincroniza como el resto del maestro; nunca se edita localmente.
 */
export interface ConfiguracionCapturaRow {
  id: 'singleton';
  allowEan8: boolean;
  allowUpcA: boolean;
  allowEan13: boolean;
}

export interface SucursalRow {
  id: number;
  branchCode: string;
  name: string;
}

export interface SectorRow {
  id: number;
  branchId: number;
  name: string;
}

export type SyncStatus = 'pending' | 'synced' | 'error';

export interface SesionRow {
  id: string;
  branchId: number;
  inventoryNumber: string;
  startedAt: string;
  status: 'ACTIVE' | 'CLOSED';
  validateAgainstMaster: boolean;
  syncStatus: SyncStatus;
  lastError: string | null;
}

export interface CapturaRow {
  id: string;
  sessionId: string;
  sectorId: number;
  barcode: string;
  productCode: string | null;
  quantity: number;
  inMaster: boolean | null;
  capturedAt: string;
  syncStatus: SyncStatus;
  lastError: string | null;
}

export class AuditoriaInventarioDb extends Dexie {
  authConfig!: Table<AuthConfigRow, string>;
  productos!: Table<ProductoRow, number>;
  sucursales!: Table<SucursalRow, number>;
  sectores!: Table<SectorRow, number>;
  sesiones!: Table<SesionRow, string>;
  capturas!: Table<CapturaRow, string>;
  ajustes!: Table<AjusteRow, string>;
  configuracionCaptura!: Table<ConfiguracionCapturaRow, string>;

  constructor() {
    super('auditoria-inventario-pwa');
    this.version(1).stores({
      authConfig: 'id',
      productos: 'id, barcode',
      sucursales: 'id',
      sectores: 'id, branchId',
      sesiones: 'id, status, syncStatus',
      capturas: 'id, sessionId, syncStatus',
    });
    // v2: tabla de ajustes del equipo ("Validar producto" pasa de ser un check
    // por sesion a un default global, ver Mantenedor). No toca las tablas de v1.
    this.version(2).stores({
      ajustes: 'id',
    });
    // v3: formatos de codigo de barra aceptados -- vienen del ADMINISTRADOR (ver
    // ConfiguracionCapturaRow), tabla separada de "ajustes" porque esa es
    // editable en el equipo y esta no.
    this.version(3).stores({
      configuracionCaptura: 'id',
    });
  }
}

export const db = new AuditoriaInventarioDb();
