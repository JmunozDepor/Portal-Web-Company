export interface LoginResponse {
  token: string;
  expiresAt: string;
  displayName: string;
}

export interface ProductoDto {
  id: number;
  barcode: string;
  productCode: string;
  description: string | null;
  brand: string | null;
  line: string | null;
}

export interface MaestroPage {
  items: ProductoDto[];
  hasMore: boolean;
}

export interface SucursalDto {
  id: number;
  branchCode: string;
  name: string;
}

export interface EmpresaDto {
  companyCode: string;
  name: string;
}

export interface UsuarioDto {
  username: string;
  fullName: string | null;
}

export interface AjustesCapturaDto {
  allowEan8: boolean;
  allowUpcA: boolean;
  allowEan13: boolean;
}

export interface SectorDto {
  id: number;
  name: string;
}

export interface SesionUpsertBody {
  id: string;
  branchId: number;
  inventoryNumber: string;
  startedAt: string;
  status: 'ACTIVE' | 'CLOSED';
  validateAgainstMaster: boolean;
}

/** Tarea de conteo creada desde el portal -- la PWA se suma a una de estas, ya no crea sesiones (2026-10-15). */
export interface SesionAbiertaDto {
  id: string;
  branchId: number;
  inventoryNumber: string;
  startedAt: string;
  validateAgainstMaster: boolean;
}

export interface CapturaBody {
  id: string;
  sessionId: string;
  sectorId: number;
  barcode: string;
  productCode: string | null;
  quantity: number;
  inMaster: boolean | null;
  capturedAt: string;
}
