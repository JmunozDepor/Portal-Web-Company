import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { LoginPage } from '../../../src/features/auth/LoginPage';
import { guardarEmpresasCache, guardarUsuariosCache } from '../../../src/features/auth/offlineDirectory';
import { APP_VERSION } from '../../../src/version';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status });
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('LoginPage', () => {
  it('muestra el estado en línea/sin conexión y la versión del build, ya desde el login', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => jsonResponse([])));
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    expect(await screen.findByText('En línea')).toBeTruthy();
    expect(screen.getByText(new RegExp(`^v${APP_VERSION} `))).toBeTruthy();
  });

  it('precarga Empresa automáticamente si el servidor devuelve una sola opción', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/empresas')) return jsonResponse([{ companyCode: 'DEPOR', name: 'Comercial Depor' }]);
      if (url.includes('/auth/usuarios')) return jsonResponse([{ username: 'jmunoz', fullName: 'Jorge Muñoz' }]);
      return jsonResponse({});
    }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    const empresaSelect = await screen.findByLabelText<HTMLSelectElement>('Empresa');
    expect(empresaSelect.value).toBe('DEPOR');
    // El desplegable de Usuario se pobló solo porque Empresa ya quedó elegida.
    expect(await screen.findByRole('option', { name: 'Jorge Muñoz' })).toBeTruthy();
  });

  it('no precarga Empresa si hay más de una opción, y Usuario queda vacío hasta elegir Empresa', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/empresas')) {
        return jsonResponse([
          { companyCode: 'DEPOR', name: 'Comercial Depor' },
          { companyCode: 'DEPOR_TEST', name: 'Depor (Testing)' },
        ]);
      }
      return jsonResponse([]);
    }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    const empresaSelect = await screen.findByLabelText<HTMLSelectElement>('Empresa');
    await screen.findByRole('option', { name: 'Comercial Depor' });
    expect(empresaSelect.value).toBe('');
    expect(screen.getByLabelText<HTMLSelectElement>('Usuario').disabled).toBe(true);
  });

  it('al elegir Empresa carga los usuarios de esa empresa en el desplegable', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/empresas')) {
        return jsonResponse([
          { companyCode: 'DEPOR', name: 'Comercial Depor' },
          { companyCode: 'DEPOR_TEST', name: 'Depor (Testing)' },
        ]);
      }
      if (url.includes('companyCode=DEPOR_TEST')) return jsonResponse([{ username: 'capturatest', fullName: 'Capturador Test' }]);
      return jsonResponse([]);
    }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    await screen.findByRole('option', { name: 'Comercial Depor' });
    fireEvent.change(screen.getByLabelText('Empresa'), { target: { value: 'DEPOR_TEST' } });

    expect(await screen.findByRole('option', { name: 'Capturador Test' })).toBeTruthy();
    expect(screen.getByLabelText<HTMLSelectElement>('Usuario').disabled).toBe(false);
  });

  it('el boton Ingresar queda deshabilitado hasta elegir Empresa y Usuario', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/empresas')) {
        return jsonResponse([
          { companyCode: 'DEPOR', name: 'Comercial Depor' },
          { companyCode: 'DEPOR_TEST', name: 'Depor (Testing)' },
        ]);
      }
      if (url.includes('/auth/usuarios')) return jsonResponse([{ username: 'jmunoz', fullName: 'Jorge Muñoz' }]);
      return jsonResponse([]);
    }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    await screen.findByRole('option', { name: 'Comercial Depor' });
    expect(screen.getByRole('button', { name: 'Ingresar' })).toHaveProperty('disabled', true);

    fireEvent.change(screen.getByLabelText('Empresa'), { target: { value: 'DEPOR' } });
    await screen.findByRole('option', { name: 'Jorge Muñoz' });
    fireEvent.change(screen.getByLabelText('Usuario'), { target: { value: 'jmunoz' } });

    expect(screen.getByRole('button', { name: 'Ingresar' })).toHaveProperty('disabled', false);
  });

  it('muestra un error con reintento si no se pudo cargar la lista de empresas', async () => {
    const fetchMock = vi.fn(async () => { throw new TypeError('Failed to fetch'); });
    vi.stubGlobal('fetch', fetchMock);

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('No se pudo cargar la lista de empresas.');

    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it('sin conexión, usa la última lista de empresas guardada en el equipo en vez de quedar vacío', async () => {
    guardarEmpresasCache([{ companyCode: 'DEPOR_TEST', name: 'Depor (Testing)' }]);
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    const empresaSelect = await screen.findByLabelText<HTMLSelectElement>('Empresa');
    expect(empresaSelect.value).toBe('DEPOR_TEST');
    expect(screen.queryByRole('alert')).toBeNull();
    expect((await screen.findByRole('status')).textContent).toContain('Sin conexión');
  });

  it('sin conexión y sin nada guardado, muestra el error normal (no puede inventar opciones)', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => { throw new TypeError('Failed to fetch'); }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    const alerta = await screen.findByRole('alert');
    expect(alerta.textContent).toContain('No se pudo cargar la lista de empresas.');
  });

  it('sin conexión, usa los usuarios guardados de la empresa elegida', async () => {
    guardarUsuariosCache('DEPOR_TEST', [{ username: 'capturatest', fullName: 'Capturador Test' }]);
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/empresas')) {
        return jsonResponse([
          { companyCode: 'DEPOR', name: 'Comercial Depor' },
          { companyCode: 'DEPOR_TEST', name: 'Depor (Testing)' },
        ]);
      }
      throw new TypeError('Failed to fetch');
    }));

    render(<LoginPage error={null} onLogin={vi.fn().mockResolvedValue(true)} />);

    await screen.findByRole('option', { name: 'Comercial Depor' });
    fireEvent.change(screen.getByLabelText('Empresa'), { target: { value: 'DEPOR_TEST' } });

    expect(await screen.findByRole('option', { name: 'Capturador Test' })).toBeTruthy();
    expect((await screen.findByRole('status')).textContent).toContain('Sin conexión');
  });

  it('llama a onLogin con la empresa/usuario elegidos y la contraseña tipeada', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/auth/empresas')) return jsonResponse([{ companyCode: 'DEPOR', name: 'Comercial Depor' }]);
      if (url.includes('/auth/usuarios')) return jsonResponse([{ username: 'jmunoz', fullName: 'Jorge Muñoz' }]);
      return jsonResponse({});
    }));
    const onLogin = vi.fn().mockResolvedValue(true);

    render(<LoginPage error={null} onLogin={onLogin} />);

    await screen.findByRole('option', { name: 'Jorge Muñoz' });
    fireEvent.change(screen.getByLabelText('Usuario'), { target: { value: 'jmunoz' } });
    fireEvent.change(screen.getByLabelText('Contraseña'), { target: { value: 'secreto123' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ingresar' }));

    expect(onLogin).toHaveBeenCalledWith('DEPOR', 'jmunoz', 'secreto123');
  });
});
