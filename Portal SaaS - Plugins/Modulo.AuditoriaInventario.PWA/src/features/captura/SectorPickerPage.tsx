import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { TopBar } from '../../components/TopBar';
import { getSesion, getCapturaTotalsBySesionYSectores } from '../../db/repositories';
import { getOrFetchSectores } from '../../sync/sectoresSync';
import { cerrarSesion } from '../sesiones/sesionesService';
import type { SesionRow, SectorRow } from '../../db/schema';

const SECTORES_ERROR_MSG =
  'No hay sectores cacheados para esta sucursal — conéctate a la red una vez e inténtalo de nuevo.';

export function SectorPickerPage({ token, onMenuClick }: { token: string; onMenuClick: () => void }) {
  const { sesionId } = useParams<{ sesionId: string }>();
  const navigate = useNavigate();
  const [sesion, setSesion] = useState<SesionRow | null | undefined>(undefined);
  const [sectores, setSectores] = useState<SectorRow[]>([]);
  const [totales, setTotales] = useState<Record<number, number>>({});
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!sesionId) return;
    let cancelado = false;
    (async () => {
      const s = await getSesion(sesionId);
      if (cancelado) return;
      setSesion(s ?? null);
      if (!s) return;
      try {
        const lista = await getOrFetchSectores(token, s.branchId);
        if (cancelado) return;
        setSectores(lista);
        setError(null);
        setTotales(await getCapturaTotalsBySesionYSectores(sesionId, lista.map((sec) => sec.id)));
      } catch {
        if (cancelado) return;
        setSectores([]);
        setError(SECTORES_ERROR_MSG);
      }
    })();
    return () => { cancelado = true; };
  }, [sesionId, token]);

  async function handleCerrarSesion() {
    if (!sesion || sesion.status === 'CLOSED') return;
    if (!window.confirm('¿Cerrar esta sesión de conteo? No vas a poder registrar más capturas.')) return;
    await cerrarSesion(sesion.id);
    setSesion(await getSesion(sesion.id) ?? null);
  }

  if (sesion === undefined) return <p>Cargando sesión...</p>;
  if (sesion === null) return <p role="alert" className="alert">La sesión no existe.</p>;

  const cerrada = sesion.status === 'CLOSED';

  return (
    <div>
      <TopBar title={`Captura — ${sesion.inventoryNumber}`} onMenuClick={onMenuClick} />
      <div className="page">
        <Link to="/sesiones" className="back-link">← Sesiones</Link>
        <div className="page-header">
          <h1>Elegí un sector</h1>
          {cerrada
            ? <span className="tag tag--closed" role="status">Sesión cerrada.</span>
            : <button type="button" className="btn btn--danger" onClick={handleCerrarSesion}>Cerrar sesión</button>}
        </div>
        {error && <p role="alert" className="alert">{error}</p>}
        {!error && sectores.length === 0 && <p className="empty-state">No hay sectores para esta sucursal.</p>}
        <div className="sector-pick">
          {sectores.map((s) => (
            <button
              key={s.id}
              type="button"
              className="sector-card"
              onClick={() => navigate(`/sesiones/${sesionId}/sectores/${s.id}`)}
            >
              <span>{s.name}</span>
              <small>
                {(totales[s.id] ?? 0) > 0 ? `${totales[s.id]} unidad${totales[s.id] === 1 ? '' : 'es'} contada${totales[s.id] === 1 ? '' : 's'}` : 'Sin capturas todavía'}
              </small>
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
