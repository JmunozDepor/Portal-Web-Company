import { Link } from 'react-router-dom';
import { BUILD_LABEL } from '../version';

export function AppDrawer({
  open,
  onClose,
  onLogout,
}: {
  open: boolean;
  onClose: () => void;
  onLogout: () => void;
}) {
  return (
    <div
      className={open ? 'drawer-overlay open' : 'drawer-overlay'}
      onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}
      aria-hidden={!open}
    >
      <div className="drawer">
        <div className="drawer-head">
          <strong>Auditoría</strong>
        </div>
        <nav>
          <Link to="/sesiones" onClick={onClose}>Inicio</Link>
          <Link to="/mantenedor" onClick={onClose}>Mantenedor</Link>
        </nav>
        <div className="drawer-foot">
          <button type="button" className="btn btn--ghost btn--block" onClick={onLogout}>Salir</button>
          <p className="drawer-foot__version">{BUILD_LABEL}</p>
        </div>
      </div>
    </div>
  );
}
