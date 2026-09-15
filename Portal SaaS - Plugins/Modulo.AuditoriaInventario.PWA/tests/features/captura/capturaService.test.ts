import { describe, it, expect, beforeEach } from 'vitest';
import { registrarCaptura } from '../../../src/features/captura/capturaService';
import { upsertProductos } from '../../../src/db/repositories';
import { db } from '../../../src/db/schema';

beforeEach(async () => {
  await db.productos.clear();
  await db.capturas.clear();
  await upsertProductos([{ id: 1, barcode: 'EXISTE', productCode: 'P1', description: null, brand: null, line: null }]);
});

describe('registrarCaptura', () => {
  it('con validacion activa y barcode en el maestro, completa productCode e inMaster=true', async () => {
    const captura = await registrarCaptura({
      sessionId: 's1', sectorId: 1, barcode: 'EXISTE', quantity: 2, validateAgainstMaster: true,
    });
    expect(captura.productCode).toBe('P1');
    expect(captura.inMaster).toBe(true);
    expect(captura.quantity).toBe(2);
  });

  it('con validacion activa y barcode fuera del maestro, inMaster=false y productCode null', async () => {
    const captura = await registrarCaptura({
      sessionId: 's1', sectorId: 1, barcode: 'NO-EXISTE', quantity: 1, validateAgainstMaster: true,
    });
    expect(captura.productCode).toBeNull();
    expect(captura.inMaster).toBe(false);
  });

  it('con validacion desactivada, no consulta el maestro y deja inMaster en null', async () => {
    const captura = await registrarCaptura({
      sessionId: 's1', sectorId: 1, barcode: 'EXISTE', quantity: 1, validateAgainstMaster: false,
    });
    expect(captura.inMaster).toBeNull();
    expect(captura.productCode).toBeNull();
  });

  it('cada captura tiene un id GUID unico', async () => {
    const c1 = await registrarCaptura({ sessionId: 's1', sectorId: 1, barcode: 'A', quantity: 1, validateAgainstMaster: false });
    const c2 = await registrarCaptura({ sessionId: 's1', sectorId: 1, barcode: 'A', quantity: 1, validateAgainstMaster: false });
    expect(c1.id).not.toBe(c2.id);
  });
});
