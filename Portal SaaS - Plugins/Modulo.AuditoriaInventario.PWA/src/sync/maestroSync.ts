import { getProductos, getSucursales, getAjustesCaptura } from '../api/endpoints';
import { upsertProductos, upsertSucursales, setConfiguracionCaptura } from '../db/repositories';

export async function syncProductos(token: string, onProgress?: (count: number) => void): Promise<number> {
  let afterId = 0;
  let total = 0;
  let hasMore = true;

  while (hasMore) {
    const page = await getProductos(token, afterId);
    if (page.items.length === 0) {
      break; // Salir si la página está vacía, previene loop infinito con hasMore=true
    }
    // El maestro local solo guarda Barcode/ProductCode (ver ProductoRow) -- el
    // resto del DTO del servidor (description/brand/line) se descarta acá.
    await upsertProductos(page.items.map((p) => ({ id: p.id, barcode: p.barcode, productCode: p.productCode })));
    afterId = page.items[page.items.length - 1].id;
    total += page.items.length;
    onProgress?.(total);
    hasMore = page.hasMore;
  }

  return total;
}

export async function syncSucursales(token: string): Promise<number> {
  const items = await getSucursales(token);
  await upsertSucursales(items);
  return items.length;
}

/** Formatos de codigo de barra habilitados por el administrador -- ver ConfiguracionCaptura (admin-only). */
export async function syncAjustesCaptura(token: string): Promise<void> {
  const ajustes = await getAjustesCaptura(token);
  await setConfiguracionCaptura(ajustes);
}
