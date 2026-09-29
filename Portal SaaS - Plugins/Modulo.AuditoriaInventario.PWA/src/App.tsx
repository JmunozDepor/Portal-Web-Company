import { useCallback, useEffect, useRef, useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from './features/auth/useAuth';
import { LoginPage } from './features/auth/LoginPage';
import { InicioPage } from './features/inicio/InicioPage';
import { SectorPickerPage } from './features/captura/SectorPickerPage';
import { CapturaPage } from './features/captura/CapturaPage';
import { RevisionPage } from './features/captura/RevisionPage';
import { MantenedorPage } from './features/mantenedor/MantenedorPage';
import { AppDrawer } from './components/AppDrawer';
import { startSyncLoop, type SyncLoopHandle } from './sync/syncLoop';
import { reintentarErrores } from './db/repositories';

function AuthenticatedApp({ token, onLogout }: { token: string; onLogout: () => void }) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  const navigate = useNavigate();
  const syncHandleRef = useRef<SyncLoopHandle | null>(null);

  useEffect(() => {
    const handle = startSyncLoop({
      getToken: () => token,
      onUnauthorized: () => {
        handle.stop();
        onLogout();
        navigate('/login');
      },
    });
    syncHandleRef.current = handle;
    return () => {
      handle.stop();
      syncHandleRef.current = null;
    };
  }, [token, onLogout, navigate]);

  // "Sincronizar ahora" (Mantenedor): ademas de forzar un tick, devuelve a
  // 'pending' las filas que quedaron en 'error', unico camino de reintento.
  const handleSyncNow = useCallback(async () => {
    await reintentarErrores();
    await syncHandleRef.current?.runOnce();
  }, []);

  const handleLogout = useCallback(() => {
    setDrawerOpen(false);
    onLogout();
    navigate('/login');
  }, [onLogout, navigate]);

  const openMenu = useCallback(() => setDrawerOpen(true), []);

  return (
    <div className="app-shell">
      <AppDrawer open={drawerOpen} onClose={() => setDrawerOpen(false)} onLogout={handleLogout} />
      <Routes>
        <Route path="/sesiones" element={<InicioPage onMenuClick={openMenu} />} />
        <Route path="/sesiones/:sesionId" element={<SectorPickerPage token={token} onMenuClick={openMenu} />} />
        <Route path="/sesiones/:sesionId/sectores/:sectorId" element={<CapturaPage token={token} onMenuClick={openMenu} />} />
        <Route path="/sesiones/:sesionId/sectores/:sectorId/revision" element={<RevisionPage token={token} onMenuClick={openMenu} />} />
        <Route path="/mantenedor" element={<MantenedorPage token={token} onMenuClick={openMenu} onSyncNow={handleSyncNow} />} />
        <Route path="*" element={<Navigate to="/sesiones" replace />} />
      </Routes>
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
