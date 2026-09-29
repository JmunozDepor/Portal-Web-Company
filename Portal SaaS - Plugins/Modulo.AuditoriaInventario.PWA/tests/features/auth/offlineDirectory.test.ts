import { describe, it, expect, beforeEach } from 'vitest';
import {
  guardarEmpresasCache, cargarEmpresasCache, guardarUsuariosCache, cargarUsuariosCache,
} from '../../../src/features/auth/offlineDirectory';

beforeEach(() => {
  localStorage.clear();
});

describe('offlineDirectory', () => {
  it('cargarEmpresasCache devuelve [] si nunca se guardó nada', () => {
    expect(cargarEmpresasCache()).toEqual([]);
  });

  it('guardarEmpresasCache + cargarEmpresasCache hacen round-trip', () => {
    const lista = [{ companyCode: 'DEPOR', name: 'Comercial Depor' }];
    guardarEmpresasCache(lista);
    expect(cargarEmpresasCache()).toEqual(lista);
  });

  it('cargarUsuariosCache devuelve [] para una empresa sin caché', () => {
    expect(cargarUsuariosCache('DEPOR')).toEqual([]);
  });

  it('guardarUsuariosCache queda separado por empresa', () => {
    guardarUsuariosCache('DEPOR', [{ username: 'jmunoz', fullName: 'Jorge Muñoz' }]);
    guardarUsuariosCache('DEPOR_TEST', [{ username: 'capturatest', fullName: null }]);

    expect(cargarUsuariosCache('DEPOR')).toEqual([{ username: 'jmunoz', fullName: 'Jorge Muñoz' }]);
    expect(cargarUsuariosCache('DEPOR_TEST')).toEqual([{ username: 'capturatest', fullName: null }]);
  });
});
