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
