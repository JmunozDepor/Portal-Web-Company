import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { getSesion, getCapturasBySesion } from '../../db/repositories';
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
    await registrarCaptura({
      sessionId: sesion.id,
      sectorId,
      barcode,
      quantity: cantidad,
      validateAgainstMaster: sesion.validateAgainstMaster,
    });
    setCapturas(await getCapturasBySesion(sesion.id));
  }

  async function handleCerrarSesion() {
    if (!sesion || sesion.status === 'CLOSED') return;
    await cerrarSesion(sesion.id);
    const updatedSesion = await getSesion(sesion.id);
    setSesion(updatedSesion ?? null);
    setAviso(null);
  }

  if (!sesion) return <p>Cargando sesión...</p>;

  const cerrada = sesion.status === 'CLOSED';
  const puedeCapturar = !cerrada && !sectoresError;

  return (
    <div>
      <h1>Captura — {sesion.inventoryNumber}</h1>
      {cerrada
        ? <p role="status">Sesión cerrada.</p>
        : <button onClick={handleCerrarSesion}>Cerrar sesión</button>}
      {sectoresError && <p role="alert">{sectoresError}</p>}
      {aviso && <p role="alert">{aviso}</p>}
      {puedeCapturar && (
        <>
          <label>
            Sector
            <select value={sectorId ?? ''} onChange={(e) => setSectorId(Number(e.target.value))}>
              <option value="" disabled>Elegir...</option>
              {sectores.map((s) => (
                <option key={s.id} value={s.id}>{s.name}</option>
              ))}
            </select>
          </label>
          <label>
            Cantidad
            <input type="number" min={1} value={cantidad} onChange={(e) => setCantidad(Number(e.target.value))} />
          </label>
          <BarcodeInput onCommit={handleCommit} />
        </>
      )}
      <ul>
        {capturas.map((c) => (
          <li key={c.id}>{c.barcode} x{c.quantity} — {c.inMaster === false ? 'no en maestro' : ''}</li>
        ))}
      </ul>
    </div>
  );
}
