import { createSesion, updateSesion } from '../../db/repositories';
import type { SesionRow } from '../../db/schema';

export async function crearSesion(input: {
  branchId: number;
  inventoryNumber: string;
  validateAgainstMaster: boolean;
}): Promise<SesionRow> {
  const row: SesionRow = {
    id: crypto.randomUUID(),
    branchId: input.branchId,
    inventoryNumber: input.inventoryNumber,
    startedAt: new Date().toISOString(),
    status: 'ACTIVE',
    validateAgainstMaster: input.validateAgainstMaster,
    syncStatus: 'pending',
    lastError: null,
  };
  await createSesion(row);
  return row;
}

export async function cerrarSesion(sesionId: string): Promise<void> {
  await updateSesion(sesionId, { status: 'CLOSED', syncStatus: 'pending' });
}
