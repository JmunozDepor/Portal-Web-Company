import { request } from './client';
import type {
  LoginResponse, MaestroPage, SucursalDto, SectorDto, SesionUpsertBody, CapturaBody, EmpresaDto, UsuarioDto,
  AjustesCapturaDto,
} from './types';

const ROUTE_PREFIX = '/api/auditoria-inventario/v1';

export function login(companyCode: string, username: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>(`${ROUTE_PREFIX}/auth/login`, {
    method: 'POST',
    body: JSON.stringify({ companyCode, username, password }),
  });
}

/** Sin token: se llama ANTES de loguearse, para el desplegable de Empresa en LoginPage. */
export function getEmpresas(): Promise<EmpresaDto[]> {
  return request<EmpresaDto[]>(`${ROUTE_PREFIX}/auth/empresas`);
}

/** Sin token: se llama ANTES de loguearse, para el desplegable de Usuario en LoginPage. */
export function getUsuarios(companyCode: string): Promise<UsuarioDto[]> {
  return request<UsuarioDto[]>(`${ROUTE_PREFIX}/auth/usuarios?companyCode=${encodeURIComponent(companyCode)}`);
}

export function getProductos(token: string, afterId: number): Promise<MaestroPage> {
  return request<MaestroPage>(`${ROUTE_PREFIX}/maestro/productos?afterId=${afterId}`, { token });
}

/** Total real de productos en el servidor -- para detectar un maestro local sincronizado a medias, ver Mantenedor. */
export function getProductosCountRemoto(token: string): Promise<number> {
  return request<number>(`${ROUTE_PREFIX}/maestro/productos/total`, { token });
}

export function getSucursales(token: string): Promise<SucursalDto[]> {
  return request<SucursalDto[]>(`${ROUTE_PREFIX}/maestro/sucursales`, { token });
}

export function getSectores(token: string, branchId: number): Promise<SectorDto[]> {
  return request<SectorDto[]>(`${ROUTE_PREFIX}/maestro/sectores?branchId=${branchId}`, { token });
}

export function getAjustesCaptura(token: string): Promise<AjustesCapturaDto> {
  return request<AjustesCapturaDto>(`${ROUTE_PREFIX}/maestro/ajustes-captura`, { token });
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
