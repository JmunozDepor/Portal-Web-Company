import type { EmpresaDto, UsuarioDto } from '../../api/types';

/**
 * Copia local de /auth/empresas y /auth/usuarios -- pedido explícito del dueño
 * del proyecto, 2026-09-29: los equipos trabajan en modo offline y no siempre
 * tienen conectividad, así que LoginPage no puede depender de la red para
 * mostrar los desplegables cuando el equipo YA se conectó alguna vez antes.
 * Vive en localStorage (dato público, no sensible -- nunca la contraseña) y
 * solo se usa como fallback cuando el fetch en vivo falla; si nunca hubo un
 * fetch exitoso no hay nada que mostrar y el equipo sí necesita conectividad
 * una primera vez (mismo criterio que el resto del maestro offline-first).
 */
const KEY_EMPRESAS = 'auditoria-inventario:cache:empresas';
const keyUsuarios = (companyCode: string) => `auditoria-inventario:cache:usuarios:${companyCode}`;

export function guardarEmpresasCache(lista: EmpresaDto[]): void {
  try {
    localStorage.setItem(KEY_EMPRESAS, JSON.stringify(lista));
  } catch {
    // Sin localStorage disponible -- el fallback simplemente no existirá.
  }
}

export function cargarEmpresasCache(): EmpresaDto[] {
  try {
    const raw = localStorage.getItem(KEY_EMPRESAS);
    return raw ? (JSON.parse(raw) as EmpresaDto[]) : [];
  } catch {
    return [];
  }
}

export function guardarUsuariosCache(companyCode: string, lista: UsuarioDto[]): void {
  try {
    localStorage.setItem(keyUsuarios(companyCode), JSON.stringify(lista));
  } catch {
    // Sin localStorage disponible -- el fallback simplemente no existirá.
  }
}

export function cargarUsuariosCache(companyCode: string): UsuarioDto[] {
  try {
    const raw = localStorage.getItem(keyUsuarios(companyCode));
    return raw ? (JSON.parse(raw) as UsuarioDto[]) : [];
  } catch {
    return [];
  }
}
