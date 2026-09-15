import { request } from './client';
import type {
  LoginResponse, MaestroPage, SucursalDto, SectorDto, SesionUpsertBody, CapturaBody,
} from './types';

const ROUTE_PREFIX = '/api/auditoria-inventario/v1';

export function login(companyCode: string, username: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>(`${ROUTE_PREFIX}/auth/login`, {
    method: 'POST',
    body: JSON.stringify({ companyCode, username, password }),
  });
}

export function getProductos(token: string, afterId: number): Promise<MaestroPage> {
  return request<MaestroPage>(`${ROUTE_PREFIX}/maestro/productos?afterId=${afterId}`, { token });
}

export function getSucursales(token: string): Promise<SucursalDto[]> {
  return request<SucursalDto[]>(`${ROUTE_PREFIX}/maestro/sucursales`, { token });
}

export function getSectores(token: string, branchId: number): Promise<SectorDto[]> {
  return request<SectorDto[]>(`${ROUTE_PREFIX}/maestro/sectores?branchId=${branchId}`, { token });
}

export function upsertSesion(token: string, body: SesionUpsertBody): Promise<void> {
  return request<void>(`${ROUTE_PREFIX}/sesiones`, {
    method: 'POST',
    token,
    body: JSON.stringify(body),
  });
}

export function uploadCapturasBatch(token: string, items: CapturaBody[]): Promise<{ processed: number }> {
  return request<{ processed: number }>(`${ROUTE_PREFIX}/capturas/batch`, {
    method: 'POST',
    token,
    body: JSON.stringify(items),
  });
}
