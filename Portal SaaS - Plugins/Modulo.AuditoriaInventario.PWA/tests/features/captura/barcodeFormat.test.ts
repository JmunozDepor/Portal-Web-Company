import { describe, it, expect } from 'vitest';
import { esFormatoValido } from '../../../src/features/captura/barcodeFormat';
import type { ConfiguracionCapturaRow } from '../../../src/db/schema';

const TODOS_HABILITADOS: ConfiguracionCapturaRow = {
  id: 'singleton', allowEan8: true, allowUpcA: true, allowEan13: true,
};

describe('esFormatoValido', () => {
  it('acepta EAN-8 (8 dígitos) si está habilitado', () => {
    expect(esFormatoValido('12345678', TODOS_HABILITADOS)).toBe(true);
  });

  it('acepta UPC-A (12 dígitos) si está habilitado', () => {
    expect(esFormatoValido('123456789012', TODOS_HABILITADOS)).toBe(true);
  });

  it('acepta EAN-13 (13 dígitos) si está habilitado', () => {
    expect(esFormatoValido('1234567890123', TODOS_HABILITADOS)).toBe(true);
  });

  it('rechaza texto no numérico (payload de QR/DataMatrix, código con letras)', () => {
    expect(esFormatoValido('NO-EXISTE', TODOS_HABILITADOS)).toBe(false);
    expect(esFormatoValido('https://ejemplo.com', TODOS_HABILITADOS)).toBe(false);
  });

  it('rechaza largos que no son 8, 12 o 13', () => {
    expect(esFormatoValido('123', TODOS_HABILITADOS)).toBe(false);
    expect(esFormatoValido('12345678901234567890', TODOS_HABILITADOS)).toBe(false);
  });

  it('respeta el formato deshabilitado por el administrador aunque el largo sea correcto', () => {
    const soloEan13: ConfiguracionCapturaRow = { id: 'singleton', allowEan8: false, allowUpcA: false, allowEan13: true };
    expect(esFormatoValido('12345678', soloEan13)).toBe(false);
    expect(esFormatoValido('123456789012', soloEan13)).toBe(false);
    expect(esFormatoValido('1234567890123', soloEan13)).toBe(true);
  });

  it('no valida dígito verificador -- solo largo y que sea numérico', () => {
    // "checksum inválido" a propósito -- códigos internos reales no siempre son GS1 estándar.
    expect(esFormatoValido('0000000000000', TODOS_HABILITADOS)).toBe(true);
  });
});
