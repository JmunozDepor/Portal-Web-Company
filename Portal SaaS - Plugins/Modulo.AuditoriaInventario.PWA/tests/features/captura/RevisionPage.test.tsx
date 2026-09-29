import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act, within } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { RevisionPage } from '../../../src/features/captura/RevisionPage';
import { db } from '../../../src/db/schema';
import { getCapturasBySesion } from '../../../src/db/repositories';
import type { SesionRow } from '../../../src/db/schema';

function sesionRow(overrides: Partial<SesionRow> = {}): SesionRow {
  return {
    id: 'ses-1', branchId: 7, inventoryNumber: 'INV-77', startedAt: '2026-01-01T00:00:00Z',
    status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    ...overrides,
  };
}

function renderRevision(sectorId = '3') {
  return render(
    <MemoryRouter initialEntries={[`/sesiones/ses-1/sectores/${sectorId}/revision`]}>
      <Routes>
        <Route path="/sesiones/:sesionId/sectores/:sectorId/revision" element={<RevisionPage token="tok" onMenuClick={() => {}} />} />
      </Routes>
    </MemoryRouter>,
  );
}

async function asentar() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
}

beforeEach(async () => {
  await db.sesiones.clear();
  await db.capturas.clear();
  await db.sectores.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('RevisionPage', () => {
  it('agrupa por codigo en el resumen y lista cada captura por separado en el detalle', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.bulkPut([
      { id: 'c1', sessionId: 'ses-1', sectorId: 3, barcode: '111', productCode: 'P1', quantity: 2, inMaster: true, capturedAt: '2026-01-01T00:00:01Z', syncStatus: 'pending', lastError: null },
      { id: 'c2', sessionId: 'ses-1', sectorId: 3, barcode: '111', productCode: 'P1', quantity: 3, inMaster: true, capturedAt: '2026-01-01T00:00:02Z', syncStatus: 'pending', lastError: null },
      // Otro sector: no debe aparecer.
      { id: 'c3', sessionId: 'ses-1', sectorId: 9, barcode: '222', productCode: 'P2', quantity: 9, inMaster: true, capturedAt: '2026-01-01T00:00:03Z', syncStatus: 'pending', lastError: null },
    ]);

    renderRevision('3');
    const resumen = await screen.findByText('Resumen por código (sector actual)');
    const tabla = resumen.parentElement!.querySelector('.summary-table') as HTMLElement;
    expect(within(tabla).getByText('111')).toBeTruthy();
    expect(within(tabla).getByText('5')).toBeTruthy();
    expect(within(tabla).queryByText('222')).toBeNull();

    const filas = screen.getAllByLabelText(/^Cantidad de 111$/);
    expect(filas).toHaveLength(2);
  });

  it('permite editar la cantidad de una captura no sincronizada', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.put({
      id: 'cap-1', sessionId: 'ses-1', sectorId: 3, barcode: '111', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });

    renderRevision('3');
    const cantidadInput = await screen.findByLabelText('Cantidad de 111');
    fireEvent.change(cantidadInput, { target: { value: '5' } });
    await asentar();

    const [captura] = await getCapturasBySesion('ses-1');
    expect(captura.quantity).toBe(5);
  });

  it('permite eliminar una captura no sincronizada, con confirmacion', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.put({
      id: 'cap-1', sessionId: 'ses-1', sectorId: 3, barcode: '222', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    renderRevision('3');
    const eliminarBtn = await screen.findByLabelText('Eliminar captura de 222');
    fireEvent.click(eliminarBtn);
    await asentar();

    expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
  });

  it('una captura ya sincronizada no se puede editar ni eliminar', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.put({
      id: 'cap-1', sessionId: 'ses-1', sectorId: 3, barcode: '333', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'synced', lastError: null,
    });

    renderRevision('3');
    await screen.findByText('Sinc.');

    expect(screen.queryByLabelText('Cantidad de 333')).toBeNull();
    expect(screen.queryByLabelText('Eliminar captura de 333')).toBeNull();
  });
});
