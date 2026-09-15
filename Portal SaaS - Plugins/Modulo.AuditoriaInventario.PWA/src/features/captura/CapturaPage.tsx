import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { getSesion, getCapturasBySesion } from '../../db/repositories';
import { getOrFetchSectores } from '../../sync/sectoresSync';
import { cerrarSesion } from '../sesiones/sesionesService';
import { registrarCaptura } from './capturaService';
import { BarcodeInput } from './BarcodeInput';
import type { SesionRow, SectorRow, CapturaRow } from '../../db/schema';

export function CapturaPage({ token }: { token: string }) {
  const { sesionId } = useParams<{ sesionId: string }>();
  const [sesion, setSesion] = useState<SesionRow | null>(null);
  const [sectores, setSectores] = useState<SectorRow[]>([]);
  const [sectorId, setSectorId] = useState<number | null>(null);
  const [cantidad, setCantidad] = useState(1);
  const [capturas, setCapturas] = useState<CapturaRow[]>([]);

  useEffect(() => {
    if (!sesionId) return;
    getSesion(sesionId).then(async (s) => {
      setSesion(s ?? null);
      if (s) {
        setSectores(await getOrFetchSectores(token, s.branchId));
      }
    });
    getCapturasBySesion(sesionId).then(setCapturas);
  }, [sesionId, token]);

  async function handleCommit(barcode: string) {
    if (!sesion || sectorId === null) return;
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
    if (!sesion) return;
    await cerrarSesion(sesion.id);
    const updatedSesion = await getSesion(sesion.id);
    setSesion(updatedSesion ?? null);
  }

  if (!sesion) return <p>Cargando sesión...</p>;

  return (
    <div>
      <h1>Captura — {sesion.inventoryNumber}</h1>
      <button onClick={handleCerrarSesion}>Cerrar sesión</button>
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
      <ul>
        {capturas.map((c) => (
          <li key={c.id}>{c.barcode} x{c.quantity} — {c.inMaster === false ? 'no en maestro' : ''}</li>
        ))}
      </ul>
    </div>
  );
}
