import type { ConfiguracionCapturaRow } from '../../db/schema';

/**
 * Solo valida largo + que sea todo numérico (EAN-8/UPC-A/EAN-13), SIN dígito
 * verificador -- un checksum estricto rechazaría códigos internos reales del
 * catálogo que no son GS1 estándar (visto en datos reales de SAP). El objetivo es
 * filtrar escaneos claramente equivocados (QR/DataMatrix, texto), no auditar que
 * el código sea un GTIN válido.
 */
const LARGO_POR_FORMATO: Record<'allowEan8' | 'allowUpcA' | 'allowEan13', number> = {
  allowEan8: 8,
  allowUpcA: 12,
  allowEan13: 13,
};

export function esFormatoValido(barcode: string, cfg: ConfiguracionCapturaRow): boolean {
  if (!/^\d+$/.test(barcode)) return false;

  const largosHabilitados = (Object.keys(LARGO_POR_FORMATO) as Array<keyof typeof LARGO_POR_FORMATO>)
    .filter((clave) => cfg[clave])
    .map((clave) => LARGO_POR_FORMATO[clave]);

  return largosHabilitados.includes(barcode.length);
}
