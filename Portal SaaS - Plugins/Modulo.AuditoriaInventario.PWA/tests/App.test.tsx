import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { App } from '../src/App';
import { db } from '../src/db/schema';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

const LOGIN_BODY = { token: 'tok-123', expiresAt: '2030-01-01T00:00:00Z', displayName: 'Jorge' };
const SUCURSALES_BODY = [{ id: 1, branchCode: 'S1', name: 'Casa Matriz' }];
const PRODUCTOS_BODY = {
  items: [{ id: 1, barcode: '7801234567890', productCode: 'P001', description: 'Zapatilla', brand: null, line: null }],
  hasMore: false,
};

/** fetch feliz: login + maestro completo. */
function stubHappyFetch() {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.includes('/auth/login')) return jsonResponse(LOGIN_BODY);
    if (url.includes('/maestro/sucursales')) return jsonResponse(SUCURSALES_BODY);
    if (url.includes('/maestro/productos')) return jsonResponse(PRODUCTOS_BODY);
    if (url.includes('/maestro/sectores')) return jsonResponse([]);
    return jsonResponse({});
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Deja que terminen las promesas de Dexie pendientes dentro de act(). */
async function asentar() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
}

function llenarLogin() {
  fireEvent.change(screen.getByLabelText('Empresa'), { target: { value: 'DEPOR' } });
  fireEvent.change(screen.getByLabelText('Usuario'), { target: { value: 'jmunoz' } });
  fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'secreto' } });
  fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));
}

beforeEach(async () => {
  await db.authConfig.clear();
  await db.productos.clear();
  await db.sucursales.clear();
  await db.sesiones.clear();
  await db.capturas.clear();
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('App (costura de integración)', () => {
  it('tras un login exitoso muestra la lista de sesiones, sin recargar la página', async () => {
    stubHappyFetch();

    render(<App />);
    await screen.findByRole('button', { name: 'Ingresar' });

    llenarLogin();

    // Si App y LoginPage tuvieran instancias separadas de useAuth, el guard de
    // rutas nunca cambiaria y seguiriamos viendo el formulario (C-1).
    expect(await screen.findByText('Sesiones de conteo')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Ingresar' })).toBeNull();
    expect(window.location.pathname).toBe('/sesiones');

    const stored = await db.authConfig.get('singleton');
    expect(stored?.token).toBe('tok-123');
    await asentar();
  });

  it('un login fallido deja el formulario visible con el mensaje de error', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => jsonResponse('no autorizado', 401)));

    render(<App />);
    await screen.findByRole('button', { name: 'Ingresar' });

    llenarLogin();

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Ingresar' })).toBeTruthy();
  });

  it('no re-descarga el maestro de productos si ya está cacheado', async () => {
    await db.productos.bulkPut(PRODUCTOS_BODY.items);
    await db.sucursales.bulkPut(SUCURSALES_BODY);
    const fetchMock = stubHappyFetch();

    render(<App />);
    await screen.findByRole('button', { name: 'Ingresar' });
    llenarLogin();
    await screen.findByText('Sesiones de conteo');

    const urls = fetchMock.mock.calls.map((c) => String(c[0]));
    expect(urls.some((u) => u.includes('/maestro/productos'))).toBe(false);
    expect(urls.some((u) => u.includes('/maestro/sucursales'))).toBe(false);
    await asentar();
  });

  it('entra igual a la app si la sincronización del maestro falla pero quedaron datos en Dexie', async () => {
    await db.authConfig.put({
      id: 'singleton', token: 'tok-cache', expiresAt: '2030-01-01T00:00:00Z',
      displayName: 'Jorge', companyCode: 'DEPOR',
    });
    await db.sucursales.bulkPut(SUCURSALES_BODY);
    // Productos vacios: se intenta la paginacion, la primera pagina entra y la
    // segunda revienta (red caida a mitad de la descarga).
    let pagina = 0;
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      if (!String(input).includes('/maestro/productos')) throw new TypeError('Failed to fetch');
      pagina += 1;
      if (pagina === 1) return jsonResponse({ items: PRODUCTOS_BODY.items, hasMore: true });
      throw new TypeError('Failed to fetch');
    }));

    render(<App />);

    // No se queda colgado en "Sincronizando maestro...": entra con aviso.
    expect(await screen.findByText('Sesiones de conteo')).toBeTruthy();
    expect(screen.getByRole('status').textContent).toContain('puede estar desactualizado');
    await asentar();
  });

  it('muestra un error con reintento si el maestro está vacío y la sincronización falla', async () => {
    await db.authConfig.put({
      id: 'singleton', token: 'tok-cache', expiresAt: '2030-01-01T00:00:00Z',
      displayName: 'Jorge', companyCode: 'DEPOR',
    });
    const fetchMock = vi.fn(async () => { throw new TypeError('Failed to fetch'); });
    vi.stubGlobal('fetch', fetchMock);

    render(<App />);

    // Ni spinner eterno ni promesa sin capturar: estado de error explicito (C-2).
    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('No se pudo sincronizar el maestro');
    const reintentar = screen.getByRole('button', { name: 'Reintentar' });

    // El reintento vuelve a llamar a la red.
    const llamadasPrevias = fetchMock.mock.calls.length;
    fireEvent.click(reintentar);
    await waitFor(() => expect(fetchMock.mock.calls.length).toBeGreaterThan(llamadasPrevias));
    await asentar();
  });
});
