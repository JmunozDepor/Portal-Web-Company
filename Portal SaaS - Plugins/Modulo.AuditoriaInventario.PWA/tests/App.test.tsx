import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { App } from '../src/App';
import { db } from '../src/db/schema';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

const LOGIN_BODY = { token: 'tok-123', expiresAt: '2030-01-01T00:00:00Z', displayName: 'Jorge' };
const EMPRESAS_BODY = [{ companyCode: 'DEPOR', name: 'Comercial Depor' }];
const USUARIOS_BODY = [{ username: 'jmunoz', fullName: 'Jorge Muñoz' }];

function stubLoginFetch(loginResponse: () => Response = () => jsonResponse(LOGIN_BODY)) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.includes('/auth/empresas')) return jsonResponse(EMPRESAS_BODY);
    if (url.includes('/auth/usuarios')) return jsonResponse(USUARIOS_BODY);
    if (url.includes('/auth/login')) return loginResponse();
    return jsonResponse({});
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Deja que terminen las promesas de Dexie pendientes dentro de act(). */
async function asentar() {
  await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
}

/** Empresa/Usuario son desplegables poblados por red -- hay que esperar a que carguen antes de elegir. */
async function llenarLogin() {
  await screen.findByRole('option', { name: 'Comercial Depor' });
  fireEvent.change(screen.getByLabelText('Empresa'), { target: { value: 'DEPOR' } });
  await screen.findByRole('option', { name: 'Jorge Muñoz' });
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
  await db.ajustes.clear();
  window.history.pushState({}, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('App (costura de integración)', () => {
  it('tras un login exitoso entra directo a Inicio, sin sincronizar el maestro (I-1)', async () => {
    const fetchMock = stubLoginFetch();

    render(<App />);
    await screen.findByRole('button', { name: 'Ingresar' });

    await llenarLogin();

    // Si App y LoginPage tuvieran instancias separadas de useAuth, el guard de
    // rutas nunca cambiaria y seguiriamos viendo el formulario (C-1).
    expect(await screen.findByText('Conteos disponibles')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Ingresar' })).toBeNull();
    expect(window.location.pathname).toBe('/sesiones');

    const stored = await db.authConfig.get('singleton');
    expect(stored?.token).toBe('tok-123');

    // El maestro es manual-only ahora (se dispara desde Mantenedor): el login
    // no debe llamar a ningun endpoint de /maestro/*.
    const urls = fetchMock.mock.calls.map((c) => String(c[0]));
    expect(urls.some((u) => u.includes('/maestro/'))).toBe(false);
    await asentar();
  });

  it('un login fallido deja el formulario visible con el mensaje de error', async () => {
    stubLoginFetch(() => jsonResponse('no autorizado', 401));

    render(<App />);
    await screen.findByRole('button', { name: 'Ingresar' });

    await llenarLogin();

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Ingresar' })).toBeTruthy();
  });

  it('con sesion ya guardada entra directo a Inicio al recargar, sin pasar por el login', async () => {
    await db.authConfig.put({
      id: 'singleton', token: 'tok-cache', expiresAt: '2030-01-01T00:00:00Z',
      displayName: 'Jorge', companyCode: 'DEPOR',
    });
    stubLoginFetch();

    render(<App />);

    expect(await screen.findByText('Conteos disponibles')).toBeTruthy();
    expect(window.location.pathname).toBe('/sesiones');
    await asentar();
  });

  it('el menu hamburguesa permite salir y vuelve al login', async () => {
    await db.authConfig.put({
      id: 'singleton', token: 'tok-cache', expiresAt: '2030-01-01T00:00:00Z',
      displayName: 'Jorge', companyCode: 'DEPOR',
    });
    stubLoginFetch();

    render(<App />);
    await screen.findByText('Conteos disponibles');

    fireEvent.click(screen.getByRole('button', { name: 'Abrir menú' }));
    fireEvent.click(screen.getByRole('button', { name: 'Salir' }));

    await screen.findByRole('button', { name: 'Ingresar' });
    expect(window.location.pathname).toBe('/login');
    expect(await db.authConfig.get('singleton')).toBeUndefined();
    await asentar();
  });
});
