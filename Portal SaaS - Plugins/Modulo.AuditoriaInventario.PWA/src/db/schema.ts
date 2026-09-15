import Dexie, { type Table } from 'dexie';

export interface AuthConfigRow {
  id: 'singleton';
  token: string;
  expiresAt: string;
  displayName: string;
  companyCode: string;
}

export interface ProductoRow {
  id: number;
  barcode: string;
  productCode: string;
  description: string | null;
  brand: string | null;
  line: string | null;
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
  }
}

export const db = new AuditoriaInventarioDb();
