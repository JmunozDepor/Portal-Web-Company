import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getSesiones } from '../../db/repositories';
import { NuevaSesionForm } from './NuevaSesionForm';
import type { SesionRow } from '../../db/schema';

export function SesionesListPage() {
  const [sesiones, setSesiones] = useState<SesionRow[]>([]);
  const [showForm, setShowForm] = useState(false);

  async function reload() {
    setSesiones(await getSesiones());
  }

  useEffect(() => {
    reload();
  }, []);

  return (
    <div>
      <h1>Sesiones de conteo</h1>
      <button onClick={() => setShowForm(true)}>Nueva sesión</button>
      {showForm && (
        <NuevaSesionForm
          onCreated={() => {
            setShowForm(false);
            reload();
          }}
        />
      )}
      <ul>
        {sesiones.map((s) => (
          <li key={s.id}>
            <Link to={`/sesiones/${s.id}`}>{s.inventoryNumber} — {s.status} ({s.syncStatus})</Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
