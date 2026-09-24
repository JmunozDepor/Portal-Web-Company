import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { CapturaPage } from '../../../src/features/captura/CapturaPage';
import { db } from '../../../src/db/schema';
import { getCapturasBySesion } from '../../../src/db/repositories';
import type { SesionRow } from '../../../src/db/schema';

function sesionRow(overrides: Partial<SesionRow> = {}): SesionRow {
  return {
    id: 'ses-1', branchId: 7, inventoryNumber: 'INV-77', startedAt: '2026-01-01T00:00:00Z',
    status: 'ACTIVE', validateAgainstMaster: false, syncStatus: 'pending', lastError: null,
    ...overrides,
  };
}

function renderCaptura() {
  return render(
    <MemoryRouter initialEntries={['/sesiones/ses-1']}>
      <Routes>
        <Route path="/sesiones/:sesionId" element={<CapturaPage token="tok" />} />
      </Routes>
    </MemoryRouter>,
  );
}

/** Deja que terminen las promesas de Dexie pendientes dentro de act(). */
async function asentar() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
}

function escanear(barcode: string) {
  const input = screen.getByPlaceholderText('Escanear código de barra');
  fireEvent.change(input, { target: { value: barcode } });
  fireEvent.keyDown(input, { key: 'Enter' });
}

beforeEach(async () => {
  await db.sesiones.clear();
  await db.capturas.clear();
  await db.sectores.clear();
  await db.productos.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('CapturaPage', () => {
  it('avisa en pantalla cuando no se pudieron obtener los sectores (I-2)', async () => {
    await db.sesiones.put(sesionRow());
    // Sin sectores cacheados y sin red: getOrFetchSectores revienta.
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    renderCaptura();

    const alerta = await screen.findByRole('alert');
    await asentar();
    expect(alerta.textContent).toContain('No hay sectores cacheados para esta sucursal');
    // Y no queda un input vivo que se trague los escaneos en silencio.
    expect(screen.queryByPlaceholderText('Escanear código de barra')).toBeNull();
    await asentar();
  });

  it('muestra un aviso visible si se escanea sin sector elegido, en vez de no hacer nada (I-2)', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura();
    await screen.findByPlaceholderText('Escanear código de barra');

    escanear('7801234567890');

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('Elige un sector');
    expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
    await asentar();
  });

  it('registra la captura una vez elegido el sector', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura();
    await screen.findByPlaceholderText('Escanear código de barra');
    fireEvent.change(screen.getByLabelText('Sector'), { target: { value: '3' } });

    escanear('7801234567890');

    await screen.findByText(/7801234567890/);
    const capturas = await getCapturasBySesion('ses-1');
    expect(capturas).toHaveLength(1);
    expect(capturas[0].sectorId).toBe(3);
    await asentar();
  });

  it('cada escaneo queda como linea propia y el resumen agrupa por codigo', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura();
    await screen.findByPlaceholderText('Escanear código de barra');
    fireEvent.change(screen.getByLabelText('Sector'), { target: { value: '3' } });

    escanear('7801234567890');
    await screen.findByText(/7801234567890/);
    escanear('7801234567890');
    await asentar();

    // Cada escaneo es su propia fila en Dexie (no se fusionan).
    const capturas = await getCapturasBySesion('ses-1');
    expect(capturas).toHaveLength(2);

    // El total del sector ya refleja el agrupado (2).
    expect(await screen.findByText('2')).toBeTruthy();

    // El resumen (ventana) tambien lo agrupa en una sola fila con total 2.
    fireEvent.click(screen.getByRole('button', { name: 'Ver resumen' }));
    const modal = await screen.findByRole('heading', { name: 'Resumen por código' });
    const dentroDelModal = within(modal.closest('.modal') as HTMLElement);
    expect(dentroDelModal.getByText('7801234567890')).toBeTruthy();
    expect(dentroDelModal.getByText('2')).toBeTruthy();
    await asentar();
  });

  it('permite editar la cantidad de una captura no sincronizada', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.put({
      id: 'cap-1', sessionId: 'ses-1', sectorId: 3, barcode: '111', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });

    renderCaptura();
    fireEvent.change(await screen.findByLabelText('Sector'), { target: { value: '3' } });

    const cantidadInput = await screen.findByLabelText('Cantidad de 111');
    fireEvent.change(cantidadInput, { target: { value: '5' } });

    await waitFor(async () => {
      const [captura] = await getCapturasBySesion('ses-1');
      expect(captura.quantity).toBe(5);
    });
  });

  it('permite eliminar una captura no sincronizada', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.put({
      id: 'cap-1', sessionId: 'ses-1', sectorId: 3, barcode: '222', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    renderCaptura();
    fireEvent.change(await screen.findByLabelText('Sector'), { target: { value: '3' } });

    const eliminarBtn = await screen.findByLabelText('Eliminar captura de 222');
    fireEvent.click(eliminarBtn);

    await waitFor(async () => {
      expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
    });
  });

  it('una captura ya sincronizada no se puede editar ni eliminar', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await db.capturas.put({
      id: 'cap-1', sessionId: 'ses-1', sectorId: 3, barcode: '333', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'synced', lastError: null,
    });

    renderCaptura();
    fireEvent.change(await screen.findByLabelText('Sector'), { target: { value: '3' } });

    await screen.findByText('Sinc.');
    expect(screen.queryByLabelText('Cantidad de 333')).toBeNull();
    expect(screen.queryByLabelText('Eliminar captura de 333')).toBeNull();
    await asentar();
  });

  it('una sesion cerrada no ofrece captura ni boton de cierre (I-6)', async () => {
    await db.sesiones.put(sesionRow({ status: 'CLOSED' }));
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura();
    await screen.findByText('Sesión cerrada.');
    await asentar();

    expect(screen.queryByPlaceholderText('Escanear código de barra')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Cerrar sesión' })).toBeNull();
    expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
    await asentar();
  });
});
