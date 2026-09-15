import { useState } from 'react';
import { getAuthConfig } from '../../db/repositories';
import { buildExportPayload, downloadExportPayload } from '../../sync/exportImport';

export function ExportButton() {
  const [deviceLabel, setDeviceLabel] = useState('');

  async function handleExport() {
    const cfg = await getAuthConfig();
    const payload = await buildExportPayload(cfg?.companyCode ?? '', deviceLabel);
    downloadExportPayload(payload);
  }

  return (
    <div>
      <input
        placeholder="Nombre del equipo (opcional)"
        value={deviceLabel}
        onChange={(e) => setDeviceLabel(e.target.value)}
      />
      <button onClick={handleExport}>Exportar pendientes</button>
    </div>
  );
}
