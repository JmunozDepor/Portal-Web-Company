import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { TopBar } from '../../components/TopBar';
import {
  getSesion, getCapturasBySesion, getAjustes, setValidarProducto, getConfiguracionCaptura,
} from '../../db/repositories';
import { getOrFetchSectores } from '../../sync/sectoresSync';
import { registrarCaptura } from './capturaService';
import { reproducirBeepError } from './beep';
import { esFormatoValido } from './barcodeFormat';
import { BarcodeInput } from './BarcodeInput';
import type { SesionRow, SectorRow, CapturaRow, ConfiguracionCapturaRow } from '../../db/schema';

const CONFIGURACION_CAPTURA_DEFAULT: ConfiguracionCapturaRow = {
  id: 'singleton', allowEan8: true, allowUpcA: true, allowEan13: true,
};

const SECTORES_ERROR_MSG =
  'No hay sectores cacheados para esta sucursal — conéctate a la red una vez e inténtalo de nuevo.';

export function CapturaPage({ token, onMenuClick }: { token: string; onMenuClick: () => void }) {
  const { sesionId, sectorId: sectorIdParam } = useParams<{ sesionId: string; sectorId: string }>();
  const sectorId = sectorIdParam ? Number(sectorIdParam) : NaN;
  const [sesion, setSesion] = useState<SesionRow | null>(null);
  const [sector, setSector] = useState<SectorRow | null>(null);
  const [cantidad, setCantidad] = useState(1);
  const [capturas, setCapturas] = useState<CapturaRow[]>([]);
  const [sectoresError, setSectoresError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);
  const [ultimaCapturaId, setUltimaCapturaId] = useState<string | null>(null);
  const [validando, setValidando] = useState(true);
  const [configuracionCaptura, setConfiguracionCaptura] = useState<ConfiguracionCapturaRow>(CONFIGURACION_CAPTURA_DEFAULT);

  useEffect(() => {
    if (!sesionId) return;
    let cancelado = false;
    (async () => {
      const s = await getSesion(sesionId);
      if (cancelado) return;
      setSesion(s ?? null);
      setValidando((await getAjustes()).validarProducto);
      setConfiguracionCaptura(await getConfiguracionCaptura());
      if (s) {
        try {
          const lista = await getOrFetchSectores(token, s.branchId);
          if (cancelado) return;
          setSector(lista.find((sec) => sec.id === sectorId) ?? null);
          setSectoresError(null);
        } catch {
          if (cancelado) return;
          setSector(null);
          setSectoresError(SECTORES_ERROR_MSG);
        }
      }
      const caps = await getCapturasBySesion(sesionId);
      if (!cancelado) setCapturas(caps);
    })();
    return () => { cancelado = true; };
  }, [sesionId, sectorId, token]);

  async function handleToggleValidar(checked: boolean) {
    setValidando(checked);
    await setValidarProducto(checked);
  }

  async function handleCommit(barcode: string) {
    if (!sesion) return;
    if (sesion.status === 'CLOSED') {
      setAviso('La sesión está cerrada: no se pueden registrar más capturas.');
      return;
    }
    if (sectoresError || !sector) {
      setAviso(SECTORES_ERROR_MSG);
      return;
    }
    if (!esFormatoValido(barcode, configuracionCaptura)) {
      // Se muestra el valor y el largo tal cual llegó del lector -- un UPC-A
      // impreso de 12 dígitos puede llegar como 11 o 13 según cómo esté
      // configurado el lector físico (recorta o expande a EAN-13), y sin ver
      // el valor real no hay forma de diagnosticar el desajuste a distancia.
      setAviso(`Código no válido: "${barcode}" (${barcode.length} dígitos) — se esperaba EAN-8 (8), UPC-A (12) o EAN-13 (13).`);
      reproducirBeepError();
      return;
    }
    setAviso(null);
    const nueva = await registrarCaptura({
      sessionId: sesion.id,
      sectorId: sector.id,
      barcode,
      quantity: cantidad,
      validateAgainstMaster: validando,
    });
    if (validando && nueva.inMaster === false) {
      reproducirBeepError();
    }
    setUltimaCapturaId(nueva.id);
    setCapturas(await getCapturasBySesion(sesion.id));
  }

  if (!sesion) return <p>Cargando sesión...</p>;

  const cerrada = sesion.status === 'CLOSED';
  const puedeCapturar = !cerrada && !sectoresError && sector !== null;

  // Orden de escaneo real: `capturedAt` es la fuente de verdad (el id es un
  // GUID aleatorio, no sirve para ordenar). Mas reciente primero.
  const capturasSector = sector === null
    ? []
    : capturas
        .filter((c) => c.sectorId === sector.id)
        .sort((a, b) => b.capturedAt.localeCompare(a.capturedAt))
        .slice(0, 20); // "Escaneado recién" -- el detalle completo vive en Revisión.
  const totalSector = sector === null
    ? 0
    : capturas.filter((c) => c.sectorId === sector.id).reduce((acc, c) => acc + c.quantity, 0);

  return (
    <div>
      <TopBar title={`Captura — ${sesion.inventoryNumber}`} onMenuClick={onMenuClick} />
      <div className="page">
        <div className="capture-context">
          <span>Sector: <strong>{sector?.name ?? '—'}</strong></span>
          <Link to={`/sesiones/${sesionId}`}>Cambiar sector</Link>
        </div>

        {sectoresError && <p role="alert" className="alert">{sectoresError}</p>}
        {aviso && <p role="alert" className="alert">{aviso}</p>}
        {cerrada && <p role="status" className="status-note">Sesión cerrada — no se pueden registrar más capturas.</p>}

        {puedeCapturar && (
          <>
            <div className="scan-field">
              <BarcodeInput onCommit={handleCommit} />
            </div>
            <div className="capture-form">
              <label htmlFor="captura-cantidad">Cantidad</label>
              <input
                id="captura-cantidad"
                className="field__control"
                type="number"
                min={1}
                value={cantidad}
                onChange={(e) => setCantidad(Number(e.target.value))}
              />
            </div>
            <div className="validate-quick">
              <span>Validar contra maestro</span>
              <label className="switch switch--sm">
                <input
                  type="checkbox"
                  checked={validando}
                  onChange={(e) => handleToggleValidar(e.target.checked)}
                  aria-label="Validar producto contra el maestro"
                />
                <span className="switch__track"><span className="switch__thumb" /></span>
              </label>
            </div>
          </>
        )}

        <div className="captura-toolbar">
          <span className="captura-toolbar__total">
            Total en este sector: <strong>{totalSector}</strong>
          </span>
          {sector !== null && (
            <Link to={`/sesiones/${sesionId}/sectores/${sector.id}/revision`} className="btn btn--ghost">
              Ver revisión
            </Link>
          )}
        </div>

        {capturasSector.length === 0 ? (
          <p className="empty-state">Sin capturas todavía en este sector. Escaneá el primer código para empezar el renglón.</p>
        ) : (
          <>
            <h2 className="summary-panel__title">Escaneado recién</h2>
            <div className="capture-list">
              <ul className="capture-list__body">
                {capturasSector.map((c) => (
                  <li
                    key={c.id}
                    className={c.id === ultimaCapturaId ? 'capture-list__row capture-list__row--new' : 'capture-list__row'}
                  >
                    <div className="capture-list__line1">
                      <span className="capture-list__barcode">{c.barcode}</span>
                      <span className="capture-list__qty">{c.quantity}</span>
                      <span className={`capture-list__estado capture-list__estado--${c.syncStatus}`}>
                        {c.syncStatus === 'synced' ? 'Sinc.' : c.syncStatus === 'error' ? 'Error' : 'Pend.'}
                      </span>
                    </div>
                    <div className={c.inMaster === false ? 'capture-list__line2 capture-list__master--missing' : 'capture-list__line2'}>
                      {c.productCode ?? (c.inMaster === false ? 'No maestro' : '—')}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
