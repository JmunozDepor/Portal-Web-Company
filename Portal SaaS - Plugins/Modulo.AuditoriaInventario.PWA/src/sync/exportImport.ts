import { getSesiones, getCapturasBySesion } from '../db/repositories';
import type { CapturaRow } from '../db/schema';

export interface ExportPayload {
  exportedAt: string;
  companyCode: string;
  deviceLabel: string;
  sesiones: Array<{
    id: string;
    branchId: number;
    inventoryNumber: string;
    startedAt: string;
    status: 'ACTIVE' | 'CLOSED';
    validateAgainstMaster: boolean;
  }>;
  capturas: Array<{
    id: string;
    sessionId: string;
    sectorId: number;
    barcode: string;
    productCode: string | null;
    quantity: number;
    inMaster: boolean | null;
    capturedAt: string;
  }>;
}

export async function buildExportPayload(companyCode: string, deviceLabel: string): Promise<ExportPayload> {
  const todasSesiones = await getSesiones();
  const sesionesPendientes = todasSesiones.filter((s) => s.syncStatus !== 'synced');

  const capturasPendientes: CapturaRow[] = [];
  for (const sesion of todasSesiones) {
    const capturas = await getCapturasBySesion(sesion.id);
    capturasPendientes.push(...capturas.filter((c) => c.syncStatus !== 'synced'));
  }

  return {
    exportedAt: new Date().toISOString(),
    companyCode,
    deviceLabel,
    sesiones: sesionesPendientes.map((s) => ({
      id: s.id,
      branchId: s.branchId,
      inventoryNumber: s.inventoryNumber,
      startedAt: s.startedAt,
      status: s.status,
      validateAgainstMaster: s.validateAgainstMaster,
    })),
    capturas: capturasPendientes.map((c) => ({
      id: c.id,
      sessionId: c.sessionId,
      sectorId: c.sectorId,
      barcode: c.barcode,
      productCode: c.productCode,
      quantity: c.quantity,
      inMaster: c.inMaster,
      capturedAt: c.capturedAt,
    })),
  };
}

export function downloadExportPayload(payload: ExportPayload): void {
  const json = JSON.stringify(payload, null, 2);
  const blob = new Blob([json], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `conteo_${payload.companyCode}_${payload.exportedAt.replace(/[:.]/g, '-')}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}
