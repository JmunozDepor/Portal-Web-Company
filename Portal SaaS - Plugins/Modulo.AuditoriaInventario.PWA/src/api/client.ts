export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '';

interface RequestOptions extends RequestInit {
  token?: string;
}

export async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (options.token) {
    headers.Authorization = `Bearer ${options.token}`;
  }

  // Datos de login/maestro que cambian por acción del administrador (alta de
  // capturador, ajuste de formatos) -- 'no-store' evita que el navegador o un
  // proxy/túnel intermedio (ej. devtunnels) sirvan una respuesta vieja.
  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers, cache: 'no-store' });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new ApiError(response.status, text || response.statusText);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
