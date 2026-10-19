import { useEffect, useState } from 'react';
import { getSucursales } from '../../db/repositories';
import { listarTareasDisponibles, unirseATarea } from './sesionesService';
import type { SesionAbiertaDto } from '../../api/types';
import type { SesionRow } from '../../db/schema';

/**
 * Lista las tareas de conteo creadas desde el portal que este equipo todavía no
 * tiene (ver sesionesService.listarTareasDisponibles) -- reemplaza al viejo
 * formulario "Nueva sesión": la PWA ya no crea tareas, solo se suma a una.
 */
export function TareasDisponiblesPanel({ token, onUnido }: { token: string; onUnido: (sesion: SesionRow) => void }) {
  const [tareas, setTareas] = useState<SesionAbiertaDto[] | null>(null);
  const [sucursales, setSucursales] = useState<Record<number, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [cargando, setCargando] = useState(false);
  const [uniendoId, setUniendoId] = useState<string | null>(null);

  async function buscar() {
    setCargando(true);
    setError(null);
    try {
      setTareas(await listarTareasDisponibles(token));
    } catch {
      setError('No se pudo conectar con el servidor -- necesitás conexión para ver tareas nuevas.');
      setTareas(null);
    } finally {
      setCargando(false);
    }
  }

  useEffect(() => {
    getSucursales().then((lista) => {
      setSucursales(Object.fromEntries(lista.map((s) => [s.id, s.name])));
    });
    buscar();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  async function handleUnirse(tarea: SesionAbiertaDto) {
    setUniendoId(tarea.id);
    try {
      const sesion = await unirseATarea(tarea);
      onUnido(sesion);
    } finally {
      setUniendoId(null);
    }
  }

  return (
    <div className="form-panel">
      {error && <p role="alert" className="alert">{error}</p>}
      {cargando && <p role="status">Buscando tareas nuevas…</p>}
      {!cargando && tareas !== null && tareas.length === 0 && !error && (
        <p className="empty-state">No hay tareas nuevas para sumarte -- pedile al administrador que cree una desde el portal.</p>
      )}
      {tareas !== null && tareas.length > 0 && (
        <ul className="ledger">
          {tareas.map((t) => (
            <li key={t.id}>
              <div className="ledger-row">
                <div className="ledger-row__title">
                  <span>{t.inventoryNumber}</span>
                  <span className="tag tag--active">{sucursales[t.branchId] ?? `Sucursal #${t.branchId}`}</span>
                </div>
                <button
                  type="button"
                  className="btn btn--primary btn--sm"
                  disabled={uniendoId === t.id}
                  onClick={() => handleUnirse(t)}
                >
                  {uniendoId === t.id ? 'Sumando…' : 'Sumarme'}
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
      <button type="button" className="btn btn--ghost" onClick={buscar} disabled={cargando}>
        Buscar tareas nuevas
      </button>
    </div>
  );
}
