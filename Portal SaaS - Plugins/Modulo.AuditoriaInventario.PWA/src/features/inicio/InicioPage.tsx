import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { TopBar } from '../../components/TopBar';
import { getSesiones, getCapturaTotalsBySesionIds } from '../../db/repositories';
import { NuevaSesionForm } from '../sesiones/NuevaSesionForm';
import type { SesionRow } from '../../db/schema';

export function InicioPage({ onMenuClick }: { onMenuClick: () => void }) {
  const [sesiones, setSesiones] = useState<SesionRow[]>([]);
  const [totales, setTotales] = useState<Record<string, number>>({});
  const [showForm, setShowForm] = useState(false);

  async function reload() {
    const lista = await getSesiones();
    setSesiones(lista);
    setTotales(await getCapturaTotalsBySesionIds(lista.map((s) => s.id)));
  }

  useEffect(() => {
    reload();
  }, []);

  return (
    <div>
      <TopBar title="Auditoría de Inventario" onMenuClick={onMenuClick} />
      <div className="page">
        <div className="page-header">
          <h1>Conteos disponibles</h1>
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
      </div>
    </div>
  );
}
