import { getProductos, getSucursales } from '../api/endpoints';
import { upsertProductos, upsertSucursales } from '../db/repositories';

export async function syncProductos(token: string, onProgress?: (count: number) => void): Promise<number> {
  let afterId = 0;
  let total = 0;
  let hasMore = true;

  while (hasMore) {
    const page = await getProductos(token, afterId);
    if (page.items.length > 0) {
      await upsertProductos(page.items);
      afterId = page.items[page.items.length - 1].id;
      total += page.items.length;
      onProgress?.(total);
    }
    hasMore = page.hasMore;
  }

  return total;
}

export async function syncSucursales(token: string): Promise<number> {
  const items = await getSucursales(token);
  await upsertSucursales(items);
  return items.length;
}
