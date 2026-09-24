import { useEffect, useState } from 'react';
import { getPendingCounts } from '../db/repositories';

export function SyncStatusBadge({ onSyncNow, onLogout }: { onSyncNow: () => void; onLogout: () => void }) {
  const [counts, setCounts] = useState({ sesiones: 0, capturas: 0, errores: 0 });
  const [online, setOnline] = useState(navigator.onLine);

  useEffect(() => {
    const update = () => getPendingCounts().then(setCounts);
    update();
    const interval = setInterval(update, 5000);
    const onOnline = () => setOnline(true);
    const onOffline = () => setOnline(false);
    window.addEventListener('online', onOnline);
    window.addEventListener('offline', onOffline);
    return () => {
      clearInterval(interval);
      window.removeEventListener('online', onOnline);
      window.removeEventListener('offline', onOffline);
    };
  }, []);

  return (
    <div className="topbar">
      <span className="topbar__status">
        <span className={online ? 'topbar__dot' : 'topbar__dot topbar__dot--offline'} aria-hidden="true" />
        {online ? 'En línea' : 'Sin conexión'}
      </span>
      <span className="topbar__status">Pendientes: {counts.sesiones + counts.capturas}</span>
      {counts.errores > 0 && <span className="topbar__errors">Errores: {counts.errores}</span>}
      <span className="topbar__spacer" />
      <button className="topbar__sync-btn" onClick={onSyncNow}>Sincronizar ahora</button>
      <button className="topbar__sync-btn" onClick={onLogout}>Salir</button>
    </div>
  );
}
