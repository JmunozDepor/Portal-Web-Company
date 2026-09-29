import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { SectorPickerPage } from '../../../src/features/captura/SectorPickerPage';
import { db } from '../../../src/db/schema';
import { getSesion } from '../../../src/db/repositories';
import type { SesionRow } from '../../../src/db/schema';

function sesionRow(overrides: Partial<SesionRow> = {}): SesionRow {
  return {
    id: 'ses-1', branchId: 7, inventoryNumber: 'INV-77', startedAt: '2026-01-01T00:00:00Z',
    status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    ...overrides,
  };
}

function renderPicker(sesionId = 'ses-1') {
  return render(
    <MemoryRouter initialEntries={[`/sesiones/${sesionId}`]}>
      <Routes>
        <Route path="/sesiones/:sesionId" element={<SectorPickerPage token="tok" onMenuClick={() => {}} />} />
        <Route path="/sesiones/:sesionId/sectores/:sectorId" element={<p>Pantalla de captura</p>} />
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

describe('SectorPickerPage', () => {
  it('avisa si la sesion no existe', async () => {
    renderPicker('no-existe');
    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('La sesión no existe.');
  });

  it('avisa si no hay sectores cacheados y no hay red', async () => {
    await db.sesiones.put(sesionRow());
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    renderPicker();

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('No hay sectores cacheados para esta sucursal');
  });

  it('lista los sectores con su total contado y navega a Captura al elegir uno', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([
      { id: 1, branchId: 7, name: 'Bodega A' },
      { id: 2, branchId: 7, name: 'Bodega B' },
    ]);
    await db.capturas.put({
      id: 'c1', sessionId: 'ses-1', sectorId: 1, barcode: '111', productCode: 'P1',
      quantity: 4, inMaster: true, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });

    renderPicker();
    await screen.findByText('Bodega A');

    expect(screen.getByText('4 unidades contadas')).toBeTruthy();
    expect(screen.getByText('Sin capturas todavía')).toBeTruthy();

    fireEvent.click(screen.getByText('Bodega A').closest('button')!);
    expect(await screen.findByText('Pantalla de captura')).toBeTruthy();
  });

  it('permite cerrar la sesion con confirmacion y refleja el estado', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 1, branchId: 7, name: 'Bodega A' }]);
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    renderPicker();
    await screen.findByText('Bodega A');

    fireEvent.click(screen.getByRole('button', { name: 'Cerrar sesión' }));
    await asentar();

    expect(await screen.findByRole('status')).toHaveProperty('textContent', 'Sesión cerrada.');
    expect((await getSesion('ses-1'))?.status).toBe('CLOSED');
  });

  it('una sesion cerrada no ofrece el boton de cerrar', async () => {
    await db.sesiones.put(sesionRow({ status: 'CLOSED' }));
    await db.sectores.bulkPut([{ id: 1, branchId: 7, name: 'Bodega A' }]);

    renderPicker();
    await screen.findByText('Bodega A');

    expect(screen.queryByRole('button', { name: 'Cerrar sesión' })).toBeNull();
    expect(screen.getByRole('status').textContent).toBe('Sesión cerrada.');
  });
});
