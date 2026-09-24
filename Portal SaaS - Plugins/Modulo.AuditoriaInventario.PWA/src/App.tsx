import { useCallback, useEffect, useRef, useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from './features/auth/useAuth';
import { LoginPage } from './features/auth/LoginPage';
import { SesionesListPage } from './features/sesiones/SesionesListPage';
import { CapturaPage } from './features/captura/CapturaPage';
import { SyncStatusBadge } from './components/SyncStatusBadge';
import { startSyncLoop, type SyncLoopHandle } from './sync/syncLoop';
import { syncProductos, syncSucursales } from './sync/maestroSync';
import { getSucursales, getProductosCount, reintentarErrores } from './db/repositories';
import { ApiError } from './api/client';

type MaestroEstado = 'cargando' | 'listo' | 'error';

function AuthenticatedApp({ token, onLogout }: { token: string; onLogout: () => void }) {
  const [maestroEstado, setMaestroEstado] = useState<MaestroEstado>('cargando');
  const [maestroDesactualizado, setMaestroDesactualizado] = useState(false);
  const [progreso, setProgreso] = useState(0);
  const [intento, setIntento] = useState(0);
  const navigate = useNavigate();
  const location = useLocation();
  const syncHandleRef = useRef<SyncLoopHandle | null>(null);
  // La barra de sync/logout ocupa espacio que la pantalla de Captura necesita
  // para el escaneo -- queda solo en la lista de Sesiones, la sync de fondo
  // (mas abajo) sigue corriendo igual este visible o no.
  const mostrarTopbar = location.pathname === '/sesiones';

  // Se guarda en un ref: `navigate` cambia de identidad en cada cambio de ruta,
  // y ni la carga del maestro ni el loop de sincronizacion deben reiniciarse al
  // navegar entre pantallas. El efecto que actualiza el ref se declara primero
  // para que ya este al dia cuando corran los efectos de abajo.
  const unauthorizedRef = useRef<() => void>(() => {});
  useEffect(() => {
    unauthorizedRef.current = () => {
      onLogout();
      navigate('/login');
    };
  }, [onLogout, navigate]);

  // Carga inicial del maestro. Solo descarga lo que falta localmente: el loop de
  // sincronizacion (30s / evento online / boton manual) es el que mantiene los
  // datos frescos despues. Nunca deja la app colgada: si falla y ya hay maestro
  // en Dexie entra igual (marcando que puede estar desactualizado), y si no hay
  // nada muestra un estado de error con reintento.
  useEffect(() => {
    let cancelado = false;
    (async () => {
      setMaestroEstado('cargando');
      setMaestroDesactualizado(false);
      try {
        const sucursales = await getSucursales();
        if (sucursales.length === 0) {
          await syncSucursales(token);
        }
        if (await getProductosCount() === 0) {
          await syncProductos(token, setProgreso);
        }
        if (!cancelado) setMaestroEstado('listo');
      } catch (err) {
        if (cancelado) return;
        if (err instanceof ApiError && err.status === 401) {
          unauthorizedRef.current();
          return;
        }
        try {
          const [sucursalesCache, productosCache] = await Promise.all([
            getSucursales(),
            getProductosCount(),
          ]);
          if (cancelado) return;
          if (sucursalesCache.length > 0 && productosCache > 0) {
            setMaestroDesactualizado(true);
            setMaestroEstado('listo');
          } else {
            setMaestroEstado('error');
          }
        } catch {
          if (!cancelado) setMaestroEstado('error');
        }
      }
    })();
    return () => { cancelado = true; };
  }, [token, intento]);

  useEffect(() => {
    const handle = startSyncLoop({
      getToken: () => token,
      onUnauthorized: () => unauthorizedRef.current(),
    });
    syncHandleRef.current = handle;
    return () => {
      handle.stop();
      syncHandleRef.current = null;
    };
  }, [token]);

  // "Sincronizar ahora": ademas de forzar un tick, devuelve a 'pending' las filas
  // que quedaron en 'error', que es el unico camino de reintento disponible.
  const handleSyncNow = useCallback(async () => {
    await reintentarErrores();
    await syncHandleRef.current?.runOnce();
  }, []);

  // Logout manual ("Salir"): mismo camino que el 401 automatico (limpia el
  // token y vuelve a /login); el cleanup del efecto de sync frena el loop al
  // desmontar este componente cuando cambia la ruta.
  const handleLogout = useCallback(() => {
    onLogout();
    navigate('/login');
  }, [onLogout, navigate]);

  if (maestroEstado === 'cargando') {
    return (
      <div className="page-loading">
        <p>Sincronizando maestro… {progreso} productos</p>
      </div>
    );
  }

  if (maestroEstado === 'error') {
    return (
      <div className="page">
        <p role="alert" className="alert">
          No se pudo sincronizar el maestro y no hay datos locales para trabajar sin conexión.
        </p>
        <button className="btn btn--primary" onClick={() => setIntento((n) => n + 1)}>Reintentar</button>
      </div>
    );
  }

  return (
    <div className="app-shell">
      {mostrarTopbar && <SyncStatusBadge onSyncNow={handleSyncNow} onLogout={handleLogout} />}
      <div className="page">
        {maestroDesactualizado && (
          <p role="status" className="status-note">
            Sin conexión con el servidor: se está usando el maestro local, puede estar desactualizado.
          </p>
        )}
        <Routes>
          <Route path="/sesiones" element={<SesionesListPage />} />
          <Route path="/sesiones/:sesionId" element={<CapturaPage token={token} />} />
          <Route path="*" element={<Navigate to="/sesiones" replace />} />
        </Routes>
      </div>
    </div>
  );
}

export function App() {
  // Unica instancia de useAuth en toda la app: LoginPage recibe error/onLogin por
  // props para que el login actualice el guard de rutas de aqui abajo.
  const { token, loading, error, login, logout } = useAuth();

  if (loading) {
    return (
      <div className="page-loading">
        <p>Cargando…</p>
      </div>
    );
  }

  return (
    <BrowserRouter>
      <Routes>
        <Route
          path="/login"
          element={token ? <Navigate to="/sesiones" replace /> : <LoginPage error={error} onLogin={login} />}
        />
        <Route
          path="/*"
          element={token ? <AuthenticatedApp token={token} onLogout={logout} /> : <Navigate to="/login" replace />}
        />
      </Routes>
    </BrowserRouter>
  );
}
