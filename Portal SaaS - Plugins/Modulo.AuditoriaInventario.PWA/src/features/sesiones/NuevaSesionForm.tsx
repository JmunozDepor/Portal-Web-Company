import { useEffect, useState } from 'react';
import { getSucursales } from '../../db/repositories';
import { crearSesion } from './sesionesService';
import type { SucursalRow } from '../../db/schema';
import type { SesionRow } from '../../db/schema';

export function NuevaSesionForm({ onCreated }: { onCreated: (sesion: SesionRow) => void }) {
  const [sucursales, setSucursales] = useState<SucursalRow[]>([]);
  const [branchId, setBranchId] = useState<number | null>(null);
  const [inventoryNumber, setInventoryNumber] = useState('');
  const [validateAgainstMaster, setValidateAgainstMaster] = useState(true);

  useEffect(() => {
    getSucursales().then(setSucursales);
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (branchId === null) return;
    const sesion = await crearSesion({ branchId, inventoryNumber, validateAgainstMaster });
    onCreated(sesion);
  }

  return (
    <form onSubmit={handleSubmit}>
      <label>
        Sucursal
        <select value={branchId ?? ''} onChange={(e) => setBranchId(Number(e.target.value))} required>
          <option value="" disabled>Elegir...</option>
          {sucursales.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </label>
      <label>
        Nro. de Inventario
        <input value={inventoryNumber} onChange={(e) => setInventoryNumber(e.target.value)} required />
      </label>
      <label>
        <input type="checkbox" checked={validateAgainstMaster} onChange={(e) => setValidateAgainstMaster(e.target.checked)} />
        Validar contra maestro
      </label>
      <button type="submit">Crear sesión</button>
    </form>
  );
}
