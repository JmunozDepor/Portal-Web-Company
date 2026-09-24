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
    <div className="export-bar">
      <input
        className="field__control"
        placeholder="Nombre del equipo (opcional)"
        value={deviceLabel}
        onChange={(e) => setDeviceLabel(e.target.value)}
      />
      <button className="btn btn--ghost" onClick={handleExport}>Exportar pendientes</button>
    </div>
  );
}
