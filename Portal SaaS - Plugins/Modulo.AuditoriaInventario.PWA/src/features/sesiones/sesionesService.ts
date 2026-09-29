import { createSesion, updateSesion } from '../../db/repositories';
import type { SesionRow } from '../../db/schema';

export async function crearSesion(input: {
  branchId: number;
  inventoryNumber: string;
}): Promise<SesionRow> {
  // "Validar contra maestro" paso de ser un check por sesion a un ajuste
  // global del equipo (ver Mantenedor) -- este campo se manda igual porque el
  // contrato del servidor lo espera, pero ya no gobierna nada del lado de la
  // PWA (CapturaPage lee getAjustes() en vivo, no esto).
  const row: SesionRow = {
    id: crypto.randomUUID(),
    branchId: input.branchId,
    inventoryNumber: input.inventoryNumber,
    startedAt: new Date().toISOString(),
    status: 'ACTIVE',
    validateAgainstMaster: true,
    syncStatus: 'pending',
    lastError: null,
  };
  await createSesion(row);
  return row;
}

export async function cerrarSesion(sesionId: string): Promise<void> {
  await updateSesion(sesionId, { status: 'CLOSED', syncStatus: 'pending' });
}
