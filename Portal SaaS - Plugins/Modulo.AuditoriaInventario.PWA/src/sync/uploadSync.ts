import { upsertSesion, uploadCapturasBatch } from '../api/endpoints';
import { ApiError } from '../api/client';
import {
  getSesionesPendientes, updateSesion, getCapturasPendientes, updateCaptura,
  getSesion, getSesiones,
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
      // Guarda contra lost update: si la fila cambio mientras el POST estaba en
      // vuelo (p.ej. el operador cerro la sesion), no la marcamos 'synced' —
      // queda 'pending' y el proximo tick sube el estado nuevo.
      const actual = await getSesion(sesion.id);
      if (actual && actual.status !== sesion.status) {
        continue;
      }
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
  const todas = await getCapturasPendientes();
  // Una captura cuya sesion quedo en 'error' referencia un sessionId que el
  // servidor nunca acepto: subirla haria fallar el lote completo y arrastraria
  // a capturas de sesiones sanas. Se posponen hasta que la sesion se recupere.
  const sesiones = await getSesiones();
  const sesionesEnError = new Set(
    sesiones.filter((s) => s.syncStatus === 'error').map((s) => s.id),
  );
  const pendientes = sesionesEnError.size === 0
    ? todas
    : todas.filter((c) => !sesionesEnError.has(c.sessionId));

  for (let i = 0; i < pendientes.length; i += BATCH_SIZE) {
    const batch = pendientes.slice(i, i + BATCH_SIZE);
    try {
      await uploadCapturasBatch(token, batch.map(toCapturaBody));
      // Las capturas son inmutables una vez creadas (registrarCaptura solo hace
      // add; ningun otro camino las edita), asi que no hay lost update posible
      // y no necesitan la re-lectura previa que si lleva la sesion.
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
