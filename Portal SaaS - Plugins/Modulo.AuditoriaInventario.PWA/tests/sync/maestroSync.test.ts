import { describe, it, expect, vi } from 'vitest';
import { syncProductos, syncSucursales } from '../../src/sync/maestroSync';
import * as endpoints from '../../src/api/endpoints';
import { db } from '../../src/db/schema';
import { getProductoByBarcode } from '../../src/db/repositories';

describe('syncProductos', () => {
  it('pagina hasta HasMore=false usando el ultimo Id de cada pagina como afterId', async () => {
    const spy = vi.spyOn(endpoints, 'getProductos');
    spy.mockResolvedValueOnce({
      items: [{ id: 1, barcode: 'A', productCode: 'P1', description: null, brand: null, line: null }],
      hasMore: true,
    });
    spy.mockResolvedValueOnce({
      items: [{ id: 2, barcode: 'B', productCode: 'P2', description: null, brand: null, line: null }],
      hasMore: false,
    });

    const total = await syncProductos('tok');

    expect(total).toBe(2);
    expect(spy).toHaveBeenNthCalledWith(1, 'tok', 0);
    expect(spy).toHaveBeenNthCalledWith(2, 'tok', 1);
    const producto = await getProductoByBarcode('B');
    expect(producto?.productCode).toBe('P2');
  });

  it('sale del loop si el servidor devuelve pagina vacia con hasMore=true', async () => {
    const spy = vi.spyOn(endpoints, 'getProductos');
    spy.mockResolvedValueOnce({
      items: [],
      hasMore: true,
    });

    const total = await syncProductos('tok');

    expect(total).toBe(0);
    expect(spy).toHaveBeenCalledTimes(1);
  });
});

describe('syncSucursales', () => {
  it('guarda todas las sucursales devueltas', async () => {
    await db.sucursales.clear();
    vi.spyOn(endpoints, 'getSucursales').mockResolvedValueOnce([
      { id: 1, branchCode: 'B01', name: 'Casa Matriz' },
      { id: 2, branchCode: 'B02', name: 'Sucursal Norte' },
    ]);

    const total = await syncSucursales('tok');

    expect(total).toBe(2);
    const rows = await db.sucursales.toArray();
    expect(rows).toHaveLength(2);
  });
});
