import { useEffect, useState } from 'react';
import { getEmpresas, getUsuarios } from '../../api/endpoints';
import { guardarEmpresasCache, cargarEmpresasCache, guardarUsuariosCache, cargarUsuariosCache } from './offlineDirectory';
import { useOnlineStatus } from '../../components/useOnlineStatus';
import { BUILD_LABEL } from '../../version';
import type { EmpresaDto, UsuarioDto } from '../../api/types';

export interface LoginPageProps {
  /** Mensaje de error del ultimo intento, provisto por el unico useAuth() de App. */
  error: string | null;
  onLogin: (companyCode: string, username: string, password: string) => Promise<boolean>;
}

/**
 * Formulario de login. NO instancia useAuth(): el estado de autenticacion vive
 * en App (una sola instancia del hook) y llega por props, para que un login
 * exitoso actualice el guard de rutas.
 *
 * Empresa y Usuario son desplegables (no texto libre) -- pedido explícito del
 * dueño del proyecto, 2026-09-29: en captura de terreno escribirlos a mano
 * "se vuelve engorroso" y ambos son datos conocidos de antemano (Empresa =
 * compañías activas con el módulo instalado, Usuario = capturadores activos
 * de esa empresa). Lo único manual es la Contraseña.
 *
 * Los equipos trabajan offline y no siempre tienen conectividad (mismo pedido,
 * 2026-09-29): si el fetch en vivo falla, se cae a la última copia guardada en
 * localStorage (offlineDirectory) en vez de dejar el desplegable vacío -- el
 * intento de login todavía puede fallar por falta de red, pero recién ahí, no
 * antes de poder siquiera elegir Empresa/Usuario.
 */
export function LoginPage({ error, onLogin }: LoginPageProps) {
  const [empresas, setEmpresas] = useState<EmpresaDto[]>([]);
  const [empresasError, setEmpresasError] = useState<string | null>(null);
  const [empresasOffline, setEmpresasOffline] = useState(false);
  const [companyCode, setCompanyCode] = useState('');

  const [usuarios, setUsuarios] = useState<UsuarioDto[]>([]);
  const [usuariosOffline, setUsuariosOffline] = useState(false);
  const [cargandoUsuarios, setCargandoUsuarios] = useState(false);
  const [username, setUsername] = useState('');

  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const online = useOnlineStatus();

  async function cargarEmpresas() {
    setEmpresasError(null);
    setEmpresasOffline(false);
    try {
      const lista = await getEmpresas();
      setEmpresas(lista);
      guardarEmpresasCache(lista);
      // Un equipo de captura casi siempre trabaja con una sola empresa -- si
      // solo hay una opción, se la preselecciona.
      if (lista.length === 1) setCompanyCode(lista[0].companyCode);
    } catch {
      const cache = cargarEmpresasCache();
      if (cache.length > 0) {
        setEmpresas(cache);
        setEmpresasOffline(true);
        if (cache.length === 1) setCompanyCode(cache[0].companyCode);
      } else {
        setEmpresasError('No se pudo cargar la lista de empresas.');
      }
    }
  }

  useEffect(() => {
    cargarEmpresas();
  }, []);

  useEffect(() => {
    setUsername('');
    setUsuariosOffline(false);
    if (!companyCode) {
      setUsuarios([]);
      return;
    }
    let cancelado = false;
    setCargandoUsuarios(true);
    getUsuarios(companyCode)
      .then((lista) => {
        if (cancelado) return;
        setUsuarios(lista);
        guardarUsuariosCache(companyCode, lista);
      })
      .catch(() => {
        if (cancelado) return;
        const cache = cargarUsuariosCache(companyCode);
        setUsuarios(cache);
        setUsuariosOffline(cache.length > 0);
      })
      .finally(() => { if (!cancelado) setCargandoUsuarios(false); });
    return () => { cancelado = true; };
  }, [companyCode]);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    try {
      await onLogin(companyCode, username, password);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="login">
      <div className="login-card">
        <form onSubmit={handleSubmit}>
          <div className="login-card__header">
            <h1>Auditoría de Inventario</h1>
            <span className="topbar-mini login-card__status">
              <span className={online ? 'status-dot' : 'status-dot status-dot--offline'} aria-hidden="true" />
              {online ? 'En línea' : 'Sin conexión'}
            </span>
          </div>
          <p className="login-card__subtitle">Conteo físico por código de barra</p>
          {error && <p role="alert" className="alert">{error}</p>}
          {empresasError && (
            <p role="alert" className="alert">
              {empresasError}{' '}
              <button type="button" className="btn-ghost" onClick={cargarEmpresas}>Reintentar</button>
            </p>
          )}
          {(empresasOffline || usuariosOffline) && (
            <p role="status" className="status-note">Sin conexión: mostrando la última lista guardada en el equipo.</p>
          )}
          <label className="field">
            Empresa
            <select
              className="field__control"
              value={companyCode}
              onChange={(e) => setCompanyCode(e.target.value)}
              required
            >
              <option value="" disabled>Elegir...</option>
              {empresas.map((e) => (
                <option key={e.companyCode} value={e.companyCode}>{e.name}</option>
              ))}
            </select>
          </label>
          <label className="field">
            Usuario
            <select
              className="field__control"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              disabled={!companyCode || cargandoUsuarios}
              required
            >
              <option value="" disabled>
                {cargandoUsuarios ? 'Cargando…' : 'Elegir...'}
              </option>
              {usuarios.map((u) => (
                <option key={u.username} value={u.username}>{u.fullName ?? u.username}</option>
              ))}
            </select>
          </label>
          <label className="field">
            Contraseña
            <input
              className="field__control"
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>
          <button type="submit" className="btn btn--primary" disabled={submitting || !companyCode || !username}>
            {submitting ? 'Ingresando…' : 'Ingresar'}
          </button>
        </form>
        <p className="login-card__version">{BUILD_LABEL}</p>
      </div>
    </div>
  );
}
