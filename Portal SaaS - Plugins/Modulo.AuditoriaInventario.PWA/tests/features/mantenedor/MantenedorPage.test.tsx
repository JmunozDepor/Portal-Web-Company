import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { MantenedorPage } from '../../../src/features/mantenedor/MantenedorPage';
import { db } from '../../../src/db/schema';
import { getAjustes, createSesion, createCaptura, upsertProductos } from '../../../src/db/repositories';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

async function asentar() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
}

beforeEach(async () => {
  await db.productos.clear();
  await db.sucursales.clear();
  await db.sesiones.clear();
  await db.capturas.clear();
  await db.ajustes.clear();
  await db.configuracionCaptura.clear();
  await db.authConfig.clear();
  URL.createObjectURL = vi.fn(() => 'blob:mock');
  URL.revokeObjectURL = vi.fn();
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('MantenedorPage', () => {
  it('muestra el total de productos cargados en el equipo', async () => {
    await db.productos.bulkPut([{ id: 1, barcode: '111', productCode: 'P1' }]);

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    expect(await screen.findByText('1 producto cargado en el equipo.')).toBeTruthy();
  });

  it('sincroniza el maestro desde el servidor y muestra el resultado', async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/maestro/sucursales')) return jsonResponse([{ id: 1, branchCode: 'S1', name: 'Casa Matriz' }]);
      if (url.includes('/maestro/productos')) {
        return jsonResponse({ items: [{ id: 1, barcode: '111', productCode: 'P1', description: null, brand: null, line: null }], hasMore: false });
      }
      if (url.includes('/maestro/ajustes-captura')) return jsonResponse({ allowEan8: true, allowUpcA: false, allowEan13: true });
      if (url.includes('/maestro/productos/total')) return jsonResponse(1);
      return jsonResponse({});
    });
    vi.stubGlobal('fetch', fetchMock);

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Sincronizar desde servidor' }));

    expect(await screen.findByText('Maestro sincronizado: 1 producto(s), 1 sucursal(es).')).toBeTruthy();
    expect(await db.productos.count()).toBe(1);
    expect(await db.configuracionCaptura.get('singleton')).toEqual({ id: 'singleton', allowEan8: true, allowUpcA: false, allowEan13: true });
  });

  it('avisa si el maestro local quedo incompleto respecto del total real del servidor', async () => {
    await db.productos.bulkPut([{ id: 1, barcode: '111', productCode: 'P1' }]);
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/maestro/productos/total')) return jsonResponse(197875);
      return jsonResponse({});
    }));

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    expect(await screen.findByText('1 producto cargado en el equipo de 197.875 en el servidor.')).toBeTruthy();
    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('Maestro incompleto: faltan 197.874 producto(s) por sincronizar.');
  });

  it('no avisa nada si el maestro local ya esta completo', async () => {
    await db.productos.bulkPut([{ id: 1, barcode: '111', productCode: 'P1' }]);
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/maestro/productos/total')) return jsonResponse(1);
      return jsonResponse({});
    }));

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    expect(await screen.findByText('1 producto cargado en el equipo de 1 en el servidor.')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('sin conexión, no rompe nada y no muestra ningun total de referencia', async () => {
    await db.productos.bulkPut([{ id: 1, barcode: '111', productCode: 'P1' }]);
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    expect(await screen.findByText('1 producto cargado en el equipo.')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('buscar código encuentra un producto cacheado y lo muestra con su largo', async () => {
    await upsertProductos([{ id: 1, barcode: '194435804293', productCode: '372860C-102-2X' }]);

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    fireEvent.change(screen.getByPlaceholderText('Código de barra'), { target: { value: '194435804293' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    expect(await screen.findByText(/Encontrado: "194435804293" \(12 dígitos\)/)).toBeTruthy();
    expect(screen.getByText(/372860C-102-2X/)).toBeTruthy();
  });

  it('buscar código avisa cuando no está en el maestro local, mostrando el valor tal cual se tipeó', async () => {
    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    fireEvent.change(screen.getByPlaceholderText('Código de barra'), { target: { value: '000000000000' } });
    fireEvent.click(screen.getByRole('button', { name: 'Buscar' }));

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('No está en el maestro local: "000000000000" (12 dígitos)');
  });

  it('muestra un error si la sincronizacion con el servidor falla', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Sincronizar desde servidor' }));

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'No se pudo conectar con el servidor.');
  });

  it('carga el maestro desde un archivo JSON', async () => {
    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    const payload = {
      productos: [{ id: 1, barcode: '111', productCode: 'P1' }],
      sucursales: [{ id: 1, branchCode: 'S1', name: 'Casa Matriz' }],
    };
    const archivo = new File([JSON.stringify(payload)], 'maestro.json', { type: 'application/json' });
    const input = screen.getByLabelText('Cargar maestro desde archivo JSON');
    fireEvent.change(input, { target: { files: [archivo] } });

    expect(await screen.findByText('Maestro cargado desde archivo: 1 producto(s), 1 sucursal(es).')).toBeTruthy();
    expect(await db.productos.count()).toBe(1);
    expect(await db.sucursales.count()).toBe(1);
  });

  it('avisa si el archivo cargado no tiene el formato esperado', async () => {
    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    const archivo = new File(['esto no es json'], 'maestro.json', { type: 'application/json' });
    const input = screen.getByLabelText('Cargar maestro desde archivo JSON');
    fireEvent.change(input, { target: { files: [archivo] } });

    expect(await screen.findByRole('alert')).toHaveProperty(
      'textContent',
      'El archivo no tiene el formato esperado (JSON con "productos"/"sucursales").',
    );
  });

  it('el toggle de validar producto persiste el ajuste global', async () => {
    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    const toggle = await screen.findByLabelText<HTMLInputElement>('Validar producto contra el maestro');
    expect(toggle.checked).toBe(true);

    fireEvent.click(toggle);
    await asentar();

    expect(toggle.checked).toBe(false);
    expect((await getAjustes()).validarProducto).toBe(false);
  });

  it('el boton "Sincronizar ahora" dispara el callback del padre', async () => {
    const onSyncNow = vi.fn().mockResolvedValue(undefined);
    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={onSyncNow} />);

    fireEvent.click(screen.getByRole('button', { name: 'Sincronizar ahora' }));

    await act(async () => { await Promise.resolve(); });
    expect(onSyncNow).toHaveBeenCalledTimes(1);
  });

  it('el boton "Limpiar" esta deshabilitado si no hay sesiones limpiables', async () => {
    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    const boton = await screen.findByRole('button', { name: 'Limpiar' });
    expect(boton).toHaveProperty('disabled', true);
  });

  it('"Limpiar" borra las sesiones cerradas y sincronizadas con confirmacion', async () => {
    await createSesion({
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'c1', sessionId: 's1', sectorId: 1, barcode: 'A', productCode: 'P1',
      quantity: 1, inMaster: true, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'synced', lastError: null,
    });
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);

    const boton = await screen.findByRole('button', { name: 'Limpiar (1)' });
    fireEvent.click(boton);

    expect(await screen.findByText('Se eliminaron 1 sesión (1 captura) del equipo.')).toBeTruthy();
    expect(await db.sesiones.count()).toBe(0);
  });

  it('"Exportar a archivo" genera la descarga sin romper', async () => {
    await createSesion({
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });

    render(<MantenedorPage token="tok" onMenuClick={() => {}} onSyncNow={async () => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'Exportar a archivo' }));

    await asentar();
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
  });
});
