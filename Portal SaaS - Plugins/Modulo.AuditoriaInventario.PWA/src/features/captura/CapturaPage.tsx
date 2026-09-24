import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { getSesion, getCapturasBySesion, updateCaptura, deleteCaptura } from '../../db/repositories';
import { getOrFetchSectores } from '../../sync/sectoresSync';
import { cerrarSesion } from '../sesiones/sesionesService';
import { registrarCaptura } from './capturaService';
import { BarcodeInput } from './BarcodeInput';
import type { SesionRow, SectorRow, CapturaRow } from '../../db/schema';

const SECTORES_ERROR_MSG =
  'No hay sectores cacheados para esta sucursal — conéctate a la red una vez e inténtalo de nuevo.';

export function CapturaPage({ token }: { token: string }) {
  const { sesionId } = useParams<{ sesionId: string }>();
  const [sesion, setSesion] = useState<SesionRow | null>(null);
  const [sectores, setSectores] = useState<SectorRow[]>([]);
  const [sectorId, setSectorId] = useState<number | null>(null);
  const [cantidad, setCantidad] = useState(1);
  const [capturas, setCapturas] = useState<CapturaRow[]>([]);
  const [sectoresError, setSectoresError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [ultimaCapturaId, setUltimaCapturaId] = useState<string | null>(null);
  const [mostrarResumen, setMostrarResumen] = useState(false);

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
          setSectores(lista);
          setSectoresError(null);
        } catch {
          // Sin sectores no se puede capturar: hay que decirlo en pantalla, no
          // dejar un <select> vacio que traga cada escaneo en silencio.
          if (cancelado) return;
          setSectores([]);
          setSectorId(null);
          setSectoresError(SECTORES_ERROR_MSG);
        }
      }
      const caps = await getCapturasBySesion(sesionId);
      if (!cancelado) setCapturas(caps);
    })();
    return () => { cancelado = true; };
  }, [sesionId, token]);

  async function handleCommit(barcode: string) {
    if (!sesion) return;
    if (sesion.status === 'CLOSED') {
      setAviso('La sesión está cerrada: no se pueden registrar más capturas.');
      return;
    }
    if (sectoresError) {
      setAviso(SECTORES_ERROR_MSG);
      return;
    }
    if (sectorId === null) {
      setAviso('Elige un sector antes de escanear.');
      return;
    }
    setAviso(null);
    const nueva = await registrarCaptura({
      sessionId: sesion.id,
      sectorId,
      barcode,
      quantity: cantidad,
      validateAgainstMaster: sesion.validateAgainstMaster,
    });
    setUltimaCapturaId(nueva.id);
    setCapturas(await getCapturasBySesion(sesion.id));
  }

  async function handleCerrarSesion() {
    if (!sesion || sesion.status === 'CLOSED') return;
    await cerrarSesion(sesion.id);
    const updatedSesion = await getSesion(sesion.id);
    setSesion(updatedSesion ?? null);
    setAviso(null);
  }

  // Editar/eliminar solo tiene sentido mientras la fila no viajo al servidor
  // todavia (syncStatus !== 'synced'): la API no tiene forma de avisarle que
  // se borro o cambio una captura ya recibida.
  async function handleEditarCantidad(capturaId: string, nuevaCantidad: number) {
    if (!sesion || !Number.isFinite(nuevaCantidad) || nuevaCantidad < 1) return;
    await updateCaptura(capturaId, { quantity: nuevaCantidad });
    setCapturas(await getCapturasBySesion(sesion.id));
  }

  async function handleEliminarCaptura(capturaId: string) {
    if (!sesion) return;
    if (!window.confirm('¿Eliminar este registro de captura?')) return;
    await deleteCaptura(capturaId);
    setCapturas(await getCapturasBySesion(sesion.id));
  }

  useEffect(() => {
    if (!mostrarResumen) return;
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') setMostrarResumen(false);
    }
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [mostrarResumen]);

  if (!sesion) return <p>Cargando sesión...</p>;

  const cerrada = sesion.status === 'CLOSED';
  const puedeCapturar = !cerrada && !sectoresError;

  // Orden de escaneo real: `capturedAt` es la fuente de verdad (el id es un
  // GUID aleatorio, no sirve para ordenar). Mas reciente primero, para que
  // el ultimo escaneo quede a la vista sin tener que scrollear la lista.
  const capturasSector = sectorId === null
    ? []
    : capturas
        .filter((c) => c.sectorId === sectorId)
        .sort((a, b) => b.capturedAt.localeCompare(a.capturedAt));
  const totalSector = capturasSector.reduce((acc, c) => acc + c.quantity, 0);
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
      <Link to="/sesiones" className="back-link">← Sesiones</Link>
      <div className="page-header capture-header">
        <h1 className="capture-header__title">Captura — {sesion.inventoryNumber}</h1>
        {cerrada
          ? <span className="tag tag--closed" role="status">Sesión cerrada.</span>
          : <button className="btn btn--danger" onClick={handleCerrarSesion}>Cerrar sesión</button>}
      </div>
      {sectoresError && <p role="alert" className="alert">{sectoresError}</p>}
      {aviso && <p role="alert" className="alert">{aviso}</p>}
      {puedeCapturar && (
        <>
          <div className="capture-form">
            <label className="field">
              Sector
              <select
                className="field__control"
                value={sectorId ?? ''}
                onChange={(e) => setSectorId(Number(e.target.value))}
              >
                <option value="" disabled>Elegir...</option>
                {sectores.map((s) => (
                  <option key={s.id} value={s.id}>{s.name}</option>
                ))}
              </select>
            </label>
            <label className="field">
              Cantidad
              <input
                className="field__control"
                type="number"
                min={1}
                value={cantidad}
                onChange={(e) => setCantidad(Number(e.target.value))}
              />
            </label>
          </div>
          <div className="scan-field">
            <BarcodeInput onCommit={handleCommit} />
          </div>
        </>
      )}
      {puedeCapturar && sectorId === null && (
        <p className="empty-state">Elegí un sector para ver y registrar capturas.</p>
      )}

      {sectorId !== null && (
        <>
          <div className="captura-toolbar">
            <span className="captura-toolbar__total">
              Total en este sector: <strong>{totalSector}</strong>
            </span>
            <button
              type="button"
              className="btn btn--ghost"
              onClick={() => setMostrarResumen(true)}
              disabled={resumenPorCodigo.length === 0}
            >
              Ver resumen
            </button>
          </div>

          {capturasSector.length === 0 ? (
            <p className="empty-state">Sin capturas todavía en este sector. Escaneá el primer código para empezar el renglón.</p>
          ) : (
            <div className="capture-list">
              <div className="capture-list__row capture-list__row--header" aria-hidden="true">
                <span>Código</span>
                <span>Maestro</span>
                <span>Cant.</span>
                <span className="capture-list__estado-header">Estado</span>
                <span />
              </div>
              <ul className="capture-list__body">
                {capturasSector.map((c) => {
                const editable = c.syncStatus !== 'synced';
                return (
                  <li
                    key={c.id}
                    className={c.id === ultimaCapturaId ? 'capture-list__row capture-list__row--new' : 'capture-list__row'}
                  >
                    <span className="capture-list__barcode">{c.barcode}</span>
                    <span className={c.inMaster === false ? 'capture-list__master capture-list__master--missing' : 'capture-list__master'}>
                      {c.productCode ?? (c.inMaster === false ? 'No maestro' : '—')}
                    </span>
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
                    {editable ? (
                      <button
                        type="button"
                        className="capture-list__delete"
                        onClick={() => handleEliminarCaptura(c.id)}
                        aria-label={`Eliminar captura de ${c.barcode}`}
                      >
                        ×
                      </button>
                    ) : (
                      <span />
                    )}
                  </li>
                );
                })}
              </ul>
            </div>
          )}

          {mostrarResumen && (
            <div className="modal-overlay" onClick={() => setMostrarResumen(false)}>
              <div className="modal" onClick={(e) => e.stopPropagation()}>
                <div className="modal__header">
                  <h2 className="summary-panel__title">Resumen por código</h2>
                  <button
                    type="button"
                    className="modal__close"
                    onClick={() => setMostrarResumen(false)}
                    aria-label="Cerrar resumen"
                  >
                    ×
                  </button>
                </div>
                <table className="summary-table">
                  <tbody>
                    {resumenPorCodigo.map((r) => (
                      <tr key={r.barcode}>
                        <td className="summary-table__barcode">{r.barcode}</td>
                        <td className="summary-table__qty">{r.total}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  );
}
