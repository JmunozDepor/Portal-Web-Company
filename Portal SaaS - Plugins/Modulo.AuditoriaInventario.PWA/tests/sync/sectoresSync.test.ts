import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getOrFetchSectores } from '../../src/sync/sectoresSync';
import * as endpoints from '../../src/api/endpoints';
import { db } from '../../src/db/schema';

beforeEach(async () => {
  await db.sectores.clear();
});

describe('getOrFetchSectores', () => {
  it('si no hay cache, pide a la API y guarda con branchId', async () => {
    const spy = vi.spyOn(endpoints, 'getSectores').mockResolvedValueOnce([
      { id: 1, name: 'Sala de Venta' },
      { id: 2, name: 'Bodega' },
    ]);

    const result = await getOrFetchSectores('tok', 10);

    expect(spy).toHaveBeenCalledWith('tok', 10);
    expect(result).toEqual([
      { id: 1, branchId: 10, name: 'Sala de Venta' },
      { id: 2, branchId: 10, name: 'Bodega' },
    ]);
    const stored = await db.sectores.where('branchId').equals(10).toArray();
    expect(stored).toHaveLength(2);
  });

  it('si ya hay cache para esa sucursal, no llama a la API', async () => {
    await db.sectores.bulkPut([{ id: 1, branchId: 20, name: 'Bodega' }]);
    const spy = vi.spyOn(endpoints, 'getSectores');

    const result = await getOrFetchSectores('tok', 20);

    expect(spy).not.toHaveBeenCalled();
    expect(result).toEqual([{ id: 1, branchId: 20, name: 'Bodega' }]);
  });
});
