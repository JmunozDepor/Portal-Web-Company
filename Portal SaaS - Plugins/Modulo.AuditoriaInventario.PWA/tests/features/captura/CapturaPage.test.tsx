import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { CapturaPage } from '../../../src/features/captura/CapturaPage';
import { db } from '../../../src/db/schema';
import { getCapturasBySesion, setValidarProducto, setConfiguracionCaptura } from '../../../src/db/repositories';
import type { SesionRow } from '../../../src/db/schema';

function sesionRow(overrides: Partial<SesionRow> = {}): SesionRow {
  return {
    id: 'ses-1', branchId: 7, inventoryNumber: 'INV-77', startedAt: '2026-01-01T00:00:00Z',
    status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    ...overrides,
  };
}

function renderCaptura(sectorId = '3') {
  return render(
    <MemoryRouter initialEntries={[`/sesiones/ses-1/sectores/${sectorId}`]}>
      <Routes>
        <Route path="/sesiones/:sesionId/sectores/:sectorId" element={<CapturaPage token="tok" onMenuClick={() => {}} />} />
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
  await db.ajustes.clear();
  await db.configuracionCaptura.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('CapturaPage', () => {
  it('avisa en pantalla cuando no se pudieron obtener los sectores', async () => {
    await db.sesiones.put(sesionRow());
    // Sin sectores cacheados y sin red: getOrFetchSectores revienta.
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    renderCaptura();

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('No hay sectores cacheados para esta sucursal');
    expect(screen.queryByPlaceholderText('Escanear código de barra')).toBeNull();
    await asentar();
  });

  it('el sector viene fijo desde la ruta (elegido en la pantalla anterior)', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    expect(screen.getByText('Bodega')).toBeTruthy();
  });

  it('registra la captura contra el sector de la ruta', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    escanear('7801234567890');

    await screen.findByText(/7801234567890/);
    const capturas = await getCapturasBySesion('ses-1');
    expect(capturas).toHaveLength(1);
    expect(capturas[0].sectorId).toBe(3);
    await asentar();
  });

  it('rechaza un escaneo que no tiene formato de EAN-8/UPC-A/EAN-13, sin registrarlo', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    escanear('ABC123'); // payload tipo QR/DataMatrix, no es un largo EAN/UPC valido

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('Código no válido');
    expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
    await asentar();
  });

  it('respeta el formato deshabilitado por el administrador aunque el largo sea correcto', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await setConfiguracionCaptura({ allowEan8: false, allowUpcA: true, allowEan13: true });

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    escanear('12345678'); // 8 digitos = EAN-8, pero el administrador lo deshabilito

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('Código no válido');
    expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
    await asentar();
  });

  it('cada escaneo queda como linea propia y el total del sector suma todo (no solo lo visible)', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    escanear('7801234567890');
    await screen.findByText(/7801234567890/);
    escanear('7801234567890');
    await asentar();

    const capturas = await getCapturasBySesion('ses-1');
    expect(capturas).toHaveLength(2);
    expect(await screen.findByText('2')).toBeTruthy();
  });

  it('marca visualmente una captura fuera del maestro cuando la validacion esta activa', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await setValidarProducto(true);

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    escanear('9999999999999');

    const fila = await screen.findByText('9999999999999');
    expect(fila.closest('.capture-list__row')?.querySelector('.capture-list__master--missing')).toBeTruthy();
    await asentar();
  });

  it('no marca nada fuera del maestro cuando la validacion esta desactivada', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);
    await setValidarProducto(false);

    renderCaptura('3');
    const toggle = await screen.findByLabelText<HTMLInputElement>('Validar producto contra el maestro');
    expect(toggle.checked).toBe(false);

    escanear('9999999999999');

    const fila = await screen.findByText('9999999999999');
    expect(fila.closest('.capture-list__row')?.querySelector('.capture-list__master--missing')).toBeNull();
    await asentar();
  });

  it('el toggle de validar producto se puede apagar y persiste como ajuste global', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    const toggle = await screen.findByLabelText<HTMLInputElement>('Validar producto contra el maestro');
    expect(toggle.checked).toBe(true);

    fireEvent.click(toggle);
    await asentar();

    expect(toggle.checked).toBe(false);
    expect((await db.ajustes.get('singleton'))?.validarProducto).toBe(false);
  });

  it('una sesion cerrada no ofrece captura', async () => {
    await db.sesiones.put(sesionRow({ status: 'CLOSED' }));
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    await screen.findByText('Sesión cerrada — no se pueden registrar más capturas.');

    expect(screen.queryByPlaceholderText('Escanear código de barra')).toBeNull();
    expect(await getCapturasBySesion('ses-1')).toHaveLength(0);
    await asentar();
  });

  it('ofrece volver a elegir sector y ver la revision del sector actual', async () => {
    await db.sesiones.put(sesionRow());
    await db.sectores.bulkPut([{ id: 3, branchId: 7, name: 'Bodega' }]);

    renderCaptura('3');
    await screen.findByPlaceholderText('Escanear código de barra');

    expect(screen.getByRole('link', { name: 'Cambiar sector' }).getAttribute('href')).toBe('/sesiones/ses-1');
    expect(screen.getByRole('link', { name: 'Ver revisión' }).getAttribute('href')).toBe('/sesiones/ses-1/sectores/3/revision');
  });
});
