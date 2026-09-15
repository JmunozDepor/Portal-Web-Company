import { upsertSesion, uploadCapturasBatch } from '../api/endpoints';
import { ApiError } from '../api/client';
import {
  getSesionesPendientes, updateSesion, getCapturasPendientes, updateCaptura,
} from '../db/repositories';
import type { SesionRow, CapturaRow } from '../db/schema';

const BATCH_SIZE = 100;

function toSesionBody(row: SesionRow) {
  return {
    id: row.id,
    branchId: row.branchId,
    inventoryNumber: row.inventoryNumber,
    startedAt: row.startedAt,
    status: row.status,
    validateAgainstMaster: row.validateAgainstMaster,
  };
}

function toCapturaBody(row: CapturaRow) {
  return {
    id: row.id,
    sessionId: row.sessionId,
    sectorId: row.sectorId,
    barcode: row.barcode,
    productCode: row.productCode,
    quantity: row.quantity,
    inMaster: row.inMaster,
    capturedAt: row.capturedAt,
  };
}

function isClientError(err: unknown): err is ApiError {
  return err instanceof ApiError && err.status >= 400 && err.status < 500 && err.status !== 401;
}

export async function syncSesionesPendientes(token: string): Promise<void> {
  const pendientes = await getSesionesPendientes();
  for (const sesion of pendientes) {
    try {
      await upsertSesion(token, toSesionBody(sesion));
      await updateSesion(sesion.id, { syncStatus: 'synced', lastError: null });
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        throw err;
      }
      if (isClientError(err)) {
        await updateSesion(sesion.id, { syncStatus: 'error', lastError: err.message });
      }
      // red o 5xx: la fila queda pending, se reintenta en el siguiente tick
    }
  }
}

export async function syncCapturasPendientes(token: string): Promise<void> {
  const pendientes = await getCapturasPendientes();
  for (let i = 0; i < pendientes.length; i += BATCH_SIZE) {
    const batch = pendientes.slice(i, i + BATCH_SIZE);
    try {
      await uploadCapturasBatch(token, batch.map(toCapturaBody));
      await Promise.all(batch.map((c) => updateCaptura(c.id, { syncStatus: 'synced', lastError: null })));
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        throw err;
      }
      if (isClientError(err)) {
        const message = err.message;
        await Promise.all(batch.map((c) => updateCaptura(c.id, { syncStatus: 'error', lastError: message })));
      }
    }
  }
}
