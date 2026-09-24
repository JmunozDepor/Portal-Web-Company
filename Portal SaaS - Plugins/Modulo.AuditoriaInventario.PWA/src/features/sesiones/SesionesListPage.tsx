import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  getSesiones, getCapturaTotalsBySesionIds, getSesionesLimpiables, limpiarSesionesSincronizadas,
} from '../../db/repositories';
import { NuevaSesionForm } from './NuevaSesionForm';
import { ExportButton } from '../export/ExportButton';
import type { SesionRow } from '../../db/schema';

export function SesionesListPage() {
  const [sesiones, setSesiones] = useState<SesionRow[]>([]);
  const [totales, setTotales] = useState<Record<string, number>>({});
  const [showForm, setShowForm] = useState(false);
  const [limpiablesCount, setLimpiablesCount] = useState(0);
  const [mensajeLimpieza, setMensajeLimpieza] = useState<string | null>(null);

  async function reload() {
    const lista = await getSesiones();
    setSesiones(lista);
    setTotales(await getCapturaTotalsBySesionIds(lista.map((s) => s.id)));
    setLimpiablesCount((await getSesionesLimpiables()).length);
  }

  useEffect(() => {
    reload();
  }, []);

  // Solo libera espacio local -- los datos ya estan en el servidor (la
  // consulta que arma limpiablesCount nunca incluye algo con una captura
  // sin sincronizar, ver getSesionesLimpiables).
  async function handleLimpiar() {
    if (!window.confirm(
      `¿Eliminar del equipo ${limpiablesCount} sesión${limpiablesCount === 1 ? '' : 'es'} cerrada${limpiablesCount === 1 ? '' : 's'} y ya sincronizada${limpiablesCount === 1 ? '' : 's'}? Los datos ya están guardados en el servidor.`,
    )) return;
    const { sesiones: n, capturas } = await limpiarSesionesSincronizadas();
    setMensajeLimpieza(`Se eliminaron ${n} sesión${n === 1 ? '' : 'es'} (${capturas} captura${capturas === 1 ? '' : 's'}) del equipo.`);
    await reload();
  }

  return (
    <div>
      <div className="page-header">
        <h1>Sesiones de conteo</h1>
        {!showForm && (
          <button className="btn btn--ghost" onClick={() => setShowForm(true)}>
            Nueva sesión
          </button>
        )}
      </div>
      {showForm && (
        <NuevaSesionForm
          onCreated={() => {
            setShowForm(false);
            reload();
          }}
        />
      )}
      {sesiones.length === 0 ? (
        <p className="empty-state">Todavía no hay sesiones. Creá una para empezar a contar.</p>
      ) : (
        <ul className="ledger">
          {sesiones.map((s) => (
            <li key={s.id}>
              <Link to={`/sesiones/${s.id}`} className="ledger-row">
                <div className="ledger-row__title">
                  <span>{s.inventoryNumber}</span>
                  <span className={s.status === 'CLOSED' ? 'tag tag--closed' : 'tag tag--active'}>
                    {s.status === 'CLOSED' ? 'Cerrada' : 'Activa'}
                  </span>
                </div>
                <div className="ledger-row__meta">
                  {totales[s.id] ?? 0} unidad{(totales[s.id] ?? 0) === 1 ? '' : 'es'} contada{(totales[s.id] ?? 0) === 1 ? '' : 's'}
                </div>
                <div className="ledger-row__meta">
                  {s.syncStatus === 'synced' ? 'Sincronizada' : s.syncStatus === 'error' ? 'Con error de sync' : 'Pendiente de sincronizar'}
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
      <div className="export-section">
        <ExportButton />
        {mensajeLimpieza && <p role="status" className="status-note">{mensajeLimpieza}</p>}
        <button
          type="button"
          className="btn btn--ghost cleanup-trigger"
          onClick={handleLimpiar}
          disabled={limpiablesCount === 0}
        >
          Limpiar sincronizados {limpiablesCount > 0 && `(${limpiablesCount})`}
        </button>
      </div>
    </div>
  );
}
