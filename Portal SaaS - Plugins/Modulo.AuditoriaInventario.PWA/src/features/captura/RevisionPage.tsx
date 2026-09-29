import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { TopBar } from '../../components/TopBar';
import { getSesion, getCapturasBySesion, updateCaptura, deleteCaptura } from '../../db/repositories';
import { getOrFetchSectores } from '../../sync/sectoresSync';
import type { SesionRow, SectorRow, CapturaRow } from '../../db/schema';

export function RevisionPage({ token, onMenuClick }: { token: string; onMenuClick: () => void }) {
  const { sesionId, sectorId: sectorIdParam } = useParams<{ sesionId: string; sectorId: string }>();
  const sectorId = sectorIdParam ? Number(sectorIdParam) : NaN;
  const [sesion, setSesion] = useState<SesionRow | null>(null);
  const [sector, setSector] = useState<SectorRow | null>(null);
  const [capturas, setCapturas] = useState<CapturaRow[]>([]);

  async function recargar() {
    if (!sesionId) return;
    setCapturas(await getCapturasBySesion(sesionId));
  }

  useEffect(() => {
    if (!sesionId) return;
    let cancelado = false;
    (async () => {
      const s = await getSesion(sesionId);
      if (cancelado) return;
      setSesion(s ?? null);
      if (s) {
        try {
          const lista = await getOrFetchSectores(token, s.branchId);
          if (cancelado) return;
          setSector(lista.find((sec) => sec.id === sectorId) ?? null);
        } catch {
          if (!cancelado) setSector(null);
        }
      }
      await recargar();
    })();
    return () => { cancelado = true; };
  }, [sesionId, sectorId, token]);

  async function handleEditarCantidad(capturaId: string, nuevaCantidad: number) {
    if (!Number.isFinite(nuevaCantidad) || nuevaCantidad < 1) return;
    await updateCaptura(capturaId, { quantity: nuevaCantidad });
    await recargar();
  }

  async function handleEliminarCaptura(capturaId: string) {
    if (!window.confirm('¿Eliminar este registro de captura?')) return;
    await deleteCaptura(capturaId);
    await recargar();
  }

  if (!sesion) return <p>Cargando sesión...</p>;

  const capturasSector = capturas
    .filter((c) => c.sectorId === sectorId)
    .sort((a, b) => b.capturedAt.localeCompare(a.capturedAt));
  const resumenPorCodigo = Object.values(
    capturasSector.reduce<Record<string, { barcode: string; total: number }>>((acc, c) => {
      const entry = acc[c.barcode] ?? { barcode: c.barcode, total: 0 };
      entry.total += c.quantity;
      acc[c.barcode] = entry;
      return acc;
    }, {}),
  );

  return (
    <div>
      <TopBar title={`Revisión — ${sesion.inventoryNumber}`} onMenuClick={onMenuClick} />
      <div className="page">
        <Link to={`/sesiones/${sesionId}/sectores/${sectorId}`} className="back-link">← {sector?.name ?? 'Captura'}</Link>

        <h2 className="summary-panel__title">Resumen por código (sector actual)</h2>
        {resumenPorCodigo.length === 0 ? (
          <p className="empty-state">Sin capturas todavía en este sector.</p>
        ) : (
          <table className="summary-table" style={{ marginBottom: 'var(--space-4)' }}>
            <tbody>
              {resumenPorCodigo.map((r) => (
                <tr key={r.barcode}>
                  <td className="summary-table__barcode">{r.barcode}</td>
                  <td className="summary-table__qty">{r.total}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <h2 className="summary-panel__title">Detalle (editar / eliminar)</h2>
        {capturasSector.length === 0 ? (
          <p className="empty-state">Nada para revisar todavía.</p>
        ) : (
          <div className="capture-list">
            <ul className="capture-list__body">
              {capturasSector.map((c) => {
                const editable = c.syncStatus !== 'synced';
                return (
                  <li key={c.id} className="capture-list__row">
                    <div className="capture-list__line1">
                      <span className="capture-list__barcode">{c.barcode}</span>
                      {editable ? (
                        <input
                          className="capture-list__qty-input"
                          type="number"
                          min={1}
                          value={c.quantity}
                          onChange={(e) => handleEditarCantidad(c.id, Number(e.target.value))}
                          aria-label={`Cantidad de ${c.barcode}`}
                        />
                      ) : (
                        <span className="capture-list__qty">{c.quantity}</span>
                      )}
                      <span className={`capture-list__estado capture-list__estado--${c.syncStatus}`}>
                        {c.syncStatus === 'synced' ? 'Sinc.' : c.syncStatus === 'error' ? 'Error' : 'Pend.'}
                      </span>
                      {editable && (
                        <button
                          type="button"
                          className="capture-list__delete"
                          onClick={() => handleEliminarCaptura(c.id)}
                          aria-label={`Eliminar captura de ${c.barcode}`}
                        >
                          ×
                        </button>
                      )}
                    </div>
                    <div className={c.inMaster === false ? 'capture-list__line2 capture-list__master--missing' : 'capture-list__line2'}>
                      {c.productCode ?? (c.inMaster === false ? 'No maestro' : '—')}
                    </div>
                  </li>
                );
              })}
            </ul>
          </div>
        )}
      </div>
    </div>
  );
}
