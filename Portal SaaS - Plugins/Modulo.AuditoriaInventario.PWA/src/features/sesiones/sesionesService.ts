import { getSesionesAbiertas } from '../../api/endpoints';
import { createSesion, getSesiones, updateSesion } from '../../db/repositories';
import type { SesionRow } from '../../db/schema';
import type { SesionAbiertaDto } from '../../api/types';

/**
 * Tareas de conteo creadas desde el portal, que este equipo todavía no tiene
 * guardadas localmente -- 2026-10-15: la PWA ya no crea sesiones (antes
 * crearSesion generaba un GUID local y lo subía al primer sync); el portal las
 * crea de antemano y acá solo se lista lo que falta "tomar". Requiere
 * conectividad para ver tareas nuevas (decisión explícita del dueño del
 * proyecto), igual que ya hace falta para bajar sucursales/productos.
 */
export async function listarTareasDisponibles(token: string): Promise<SesionAbiertaDto[]> {
  const abiertas = await getSesionesAbiertas(token);
  const locales = await getSesiones();
  const idsLocales = new Set(locales.map((s) => s.id));
  return abiertas.filter((t) => !idsLocales.has(t.id));
}

/**
 * Guarda localmente una tarea que ya existe en el servidor -- syncStatus
 * 'synced' de entrada (no hay nada que subir, el portal ya la creó), a
 * diferencia de la vieja crearSesion que nacía 'pending'.
 */
export async function unirseATarea(tarea: SesionAbiertaDto): Promise<SesionRow> {
  const row: SesionRow = {
    id: tarea.id,
    branchId: tarea.branchId,
    inventoryNumber: tarea.inventoryNumber,
    startedAt: tarea.startedAt,
    status: 'ACTIVE',
    validateAgainstMaster: tarea.validateAgainstMaster,
    syncStatus: 'synced',
    lastError: null,
  };
  await createSesion(row);
  return row;
}

export async function cerrarSesion(sesionId: string): Promise<void> {
  await updateSesion(sesionId, { status: 'CLOSED', syncStatus: 'pending' });
}
