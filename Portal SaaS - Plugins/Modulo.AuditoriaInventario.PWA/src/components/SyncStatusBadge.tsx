import { useEffect, useState } from 'react';
import { getPendingCounts } from '../db/repositories';

export function SyncStatusBadge({ onSyncNow }: { onSyncNow: () => void }) {
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
    <div>
      <span>{online ? 'En línea' : 'Sin conexión'}</span>
      <span> Pendientes: {counts.sesiones + counts.capturas}</span>
      {counts.errores > 0 && <span> — Errores: {counts.errores}</span>}
      <button onClick={onSyncNow}>Sincronizar ahora</button>
    </div>
  );
}
