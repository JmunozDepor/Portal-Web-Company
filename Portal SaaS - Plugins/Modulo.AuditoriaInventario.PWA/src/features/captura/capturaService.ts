import { getProductoByBarcode, createCaptura } from '../../db/repositories';
import type { CapturaRow } from '../../db/schema';

export async function registrarCaptura(input: {
  sessionId: string;
  sectorId: number;
  barcode: string;
  quantity: number;
  validateAgainstMaster: boolean;
}): Promise<CapturaRow> {
  let productCode: string | null = null;
  let inMaster: boolean | null = null;

  if (input.validateAgainstMaster) {
    const producto = await getProductoByBarcode(input.barcode);
    if (producto) {
      productCode = producto.productCode;
      inMaster = true;
    } else {
      inMaster = false;
    }
  }

  const row: CapturaRow = {
    id: crypto.randomUUID(),
    sessionId: input.sessionId,
    sectorId: input.sectorId,
    barcode: input.barcode,
    productCode,
    quantity: input.quantity,
    inMaster,
    capturedAt: new Date().toISOString(),
    syncStatus: 'pending',
    lastError: null,
  };
  await createCaptura(row);
  return row;
}
