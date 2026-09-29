import { useOnlineStatus } from './useOnlineStatus';
import { BUILD_LABEL } from '../version';

export function TopBar({ title, onMenuClick }: { title: string; onMenuClick: () => void }) {
  const online = useOnlineStatus();

  return (
    <div className="topbar topbar--slim">
      <button className="hamburger" onClick={onMenuClick} aria-label="Abrir menú">☰</button>
      <span className="topbar-title">{title}</span>
      <span className="topbar-mini" title={BUILD_LABEL}>
        <span className={online ? 'status-dot' : 'status-dot status-dot--offline'} aria-hidden="true" />
        {online ? 'En línea' : 'Sin conexión'}
      </span>
    </div>
  );
}
