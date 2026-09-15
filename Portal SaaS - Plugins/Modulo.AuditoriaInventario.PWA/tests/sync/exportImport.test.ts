import { describe, it, expect, beforeEach } from 'vitest';
import { buildExportPayload } from '../../src/sync/exportImport';
import { createSesion, createCaptura } from '../../src/db/repositories';
import { db } from '../../src/db/schema';

beforeEach(async () => {
  await db.sesiones.clear();
  await db.capturas.clear();
});

describe('buildExportPayload', () => {
  it('incluye sesiones y capturas pending/error, excluye las synced', async () => {
    await createSesion({
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    await createSesion({
      id: 's2', branchId: 1, inventoryNumber: 'INV-2', startedAt: '2026-01-01T00:00:00Z',
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'c1', sessionId: 's1', sectorId: 1, barcode: 'A', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'error', lastError: 'x',
    });
    await createCaptura({
      id: 'c2', sessionId: 's2', sectorId: 1, barcode: 'B', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'synced', lastError: null,
    });

    const payload = await buildExportPayload('DEPOR', 'skorpio-1');

    expect(payload.sesiones.map((s) => s.id)).toEqual(['s1']);
    expect(payload.capturas.map((c) => c.id)).toEqual(['c1']);
    expect(payload.companyCode).toBe('DEPOR');
  });

  it('el payload sobrevive un round-trip de JSON', async () => {
    await createSesion({
      id: 's3', branchId: 2, inventoryNumber: 'INV-3', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: false, syncStatus: 'pending', lastError: null,
    });

    const payload = await buildExportPayload('DEPOR', '');
    const roundTripped = JSON.parse(JSON.stringify(payload));

    expect(roundTripped).toEqual(payload);
  });
});
