import { useEffect, useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from './features/auth/useAuth';
import { LoginPage } from './features/auth/LoginPage';
import { SesionesListPage } from './features/sesiones/SesionesListPage';
import { CapturaPage } from './features/captura/CapturaPage';
import { SyncStatusBadge } from './components/SyncStatusBadge';
import { ExportButton } from './features/export/ExportButton';
import { startSyncLoop } from './sync/syncLoop';
import { syncProductos, syncSucursales } from './sync/maestroSync';
import { getSucursales } from './db/repositories';

function AuthenticatedApp({ token, onLogout }: { token: string; onLogout: () => void }) {
  const [maestroReady, setMaestroReady] = useState(false);
  const [progreso, setProgreso] = useState(0);
  const navigate = useNavigate();

  useEffect(() => {
    (async () => {
      const sucursales = await getSucursales();
      if (sucursales.length === 0) {
        await syncSucursales(token);
      }
      await syncProductos(token, setProgreso);
      setMaestroReady(true);
    })();
  }, [token]);

  useEffect(() => {
    const handle = startSyncLoop({
      getToken: () => token,
      onUnauthorized: () => {
        onLogout();
        navigate('/login');
      },
    });
    return () => handle.stop();
  }, [token, onLogout, navigate]);

  if (!maestroReady) {
    return <p>Sincronizando maestro... {progreso} productos</p>;
  }

  return (
    <div>
      <SyncStatusBadge onSyncNow={() => {}} />
      <ExportButton />
      <Routes>
        <Route path="/sesiones" element={<SesionesListPage />} />
        <Route path="/sesiones/:sesionId" element={<CapturaPage token={token} />} />
        <Route path="*" element={<Navigate to="/sesiones" replace />} />
      </Routes>
    </div>
  );
}

export function App() {
  const { token, loading, logout } = useAuth();

  if (loading) {
    return <p>Cargando...</p>;
  }

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={token ? <Navigate to="/sesiones" replace /> : <LoginPage />} />
        <Route
          path="/*"
          element={token ? <AuthenticatedApp token={token} onLogout={logout} /> : <Navigate to="/login" replace />}
        />
      </Routes>
    </BrowserRouter>
  );
}
