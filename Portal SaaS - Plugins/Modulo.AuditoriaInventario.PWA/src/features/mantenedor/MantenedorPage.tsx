import { useEffect, useRef, useState } from 'react';
import { TopBar } from '../../components/TopBar';
import { syncProductos, syncSucursales, syncAjustesCaptura } from '../../sync/maestroSync';
import {
  getProductosCount, getAjustes, setValidarProducto, importarMaestroDesdeJson,
  getPendingCounts, getSesionesLimpiables, limpiarSesionesSincronizadas, getAuthConfig,
  getProductoByBarcode,
} from '../../db/repositories';
import { buildExportPayload, downloadExportPayload } from '../../sync/exportImport';
import { getProductosCountRemoto } from '../../api/endpoints';
import { ApiError } from '../../api/client';

export function MantenedorPage({
  token,
  onMenuClick,
  onSyncNow,
}: {
  token: string;
  onMenuClick: () => void;
  onSyncNow: () => Promise<void>;
}) {
  const [productosCount, setProductosCount] = useState(0);
  const [productosTotalServidor, setProductosTotalServidor] = useState<number | null>(null);
  const [validando, setValidando] = useState(true);
  const [sincronizando, setSincronizando] = useState(false);
  const [progreso, setProgreso] = useState(0);
  const [mensajeMaestro, setMensajeMaestro] = useState<string | null>(null);
  const [errorMaestro, setErrorMaestro] = useState<string | null>(null);
  const [pendientes, setPendientes] = useState({ sesiones: 0, capturas: 0, errores: 0 });
  const [sincronizandoAhora, setSincronizandoAhora] = useState(false);
  const [deviceLabel, setDeviceLabel] = useState('');
  const [limpiablesCount, setLimpiablesCount] = useState(0);
  const [mensajeLimpieza, setMensajeLimpieza] = useState<string | null>(null);
  const [buscarValor, setBuscarValor] = useState('');
  const [resultadoBusqueda, setResultadoBusqueda] = useState<
    | { estado: 'encontrado'; valor: string; productCode: string }
    | { estado: 'no-encontrado'; valor: string }
    | null
  >(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  async function recargar() {
    setProductosCount(await getProductosCount());
    setValidando((await getAjustes()).validarProducto);
    setPendientes(await getPendingCounts());
    setLimpiablesCount((await getSesionesLimpiables()).length);
  }

  /**
   * Compara el conteo local contra el total real del servidor -- una
   * sincronización interrumpida a medias (ej. wifi de sala de ventas) queda
   * local silenciosamente incompleta, sin ningún error visible hasta que
   * falla un escaneo real (2026-09-29). Sin conexión no rompe nada: se deja
   * el último total conocido.
   */
  async function verificarTotalServidor() {
    try {
      setProductosTotalServidor(await getProductosCountRemoto(token));
    } catch {
      // Sin conexión -- se conserva el ultimo total conocido, si lo hay.
    }
  }

  useEffect(() => {
    recargar();
    verificarTotalServidor();
  }, []);

  async function handleSincronizarServidor() {
    setSincronizando(true);
    setErrorMaestro(null);
    setMensajeMaestro(null);
    setProgreso(0);
    try {
      const sucursales = await syncSucursales(token);
      const productos = await syncProductos(token, setProgreso);
      await syncAjustesCaptura(token);
      setMensajeMaestro(`Maestro sincronizado: ${productos} producto(s), ${sucursales} sucursal(es).`);
    } catch (err) {
      setErrorMaestro(
        err instanceof ApiError ? 'No se pudo sincronizar: el servidor respondió con un error.' : 'No se pudo conectar con el servidor.',
      );
    } finally {
      setSincronizando(false);
      await recargar();
      await verificarTotalServidor();
    }
  }

  async function handleCargarArchivo(e: React.ChangeEvent<HTMLInputElement>) {
    const archivo = e.target.files?.[0];
    if (fileInputRef.current) fileInputRef.current.value = '';
    if (!archivo) return;
    setErrorMaestro(null);
    setMensajeMaestro(null);
    try {
      const texto = await archivo.text();
      const payload = JSON.parse(texto);
      const resultado = await importarMaestroDesdeJson(payload);
      setMensajeMaestro(`Maestro cargado desde archivo: ${resultado.productos} producto(s), ${resultado.sucursales} sucursal(es).`);
    } catch {
      setErrorMaestro('El archivo no tiene el formato esperado (JSON con "productos"/"sucursales").');
    } finally {
      await recargar();
    }
  }

  async function handleToggleValidar(checked: boolean) {
    setValidando(checked);
    await setValidarProducto(checked);
  }

  async function handleSyncNowClick() {
    setSincronizandoAhora(true);
    try {
      await onSyncNow();
    } finally {
      setSincronizandoAhora(false);
      await recargar();
    }
  }

  async function handleExportar() {
    const cfg = await getAuthConfig();
    const payload = await buildExportPayload(cfg?.companyCode ?? '', deviceLabel);
    downloadExportPayload(payload);
  }

  /**
   * Busca un código escrito a mano (sin pasar por el lector) contra el
   * maestro cacheado en el equipo -- pensado para diagnosticar en el momento
   * si un "No maestro" real es porque el dato local está mal, o porque el
   * lector mandó algo distinto a lo que se ve en pantalla (un carácter
   * invisible, por ejemplo). El valor tipeado se muestra tal cual con
   * JSON.stringify para que un espacio/salto de línea escondido se note.
   */
  async function handleBuscar(e: React.FormEvent) {
    e.preventDefault();
    const valor = buscarValor.trim();
    if (!valor) return;
    const producto = await getProductoByBarcode(valor);
    setResultadoBusqueda(
      producto
        ? { estado: 'encontrado', valor, productCode: producto.productCode }
        : { estado: 'no-encontrado', valor },
    );
  }

  async function handleLimpiar() {
    if (!window.confirm(
      `¿Eliminar del equipo ${limpiablesCount} sesión${limpiablesCount === 1 ? '' : 'es'} cerrada${limpiablesCount === 1 ? '' : 's'} y ya sincronizada${limpiablesCount === 1 ? '' : 's'}? Los datos ya están guardados en el servidor.`,
    )) return;
    const { sesiones: n, capturas } = await limpiarSesionesSincronizadas();
    setMensajeLimpieza(`Se eliminaron ${n} sesión${n === 1 ? '' : 'es'} (${capturas} captura${capturas === 1 ? '' : 's'}) del equipo.`);
    await recargar();
  }

  return (
    <div>
      <TopBar title="Mantenedor" onMenuClick={onMenuClick} />
      <div className="page">
        <div className="panel">
          <p className="panel__title">Maestro de productos</p>
          <p className="panel__hint">
            {productosCount.toLocaleString('es-CL')} producto{productosCount === 1 ? '' : 's'} cargado{productosCount === 1 ? '' : 's'} en el equipo
            {productosTotalServidor !== null && ` de ${productosTotalServidor.toLocaleString('es-CL')} en el servidor`}.
          </p>
          {productosTotalServidor !== null && productosCount < productosTotalServidor && !sincronizando && (
            <p role="alert" className="alert">
              Maestro incompleto: faltan {(productosTotalServidor - productosCount).toLocaleString('es-CL')} producto(s) por sincronizar.
            </p>
          )}
          {sincronizando && <p role="status">Sincronizando… {progreso} productos</p>}
          {mensajeMaestro && <p role="status" className="status-note">{mensajeMaestro}</p>}
          {errorMaestro && <p role="alert" className="alert">{errorMaestro}</p>}
          <button type="button" className="btn btn--sm btn--block" onClick={handleSincronizarServidor} disabled={sincronizando}>
            Sincronizar desde servidor
          </button>
          <label className="btn btn--sm btn--block cleanup-trigger" style={{ cursor: 'pointer' }}>
            Cargar desde archivo (JSON)
            <input
              ref={fileInputRef}
              type="file"
              accept="application/json"
              onChange={handleCargarArchivo}
              style={{ display: 'none' }}
              aria-label="Cargar maestro desde archivo JSON"
            />
          </label>
          <p className="panel__hint" style={{ marginTop: 'var(--space-2)', marginBottom: 0 }}>
            Usá el archivo si el equipo no tiene señal — se genera desde el Admin y se copia al equipo.
          </p>
        </div>

        <div className="panel">
          <p className="panel__title">Buscar código en el maestro</p>
          <p className="panel__hint">Para diagnosticar sin usar el lector -- escribí el código a mano.</p>
          <form onSubmit={handleBuscar} className="export-bar" style={{ marginBottom: 'var(--space-2)' }}>
            <input
              className="field__control"
              placeholder="Código de barra"
              value={buscarValor}
              onChange={(e) => setBuscarValor(e.target.value)}
              inputMode="numeric"
            />
            <button type="submit" className="btn btn--sm">Buscar</button>
          </form>
          {resultadoBusqueda?.estado === 'encontrado' && (
            <p role="status" className="status-note">
              Encontrado: {JSON.stringify(resultadoBusqueda.valor)} ({resultadoBusqueda.valor.length} dígitos) → productCode {resultadoBusqueda.productCode}
            </p>
          )}
          {resultadoBusqueda?.estado === 'no-encontrado' && (
            <p role="alert" className="alert">
              No está en el maestro local: {JSON.stringify(resultadoBusqueda.valor)} ({resultadoBusqueda.valor.length} dígitos)
            </p>
          )}
        </div>

        <div className="panel">
          <div className="switch-row">
            <div>
              <p className="panel__title" style={{ margin: 0 }}>Validar producto</p>
              <p className="panel__hint" style={{ margin: '.15rem 0 0' }}>
                Avisa (sonido + color) cuando un escaneo no está en el maestro.
              </p>
            </div>
            <label className="switch">
              <input
                type="checkbox"
                checked={validando}
                onChange={(e) => handleToggleValidar(e.target.checked)}
                aria-label="Validar producto contra el maestro"
              />
              <span className="switch__track"><span className="switch__thumb" /></span>
            </label>
          </div>
        </div>

        <div className="panel">
          <p className="panel__title">Sincronización de conteos</p>
          <p className="panel__hint">
            Pendientes: {pendientes.sesiones + pendientes.capturas}
            {pendientes.errores > 0 && ` — Errores: ${pendientes.errores}`}
          </p>
          <button type="button" className="btn btn--sm btn--block" onClick={handleSyncNowClick} disabled={sincronizandoAhora}>
            Sincronizar ahora
          </button>
        </div>

        <div className="panel">
          <p className="panel__title">Exportar pendientes a archivo</p>
          <div className="export-bar" style={{ marginBottom: 'var(--space-2)' }}>
            <input
              className="field__control"
              placeholder="Nombre del equipo (opcional)"
              value={deviceLabel}
              onChange={(e) => setDeviceLabel(e.target.value)}
            />
          </div>
          <button type="button" className="btn btn--sm btn--block" onClick={handleExportar}>
            Exportar a archivo
          </button>
        </div>

        <div className="panel">
          <p className="panel__title">Limpiar sincronizados</p>
          {mensajeLimpieza && <p role="status" className="status-note">{mensajeLimpieza}</p>}
          <p className="panel__hint">
            {limpiablesCount} sesión{limpiablesCount === 1 ? '' : 'es'} cerrada{limpiablesCount === 1 ? '' : 's'} y sincronizada{limpiablesCount === 1 ? '' : 's'} lista{limpiablesCount === 1 ? '' : 's'} para borrar del equipo.
          </p>
          <button type="button" className="btn btn--sm btn--ghost btn--block" onClick={handleLimpiar} disabled={limpiablesCount === 0}>
            Limpiar {limpiablesCount > 0 && `(${limpiablesCount})`}
          </button>
        </div>
      </div>
    </div>
  );
}
