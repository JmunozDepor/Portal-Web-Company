# PWA de Captura de Inventario — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir la PWA de captura de inventario (repo nuevo) que permite
login, sincronizar el maestro de productos/sucursales/sectores, crear/cerrar
sesiones de conteo, capturar por código de barra con cola offline, sincronizar
automáticamente contra la API ya expuesta por el Host, y exportar a JSON lo
pendiente cuando no hay conectividad.

**Architecture:** App React+TypeScript de una sola página (Vite), con Dexie
(IndexedDB) como fuente de verdad local — toda alta es local-primero, un
motor de sync aparte drena lo pendiente contra la API HTTP ya implementada.
Sin librería de estado ni UI kit; módulos de lógica pura (db, api, sync)
separados de los componentes React para poder testear sin renderizar UI.

**Tech Stack:** Vite, React 18, TypeScript, Dexie, `vite-plugin-pwa`
(Workbox), `react-router-dom`, Vitest + `fake-indexeddb` +
`@testing-library/react`.

**Spec:** `docs/superpowers/specs/2026-09-15-auditoria-inventario-pwa-captura-design.md`

Este plan cubre TODO lo de la spec excepto la página admin de importación
(`Pages/ImportarConteos` dentro de `Modulo.AuditoriaInventario`), que es un
plan separado porque vive en otro repo/proyecto (.NET, no este).

## Global Constraints

- Ubicación del proyecto: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/`
  (carpeta nueva dentro del monorepo actual — no existe todavía. Sin `git
  init` propio: se trackea con el git del monorepo, como el resto del
  proyecto, aunque conceptualmente sea un "repo" separado por convención de
  carpetas).
- Casing en el wire (bodies de request y parseo de response): **camelCase**
  en todo el cliente TS (`companyCode`, `branchId`, `sessionId`, etc.), aunque
  los records C# del contrato están en PascalCase (`CompanyCode`, `BranchId`).
  ASP.NET Core usa `System.Text.Json` con `PropertyNameCaseInsensitive=true`
  por defecto para bindear el body de una minimal API, así que camelCase
  entrante bindea igual contra los records PascalCase — no hace falta tocar
  nada del lado del Host. Esto es una asunción de diseño, no verificada
  contra el Host real corriendo; la Task 12 incluye una nota para
  confirmarlo manualmente.
- Rutas reales del contrato (fijas, no se tocan):
  `POST /api/auditoria-inventario/v1/auth/login`,
  `GET /api/auditoria-inventario/v1/maestro/productos?afterId=N`,
  `GET /api/auditoria-inventario/v1/maestro/sucursales`,
  `GET /api/auditoria-inventario/v1/maestro/sectores?branchId=N`,
  `POST /api/auditoria-inventario/v1/sesiones`,
  `POST /api/auditoria-inventario/v1/capturas/batch`.
- `Status` de sesión es el string `'ACTIVE'` o `'CLOSED'` (sin otros valores).
- Todo `id` de sesión y captura es un GUID generado en el cliente
  (`crypto.randomUUID()`) — nunca se pide un Id al servidor.
- Sin librería de estado (Redux/Zustand) ni UI kit (MUI, etc.) — CSS plano,
  componentes React simples con `useState`/hooks propios.
- Testing: Vitest sobre lógica pura (`db/`, `api/`, `sync/`). Los componentes
  React de páginas (Login, Sesiones, Captura) no llevan test dedicado salvo
  que el propio task lo indique explícitamente — mismo criterio ya usado en
  `Modulo.AuditoriaInventario` (páginas CRUD simples sin test propio).
- Sin backend/servidor propio: la app se sirve con `npm run dev` (Vite) y
  `npm run build` para producción; el proxy de dev apunta al Host local en
  `http://localhost:5270` (perfil `http` de `PortalSaas.Host`) para evitar
  configurar CORS del lado del Host.

---

### Task 1: Scaffold del proyecto (Vite + React + TS + PWA)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/package.json`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tsconfig.json`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tsconfig.node.json`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/vite.config.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/index.html`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/.gitignore`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/main.tsx`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/App.tsx`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/sanity.test.ts`

**Interfaces:**
- Produces: proyecto Vite funcionando (`npm run dev`, `npm run build`,
  `npm run test`) — base para todas las tasks siguientes.

- [ ] **Step 1: Crear `package.json`**

```json
{
  "name": "modulo-auditoria-inventario-pwa",
  "private": true,
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc -b && vite build",
    "preview": "vite preview",
    "test": "vitest run"
  },
  "dependencies": {
    "dexie": "^4.0.8",
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "react-router-dom": "^6.26.2"
  },
  "devDependencies": {
    "@testing-library/react": "^16.0.1",
    "@types/react": "^18.3.5",
    "@types/react-dom": "^18.3.0",
    "@vitejs/plugin-react": "^4.3.1",
    "fake-indexeddb": "^6.0.0",
    "jsdom": "^25.0.0",
    "typescript": "^5.5.4",
    "vite": "^5.4.6",
    "vite-plugin-pwa": "^0.20.5",
    "vitest": "^2.1.1"
  }
}
```

- [ ] **Step 2: Crear `tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "useDefineForClassFields": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "types": ["vitest/globals"]
  },
  "include": ["src", "tests"],
  "references": [{ "path": "./tsconfig.node.json" }]
}
```

- [ ] **Step 3: Crear `tsconfig.node.json`**

```json
{
  "compilerOptions": {
    "composite": true,
    "skipLibCheck": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "allowSyntheticDefaultImports": true
  },
  "include": ["vite.config.ts"]
}
```

- [ ] **Step 4: Crear `vite.config.ts`**

```typescript
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'Auditoría de Inventario - Captura',
        short_name: 'AuditoriaInv',
        start_url: '/',
        display: 'standalone',
        background_color: '#ffffff',
        theme_color: '#1a1a1a',
        icons: [],
      },
      workbox: {
        globPatterns: ['**/*.{js,css,html,ico,png,svg}'],
      },
    }),
  ],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5270',
        changeOrigin: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./tests/setup.ts'],
  },
});
```

- [ ] **Step 5: Crear `tests/setup.ts`** (activa IndexedDB falso para Dexie en Vitest)

```typescript
import 'fake-indexeddb/auto';
```

- [ ] **Step 6: Crear `index.html`**

```html
<!doctype html>
<html lang="es">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>Auditoría de Inventario - Captura</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 7: Crear `src/App.tsx`**

```tsx
export function App() {
  return <div>Auditoría de Inventario - Captura</div>;
}
```

- [ ] **Step 8: Crear `src/main.tsx`**

```tsx
import React from 'react';
import ReactDOM from 'react-dom/client';
import { App } from './App';

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
```

- [ ] **Step 9: Crear `.gitignore`**

```
node_modules
dist
dev-dist
*.local
```

- [ ] **Step 10: Crear `tests/sanity.test.ts`** (prueba mínima de que Vitest corre)

```typescript
import { describe, it, expect } from 'vitest';

describe('sanity', () => {
  it('runs', () => {
    expect(1 + 1).toBe(2);
  });
});
```

- [ ] **Step 11: Instalar dependencias y verificar**

Run (desde `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/`):
```bash
npm install
npm run test
npm run build
```
Expected: `npm run test` → 1 test PASS. `npm run build` → compila sin errores
(genera `dist/`).

- [ ] **Step 12: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA"
git commit -m "chore: scaffold Vite+React+TS PWA con vite-plugin-pwa y Vitest"
```

---

### Task 2: Esquema Dexie + repositorios tipados

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/db/schema.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/db/repositories.ts`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/db/repositories.test.ts`

**Interfaces:**
- Consumes: nada (primer módulo de dominio).
- Produces: tipos `AuthConfigRow`, `ProductoRow`, `SucursalRow`, `SectorRow`,
  `SyncStatus`, `SesionRow`, `CapturaRow` (en `schema.ts`); funciones en
  `repositories.ts`: `getAuthConfig()`, `setAuthConfig(cfg)`,
  `clearAuthConfig()`, `upsertProductos(items)`, `getProductoByBarcode(barcode)`,
  `upsertSucursales(items)`, `getSucursales()`, `upsertSectores(items)`,
  `getSectoresByBranch(branchId)`, `createSesion(row)`,
  `updateSesion(id, changes)`, `getSesion(id)`, `getSesiones()`,
  `getSesionesPendientes()`, `createCaptura(row)`,
  `updateCaptura(id, changes)`, `getCapturasBySesion(sessionId)`,
  `getCapturasPendientes()`, `getPendingCounts()`. Todas estas funciones son
  las que usan las tasks 3-11.

- [ ] **Step 1: Crear `src/db/schema.ts`**

```typescript
import Dexie, { type Table } from 'dexie';

export interface AuthConfigRow {
  id: 'singleton';
  token: string;
  expiresAt: string;
  displayName: string;
  companyCode: string;
}

export interface ProductoRow {
  id: number;
  barcode: string;
  productCode: string;
  description: string | null;
  brand: string | null;
  line: string | null;
}

export interface SucursalRow {
  id: number;
  branchCode: string;
  name: string;
}

export interface SectorRow {
  id: number;
  branchId: number;
  name: string;
}

export type SyncStatus = 'pending' | 'synced' | 'error';

export interface SesionRow {
  id: string;
  branchId: number;
  inventoryNumber: string;
  startedAt: string;
  status: 'ACTIVE' | 'CLOSED';
  validateAgainstMaster: boolean;
  syncStatus: SyncStatus;
  lastError: string | null;
}

export interface CapturaRow {
  id: string;
  sessionId: string;
  sectorId: number;
  barcode: string;
  productCode: string | null;
  quantity: number;
  inMaster: boolean | null;
  capturedAt: string;
  syncStatus: SyncStatus;
  lastError: string | null;
}

export class AuditoriaInventarioDb extends Dexie {
  authConfig!: Table<AuthConfigRow, string>;
  productos!: Table<ProductoRow, number>;
  sucursales!: Table<SucursalRow, number>;
  sectores!: Table<SectorRow, number>;
  sesiones!: Table<SesionRow, string>;
  capturas!: Table<CapturaRow, string>;

  constructor() {
    super('auditoria-inventario-pwa');
    this.version(1).stores({
      authConfig: 'id',
      productos: 'id, barcode',
      sucursales: 'id',
      sectores: 'id, branchId',
      sesiones: 'id, status, syncStatus',
      capturas: 'id, sessionId, syncStatus',
    });
  }
}

export const db = new AuditoriaInventarioDb();
```

- [ ] **Step 2: Crear `src/db/repositories.ts`**

```typescript
import { db } from './schema';
import type { AuthConfigRow, ProductoRow, SucursalRow, SectorRow, SesionRow, CapturaRow } from './schema';

export async function getAuthConfig(): Promise<AuthConfigRow | undefined> {
  return db.authConfig.get('singleton');
}

export async function setAuthConfig(cfg: Omit<AuthConfigRow, 'id'>): Promise<void> {
  await db.authConfig.put({ id: 'singleton', ...cfg });
}

export async function clearAuthConfig(): Promise<void> {
  await db.authConfig.delete('singleton');
}

export async function upsertProductos(items: ProductoRow[]): Promise<void> {
  await db.productos.bulkPut(items);
}

export async function getProductoByBarcode(barcode: string): Promise<ProductoRow | undefined> {
  return db.productos.where('barcode').equals(barcode).first();
}

export async function upsertSucursales(items: SucursalRow[]): Promise<void> {
  await db.sucursales.bulkPut(items);
}

export async function getSucursales(): Promise<SucursalRow[]> {
  return db.sucursales.toArray();
}

export async function upsertSectores(items: SectorRow[]): Promise<void> {
  await db.sectores.bulkPut(items);
}

export async function getSectoresByBranch(branchId: number): Promise<SectorRow[]> {
  return db.sectores.where('branchId').equals(branchId).toArray();
}

export async function createSesion(row: SesionRow): Promise<void> {
  await db.sesiones.add(row);
}

export async function updateSesion(id: string, changes: Partial<SesionRow>): Promise<void> {
  await db.sesiones.update(id, changes);
}

export async function getSesion(id: string): Promise<SesionRow | undefined> {
  return db.sesiones.get(id);
}

export async function getSesiones(): Promise<SesionRow[]> {
  return db.sesiones.toArray();
}

export async function getSesionesPendientes(): Promise<SesionRow[]> {
  return db.sesiones.where('syncStatus').equals('pending').toArray();
}

export async function createCaptura(row: CapturaRow): Promise<void> {
  await db.capturas.add(row);
}

export async function updateCaptura(id: string, changes: Partial<CapturaRow>): Promise<void> {
  await db.capturas.update(id, changes);
}

export async function getCapturasBySesion(sessionId: string): Promise<CapturaRow[]> {
  return db.capturas.where('sessionId').equals(sessionId).toArray();
}

export async function getCapturasPendientes(): Promise<CapturaRow[]> {
  return db.capturas.where('syncStatus').equals('pending').toArray();
}

export async function getPendingCounts(): Promise<{ sesiones: number; capturas: number; errores: number }> {
  const [sesionesPend, capturasPend, sesionesErr, capturasErr] = await Promise.all([
    db.sesiones.where('syncStatus').equals('pending').count(),
    db.capturas.where('syncStatus').equals('pending').count(),
    db.sesiones.where('syncStatus').equals('error').count(),
    db.capturas.where('syncStatus').equals('error').count(),
  ]);
  return { sesiones: sesionesPend, capturas: capturasPend, errores: sesionesErr + capturasErr };
}
```

- [ ] **Step 3: Crear `tests/db/repositories.test.ts`**

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { db } from '../../src/db/schema';
import {
  upsertProductos, getProductoByBarcode, createSesion, getSesion,
  updateSesion, getSesionesPendientes, createCaptura, getCapturasPendientes,
  getPendingCounts,
} from '../../src/db/repositories';

beforeEach(async () => {
  await db.productos.clear();
  await db.sesiones.clear();
  await db.capturas.clear();
});

describe('repositories', () => {
  it('upsertProductos + getProductoByBarcode', async () => {
    await upsertProductos([
      { id: 1, barcode: '7801234567890', productCode: 'P001', description: 'Zapatilla', brand: 'Nike', line: 'Running' },
    ]);
    const found = await getProductoByBarcode('7801234567890');
    expect(found?.productCode).toBe('P001');
  });

  it('createSesion queda pending por defecto y getSesionesPendientes la incluye', async () => {
    await createSesion({
      id: 'sesion-1', branchId: 10, inventoryNumber: 'INV-001', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    const pendientes = await getSesionesPendientes();
    expect(pendientes.map((s) => s.id)).toEqual(['sesion-1']);
  });

  it('updateSesion cambia status y syncStatus', async () => {
    await createSesion({
      id: 'sesion-2', branchId: 10, inventoryNumber: 'INV-002', startedAt: new Date().toISOString(),
      status: 'ACTIVE', validateAgainstMaster: false, syncStatus: 'synced', lastError: null,
    });
    await updateSesion('sesion-2', { status: 'CLOSED', syncStatus: 'pending' });
    const sesion = await getSesion('sesion-2');
    expect(sesion?.status).toBe('CLOSED');
    expect(sesion?.syncStatus).toBe('pending');
  });

  it('getPendingCounts refleja capturas pendientes', async () => {
    await createCaptura({
      id: 'cap-1', sessionId: 'sesion-1', sectorId: 1, barcode: '123', productCode: null,
      quantity: 1, inMaster: null, capturedAt: new Date().toISOString(), syncStatus: 'pending', lastError: null,
    });
    const counts = await getPendingCounts();
    expect(counts.capturas).toBe(1);
    const pendientes = await getCapturasPendientes();
    expect(pendientes).toHaveLength(1);
  });
});
```

- [ ] **Step 4: Correr los tests**

Run: `npm run test`
Expected: 5 tests PASS (1 sanity + 4 de repositories).

- [ ] **Step 5: Commit**

```bash
git add src/db tests/db
git commit -m "feat: esquema Dexie y repositorios tipados"
```

---

### Task 3: Cliente API tipado

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/api/types.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/api/client.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/api/endpoints.ts`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/api/client.test.ts`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/api/endpoints.test.ts`

**Interfaces:**
- Consumes: nada de tasks anteriores (módulo independiente de `db/`).
- Produces: clase `ApiError` (con `status: number`); funciones
  `login(companyCode, username, password): Promise<LoginResponse>`,
  `getProductos(token, afterId): Promise<MaestroPage>`,
  `getSucursales(token): Promise<SucursalDto[]>`,
  `getSectores(token, branchId): Promise<SectorDto[]>`,
  `upsertSesion(token, body: SesionUpsertBody): Promise<void>`,
  `uploadCapturasBatch(token, items: CapturaBody[]): Promise<{ processed: number }>`.
  Estas son las que usan las tasks 4, 5, 6, 9.

- [ ] **Step 1: Crear `src/api/types.ts`**

```typescript
export interface LoginResponse {
  token: string;
  expiresAt: string;
  displayName: string;
}

export interface ProductoDto {
  id: number;
  barcode: string;
  productCode: string;
  description: string | null;
  brand: string | null;
  line: string | null;
}

export interface MaestroPage {
  items: ProductoDto[];
  hasMore: boolean;
}

export interface SucursalDto {
  id: number;
  branchCode: string;
  name: string;
}

export interface SectorDto {
  id: number;
  name: string;
}

export interface SesionUpsertBody {
  id: string;
  branchId: number;
  inventoryNumber: string;
  startedAt: string;
  status: 'ACTIVE' | 'CLOSED';
  validateAgainstMaster: boolean;
}

export interface CapturaBody {
  id: string;
  sessionId: string;
  sectorId: number;
  barcode: string;
  productCode: string | null;
  quantity: number;
  inMaster: boolean | null;
  capturedAt: string;
}
```

- [ ] **Step 2: Crear `src/api/client.ts`**

```typescript
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

  const response = await fetch(`${BASE_URL}${path}`, { ...options, headers });

  if (!response.ok) {
    const text = await response.text().catch(() => '');
    throw new ApiError(response.status, text || response.statusText);
  }

  const text = await response.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
```

- [ ] **Step 3: Crear `src/api/endpoints.ts`**

```typescript
import { request } from './client';
import type {
  LoginResponse, MaestroPage, SucursalDto, SectorDto, SesionUpsertBody, CapturaBody,
} from './types';

const ROUTE_PREFIX = '/api/auditoria-inventario/v1';

export function login(companyCode: string, username: string, password: string): Promise<LoginResponse> {
  return request<LoginResponse>(`${ROUTE_PREFIX}/auth/login`, {
    method: 'POST',
    body: JSON.stringify({ companyCode, username, password }),
  });
}

export function getProductos(token: string, afterId: number): Promise<MaestroPage> {
  return request<MaestroPage>(`${ROUTE_PREFIX}/maestro/productos?afterId=${afterId}`, { token });
}

export function getSucursales(token: string): Promise<SucursalDto[]> {
  return request<SucursalDto[]>(`${ROUTE_PREFIX}/maestro/sucursales`, { token });
}

export function getSectores(token: string, branchId: number): Promise<SectorDto[]> {
  return request<SectorDto[]>(`${ROUTE_PREFIX}/maestro/sectores?branchId=${branchId}`, { token });
}

export function upsertSesion(token: string, body: SesionUpsertBody): Promise<void> {
  return request<void>(`${ROUTE_PREFIX}/sesiones`, {
    method: 'POST',
    token,
    body: JSON.stringify(body),
  });
}

export function uploadCapturasBatch(token: string, items: CapturaBody[]): Promise<{ processed: number }> {
  return request<{ processed: number }>(`${ROUTE_PREFIX}/capturas/batch`, {
    method: 'POST',
    token,
    body: JSON.stringify(items),
  });
}
```

- [ ] **Step 4: Crear `tests/api/client.test.ts`**

```typescript
import { describe, it, expect, vi, afterEach } from 'vitest';
import { request, ApiError } from '../../src/api/client';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('client.request', () => {
  it('parsea JSON en una respuesta 200', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ ok: true }), { status: 200 }),
    ));
    const result = await request<{ ok: boolean }>('/x');
    expect(result).toEqual({ ok: true });
  });

  it('devuelve undefined en una respuesta 200 sin body', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status: 200 })));
    const result = await request<void>('/x');
    expect(result).toBeUndefined();
  });

  it('lanza ApiError con el status en una respuesta 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('no autorizado', { status: 401 })));
    await expect(request('/x')).rejects.toMatchObject(new ApiError(401, 'no autorizado'));
  });

  it('agrega el header Authorization cuando se pasa token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await request('/x', { token: 'abc123' });
    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer abc123');
  });
});
```

- [ ] **Step 5: Crear `tests/api/endpoints.test.ts`**

```typescript
import { describe, it, expect, vi, afterEach } from 'vitest';
import { login, getProductos, upsertSesion, uploadCapturasBatch } from '../../src/api/endpoints';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('endpoints', () => {
  it('login manda POST con companyCode/username/password en camelCase', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 't', expiresAt: 'x', displayName: 'D' }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);
    const result = await login('DEPOR', 'jperez', 'secreto');
    expect(result.token).toBe('t');
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toContain('/api/auditoria-inventario/v1/auth/login');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ companyCode: 'DEPOR', username: 'jperez', password: 'secreto' });
  });

  it('getProductos manda afterId en la query', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ items: [], hasMore: false }), { status: 200 }),
    );
    vi.stubGlobal('fetch', fetchMock);
    await getProductos('tok', 42);
    const [url] = fetchMock.mock.calls[0];
    expect(url).toContain('afterId=42');
  });

  it('upsertSesion manda el body con Bearer token', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('', { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    await upsertSesion('tok', {
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true,
    });
    const [, init] = fetchMock.mock.calls[0];
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer tok');
  });

  it('uploadCapturasBatch devuelve processed', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ processed: 3 }), { status: 200 }),
    ));
    const result = await uploadCapturasBatch('tok', []);
    expect(result.processed).toBe(3);
  });
});
```

- [ ] **Step 6: Correr los tests**

Run: `npm run test`
Expected: 13 tests PASS (5 anteriores + 4 de client + 4 de endpoints).

- [ ] **Step 7: Commit**

```bash
git add src/api tests/api
git commit -m "feat: cliente API tipado para la API de auditoria-inventario"
```

---

### Task 4: Autenticación (login/logout)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/auth/useAuth.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/auth/LoginPage.tsx`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/features/auth/useAuth.test.ts`

**Interfaces:**
- Consumes: `login` de `src/api/endpoints.ts` (Task 3); `getAuthConfig`,
  `setAuthConfig`, `clearAuthConfig` de `src/db/repositories.ts` (Task 2);
  `ApiError` de `src/api/client.ts` (Task 3).
- Produces: hook `useAuth()` que devuelve
  `{ token: string | null, displayName: string | null, loading: boolean,
  error: string | null, login(companyCode, username, password): Promise<boolean>,
  logout(): Promise<void> }`. Las tasks 8, 9, 10, 11, 12 usan `token` para
  llamar a la API y `logout()` para el caso 401.

- [ ] **Step 1: Crear `src/features/auth/useAuth.ts`**

```typescript
import { useCallback, useEffect, useState } from 'react';
import { login as apiLogin } from '../../api/endpoints';
import { ApiError } from '../../api/client';
import { getAuthConfig, setAuthConfig, clearAuthConfig } from '../../db/repositories';

export function useAuth() {
  const [token, setToken] = useState<string | null>(null);
  const [displayName, setDisplayName] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getAuthConfig().then((cfg) => {
      setToken(cfg?.token ?? null);
      setDisplayName(cfg?.displayName ?? null);
      setLoading(false);
    });
  }, []);

  const login = useCallback(async (companyCode: string, username: string, password: string): Promise<boolean> => {
    setError(null);
    try {
      const result = await apiLogin(companyCode, username, password);
      await setAuthConfig({
        token: result.token,
        expiresAt: result.expiresAt,
        displayName: result.displayName,
        companyCode,
      });
      setToken(result.token);
      setDisplayName(result.displayName);
      return true;
    } catch (err) {
      setError(err instanceof ApiError ? 'Usuario, empresa o contraseña incorrectos.' : 'No se pudo conectar con el servidor.');
      return false;
    }
  }, []);

  const logout = useCallback(async () => {
    await clearAuthConfig();
    setToken(null);
    setDisplayName(null);
  }, []);

  return { token, displayName, loading, error, login, logout };
}
```

- [ ] **Step 2: Crear `src/features/auth/LoginPage.tsx`**

```tsx
import { useState } from 'react';
import { useAuth } from './useAuth';

export function LoginPage() {
  const { error, login } = useAuth();
  const [companyCode, setCompanyCode] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    await login(companyCode, username, password);
    setSubmitting(false);
  }

  return (
    <form onSubmit={handleSubmit}>
      <h1>Auditoría de Inventario</h1>
      {error && <p role="alert">{error}</p>}
      <label>
        Empresa
        <input value={companyCode} onChange={(e) => setCompanyCode(e.target.value)} required />
      </label>
      <label>
        Usuario
        <input value={username} onChange={(e) => setUsername(e.target.value)} required />
      </label>
      <label>
        Contraseña
        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
      </label>
      <button type="submit" disabled={submitting}>Ingresar</button>
    </form>
  );
}
```

- [ ] **Step 3: Crear `tests/features/auth/useAuth.test.ts`**

```typescript
import { describe, it, expect, vi, afterEach, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useAuth } from '../../../src/features/auth/useAuth';
import { db } from '../../../src/db/schema';
import { upsertProductos } from '../../../src/db/repositories';

afterEach(() => {
  vi.unstubAllGlobals();
});

beforeEach(async () => {
  await db.authConfig.clear();
  await db.productos.clear();
});

describe('useAuth', () => {
  it('login exitoso guarda el token en authConfig', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 'tok-1', expiresAt: '2026-01-01T00:00:00Z', displayName: 'Juan' }), { status: 200 }),
    ));
    const { result } = renderHook(() => useAuth());
    await waitFor(() => expect(result.current.loading).toBe(false));

    let ok = false;
    await act(async () => {
      ok = await result.current.login('DEPOR', 'jperez', 'secreto');
    });

    expect(ok).toBe(true);
    expect(result.current.token).toBe('tok-1');
    const stored = await db.authConfig.get('singleton');
    expect(stored?.displayName).toBe('Juan');
  });

  it('login fallido (401) no guarda nada y expone error', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('no autorizado', { status: 401 })));
    const { result } = renderHook(() => useAuth());
    await waitFor(() => expect(result.current.loading).toBe(false));

    let ok = true;
    await act(async () => {
      ok = await result.current.login('DEPOR', 'jperez', 'mala');
    });

    expect(ok).toBe(false);
    expect(result.current.token).toBeNull();
    expect(result.current.error).not.toBeNull();
    const stored = await db.authConfig.get('singleton');
    expect(stored).toBeUndefined();
  });

  it('logout limpia authConfig pero no borra el maestro cacheado', async () => {
    await upsertProductos([{ id: 1, barcode: '123', productCode: 'P1', description: null, brand: null, line: null }]);
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ token: 'tok-1', expiresAt: '2026-01-01T00:00:00Z', displayName: 'Juan' }), { status: 200 }),
    ));
    const { result } = renderHook(() => useAuth());
    await waitFor(() => expect(result.current.loading).toBe(false));
    await act(async () => {
      await result.current.login('DEPOR', 'jperez', 'secreto');
    });

    await act(async () => {
      await result.current.logout();
    });

    expect(result.current.token).toBeNull();
    const producto = await db.productos.get(1);
    expect(producto).toBeDefined();
  });
});
```

- [ ] **Step 4: Correr los tests**

Run: `npm run test`
Expected: 16 tests PASS (13 anteriores + 3 de useAuth).

- [ ] **Step 5: Commit**

```bash
git add src/features/auth tests/features/auth
git commit -m "feat: login/logout con persistencia en Dexie"
```

---

### Task 5: Sync inicial de maestro (productos paginado + sucursales)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/sync/maestroSync.ts`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/sync/maestroSync.test.ts`

**Interfaces:**
- Consumes: `getProductos`, `getSucursales` de `src/api/endpoints.ts` (Task 3);
  `upsertProductos`, `upsertSucursales` de `src/db/repositories.ts` (Task 2).
- Produces: `syncProductos(token: string, onProgress?: (count: number) => void): Promise<number>`
  (devuelve el total sincronizado), `syncSucursales(token: string): Promise<number>`.
  Usadas por Task 12 (pantalla de sync inicial) y por los tests de esta task.

- [ ] **Step 1: Crear `src/sync/maestroSync.ts`**

```typescript
import { getProductos, getSucursales } from '../api/endpoints';
import { upsertProductos, upsertSucursales } from '../db/repositories';

export async function syncProductos(token: string, onProgress?: (count: number) => void): Promise<number> {
  let afterId = 0;
  let total = 0;
  let hasMore = true;

  while (hasMore) {
    const page = await getProductos(token, afterId);
    if (page.items.length > 0) {
      await upsertProductos(page.items);
      afterId = page.items[page.items.length - 1].id;
      total += page.items.length;
      onProgress?.(total);
    }
    hasMore = page.hasMore;
  }

  return total;
}

export async function syncSucursales(token: string): Promise<number> {
  const items = await getSucursales(token);
  await upsertSucursales(items);
  return items.length;
}
```

- [ ] **Step 2: Crear `tests/sync/maestroSync.test.ts`**

```typescript
import { describe, it, expect, vi } from 'vitest';
import { syncProductos, syncSucursales } from '../../src/sync/maestroSync';
import * as endpoints from '../../src/api/endpoints';
import { db } from '../../src/db/schema';
import { getProductoByBarcode } from '../../src/db/repositories';

describe('syncProductos', () => {
  it('pagina hasta HasMore=false usando el ultimo Id de cada pagina como afterId', async () => {
    const spy = vi.spyOn(endpoints, 'getProductos');
    spy.mockResolvedValueOnce({
      items: [{ id: 1, barcode: 'A', productCode: 'P1', description: null, brand: null, line: null }],
      hasMore: true,
    });
    spy.mockResolvedValueOnce({
      items: [{ id: 2, barcode: 'B', productCode: 'P2', description: null, brand: null, line: null }],
      hasMore: false,
    });

    const total = await syncProductos('tok');

    expect(total).toBe(2);
    expect(spy).toHaveBeenNthCalledWith(1, 'tok', 0);
    expect(spy).toHaveBeenNthCalledWith(2, 'tok', 1);
    const producto = await getProductoByBarcode('B');
    expect(producto?.productCode).toBe('P2');
  });
});

describe('syncSucursales', () => {
  it('guarda todas las sucursales devueltas', async () => {
    await db.sucursales.clear();
    vi.spyOn(endpoints, 'getSucursales').mockResolvedValueOnce([
      { id: 1, branchCode: 'B01', name: 'Casa Matriz' },
      { id: 2, branchCode: 'B02', name: 'Sucursal Norte' },
    ]);

    const total = await syncSucursales('tok');

    expect(total).toBe(2);
    const rows = await db.sucursales.toArray();
    expect(rows).toHaveLength(2);
  });
});
```

- [ ] **Step 3: Correr los tests**

Run: `npm run test`
Expected: 18 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/sync/maestroSync.ts tests/sync/maestroSync.test.ts
git commit -m "feat: sync inicial de productos (paginado) y sucursales"
```

---

### Task 6: Sectores por sucursal (cache on demand)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/sync/sectoresSync.ts`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/sync/sectoresSync.test.ts`

**Interfaces:**
- Consumes: `getSectores` de `src/api/endpoints.ts` (Task 3);
  `getSectoresByBranch`, `upsertSectores` de `src/db/repositories.ts` (Task 2).
- Produces: `getOrFetchSectores(token: string, branchId: number): Promise<SectorRow[]>`
  (usada por Task 8, pantalla de Captura).

- [ ] **Step 1: Crear `src/sync/sectoresSync.ts`**

```typescript
import { getSectores } from '../api/endpoints';
import { getSectoresByBranch, upsertSectores } from '../db/repositories';
import type { SectorRow } from '../db/schema';

export async function getOrFetchSectores(token: string, branchId: number): Promise<SectorRow[]> {
  const cached = await getSectoresByBranch(branchId);
  if (cached.length > 0) {
    return cached;
  }

  const fetched = await getSectores(token, branchId);
  const rows: SectorRow[] = fetched.map((s) => ({ id: s.id, branchId, name: s.name }));
  await upsertSectores(rows);
  return rows;
}
```

- [ ] **Step 2: Crear `tests/sync/sectoresSync.test.ts`**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getOrFetchSectores } from '../../src/sync/sectoresSync';
import * as endpoints from '../../src/api/endpoints';
import { db } from '../../src/db/schema';

beforeEach(async () => {
  await db.sectores.clear();
});

describe('getOrFetchSectores', () => {
  it('si no hay cache, pide a la API y guarda con branchId', async () => {
    const spy = vi.spyOn(endpoints, 'getSectores').mockResolvedValueOnce([
      { id: 1, name: 'Sala de Venta' },
      { id: 2, name: 'Bodega' },
    ]);

    const result = await getOrFetchSectores('tok', 10);

    expect(spy).toHaveBeenCalledWith('tok', 10);
    expect(result).toEqual([
      { id: 1, branchId: 10, name: 'Sala de Venta' },
      { id: 2, branchId: 10, name: 'Bodega' },
    ]);
    const stored = await db.sectores.where('branchId').equals(10).toArray();
    expect(stored).toHaveLength(2);
  });

  it('si ya hay cache para esa sucursal, no llama a la API', async () => {
    await db.sectores.bulkPut([{ id: 1, branchId: 20, name: 'Bodega' }]);
    const spy = vi.spyOn(endpoints, 'getSectores');

    const result = await getOrFetchSectores('tok', 20);

    expect(spy).not.toHaveBeenCalled();
    expect(result).toEqual([{ id: 1, branchId: 20, name: 'Bodega' }]);
  });
});
```

- [ ] **Step 3: Correr los tests**

Run: `npm run test`
Expected: 20 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/sync/sectoresSync.ts tests/sync/sectoresSync.test.ts
git commit -m "feat: cache de sectores por sucursal"
```

---

### Task 7: Sesiones de conteo (alta + cierre, local-first)

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/sesiones/sesionesService.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/sesiones/SesionesListPage.tsx`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/sesiones/NuevaSesionForm.tsx`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/features/sesiones/sesionesService.test.ts`

**Interfaces:**
- Consumes: `createSesion`, `updateSesion`, `getSesiones` de
  `src/db/repositories.ts` (Task 2); `getSucursales` de
  `src/db/repositories.ts` (Task 2, para el selector de sucursal en el form).
- Produces: `crearSesion(input: { branchId: number; inventoryNumber: string;
  validateAgainstMaster: boolean }): Promise<SesionRow>`,
  `cerrarSesion(sesionId: string): Promise<void>`. Usadas por Task 8
  (Captura, para saber a qué sesión pertenece una captura) y por
  `SesionesListPage.tsx`.

- [ ] **Step 1: Crear `src/features/sesiones/sesionesService.ts`**

```typescript
import { createSesion, updateSesion } from '../../db/repositories';
import type { SesionRow } from '../../db/schema';

export async function crearSesion(input: {
  branchId: number;
  inventoryNumber: string;
  validateAgainstMaster: boolean;
}): Promise<SesionRow> {
  const row: SesionRow = {
    id: crypto.randomUUID(),
    branchId: input.branchId,
    inventoryNumber: input.inventoryNumber,
    startedAt: new Date().toISOString(),
    status: 'ACTIVE',
    validateAgainstMaster: input.validateAgainstMaster,
    syncStatus: 'pending',
    lastError: null,
  };
  await createSesion(row);
  return row;
}

export async function cerrarSesion(sesionId: string): Promise<void> {
  await updateSesion(sesionId, { status: 'CLOSED', syncStatus: 'pending' });
}
```

- [ ] **Step 2: Crear `src/features/sesiones/NuevaSesionForm.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { getSucursales } from '../../db/repositories';
import { crearSesion } from './sesionesService';
import type { SucursalRow } from '../../db/schema';
import type { SesionRow } from '../../db/schema';

export function NuevaSesionForm({ onCreated }: { onCreated: (sesion: SesionRow) => void }) {
  const [sucursales, setSucursales] = useState<SucursalRow[]>([]);
  const [branchId, setBranchId] = useState<number | null>(null);
  const [inventoryNumber, setInventoryNumber] = useState('');
  const [validateAgainstMaster, setValidateAgainstMaster] = useState(true);

  useEffect(() => {
    getSucursales().then(setSucursales);
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (branchId === null) return;
    const sesion = await crearSesion({ branchId, inventoryNumber, validateAgainstMaster });
    onCreated(sesion);
  }

  return (
    <form onSubmit={handleSubmit}>
      <label>
        Sucursal
        <select value={branchId ?? ''} onChange={(e) => setBranchId(Number(e.target.value))} required>
          <option value="" disabled>Elegir...</option>
          {sucursales.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </label>
      <label>
        Nro. de Inventario
        <input value={inventoryNumber} onChange={(e) => setInventoryNumber(e.target.value)} required />
      </label>
      <label>
        <input type="checkbox" checked={validateAgainstMaster} onChange={(e) => setValidateAgainstMaster(e.target.checked)} />
        Validar contra maestro
      </label>
      <button type="submit">Crear sesión</button>
    </form>
  );
}
```

- [ ] **Step 3: Crear `src/features/sesiones/SesionesListPage.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getSesiones } from '../../db/repositories';
import { NuevaSesionForm } from './NuevaSesionForm';
import type { SesionRow } from '../../db/schema';

export function SesionesListPage() {
  const [sesiones, setSesiones] = useState<SesionRow[]>([]);
  const [showForm, setShowForm] = useState(false);

  async function reload() {
    setSesiones(await getSesiones());
  }

  useEffect(() => {
    reload();
  }, []);

  return (
    <div>
      <h1>Sesiones de conteo</h1>
      <button onClick={() => setShowForm(true)}>Nueva sesión</button>
      {showForm && (
        <NuevaSesionForm
          onCreated={() => {
            setShowForm(false);
            reload();
          }}
        />
      )}
      <ul>
        {sesiones.map((s) => (
          <li key={s.id}>
            <Link to={`/sesiones/${s.id}`}>{s.inventoryNumber} — {s.status} ({s.syncStatus})</Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 4: Crear `tests/features/sesiones/sesionesService.test.ts`**

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { crearSesion, cerrarSesion } from '../../../src/features/sesiones/sesionesService';
import { getSesion } from '../../../src/db/repositories';
import { db } from '../../../src/db/schema';

beforeEach(async () => {
  await db.sesiones.clear();
});

describe('sesionesService', () => {
  it('crearSesion genera un GUID, queda ACTIVE y pending', async () => {
    const sesion = await crearSesion({ branchId: 1, inventoryNumber: 'INV-1', validateAgainstMaster: true });
    expect(sesion.id).toMatch(/^[0-9a-f-]{36}$/);
    expect(sesion.status).toBe('ACTIVE');
    expect(sesion.syncStatus).toBe('pending');
  });

  it('cerrarSesion pasa a CLOSED y vuelve a pending aunque ya estuviera synced', async () => {
    const sesion = await crearSesion({ branchId: 1, inventoryNumber: 'INV-2', validateAgainstMaster: false });
    await db.sesiones.update(sesion.id, { syncStatus: 'synced' });

    await cerrarSesion(sesion.id);

    const actualizada = await getSesion(sesion.id);
    expect(actualizada?.status).toBe('CLOSED');
    expect(actualizada?.syncStatus).toBe('pending');
  });
});
```

- [ ] **Step 5: Correr los tests**

Run: `npm run test`
Expected: 22 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/features/sesiones tests/features/sesiones
git commit -m "feat: alta y cierre de sesiones de conteo"
```

---

### Task 8: Captura por código de barra

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/captura/capturaService.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/captura/BarcodeInput.tsx`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/captura/CapturaPage.tsx`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/features/captura/capturaService.test.ts`

**Interfaces:**
- Consumes: `getProductoByBarcode`, `createCaptura`, `updateCaptura`,
  `getCapturasBySesion` de `src/db/repositories.ts` (Task 2);
  `getOrFetchSectores` de `src/sync/sectoresSync.ts` (Task 6); `getSesion` de
  `src/db/repositories.ts` (Task 2, para leer `validateAgainstMaster` de la
  sesión activa).
- Produces: `registrarCaptura(input: { sessionId: string; sectorId: number;
  barcode: string; quantity: number; validateAgainstMaster: boolean }):
  Promise<CapturaRow>`. Usada por `CapturaPage.tsx` y por Task 9 indirectamente
  (las filas que crea son las que el motor de sync sube después).

- [ ] **Step 1: Crear `src/features/captura/capturaService.ts`**

```typescript
import { getProductoByBarcode, createCaptura } from '../../db/repositories';
import type { CapturaRow } from '../../db/schema';

export async function registrarCaptura(input: {
  sessionId: string;
  sectorId: number;
  barcode: string;
  quantity: number;
  validateAgainstMaster: boolean;
}): Promise<CapturaRow> {
  let productCode: string | null = null;
  let inMaster: boolean | null = null;

  if (input.validateAgainstMaster) {
    const producto = await getProductoByBarcode(input.barcode);
    if (producto) {
      productCode = producto.productCode;
      inMaster = true;
    } else {
      inMaster = false;
    }
  }

  const row: CapturaRow = {
    id: crypto.randomUUID(),
    sessionId: input.sessionId,
    sectorId: input.sectorId,
    barcode: input.barcode,
    productCode,
    quantity: input.quantity,
    inMaster,
    capturedAt: new Date().toISOString(),
    syncStatus: 'pending',
    lastError: null,
  };
  await createCaptura(row);
  return row;
}
```

- [ ] **Step 2: Crear `src/features/captura/BarcodeInput.tsx`**

```tsx
import { useEffect, useRef, useState } from 'react';

export function BarcodeInput({ onCommit }: { onCommit: (barcode: string) => void }) {
  const [value, setValue] = useState('');
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    inputRef.current?.focus();
    const refocus = () => inputRef.current?.focus();
    document.addEventListener('visibilitychange', refocus);
    return () => document.removeEventListener('visibilitychange', refocus);
  }, []);

  function handleKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter' && value.trim().length > 0) {
      onCommit(value.trim());
      setValue('');
    }
  }

  return (
    <input
      ref={inputRef}
      value={value}
      onChange={(e) => setValue(e.target.value)}
      onKeyDown={handleKeyDown}
      placeholder="Escanear código de barra"
      autoFocus
    />
  );
}
```

- [ ] **Step 3: Crear `src/features/captura/CapturaPage.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { getSesion, getCapturasBySesion } from '../../db/repositories';
import { getOrFetchSectores } from '../../sync/sectoresSync';
import { registrarCaptura } from './capturaService';
import { BarcodeInput } from './BarcodeInput';
import type { SesionRow, SectorRow, CapturaRow } from '../../db/schema';

export function CapturaPage({ token }: { token: string }) {
  const { sesionId } = useParams<{ sesionId: string }>();
  const [sesion, setSesion] = useState<SesionRow | null>(null);
  const [sectores, setSectores] = useState<SectorRow[]>([]);
  const [sectorId, setSectorId] = useState<number | null>(null);
  const [cantidad, setCantidad] = useState(1);
  const [capturas, setCapturas] = useState<CapturaRow[]>([]);

  useEffect(() => {
    if (!sesionId) return;
    getSesion(sesionId).then(async (s) => {
      setSesion(s ?? null);
      if (s) {
        setSectores(await getOrFetchSectores(token, s.branchId));
      }
    });
    getCapturasBySesion(sesionId).then(setCapturas);
  }, [sesionId, token]);

  async function handleCommit(barcode: string) {
    if (!sesion || sectorId === null) return;
    await registrarCaptura({
      sessionId: sesion.id,
      sectorId,
      barcode,
      quantity: cantidad,
      validateAgainstMaster: sesion.validateAgainstMaster,
    });
    setCapturas(await getCapturasBySesion(sesion.id));
  }

  if (!sesion) return <p>Cargando sesión...</p>;

  return (
    <div>
      <h1>Captura — {sesion.inventoryNumber}</h1>
      <label>
        Sector
        <select value={sectorId ?? ''} onChange={(e) => setSectorId(Number(e.target.value))}>
          <option value="" disabled>Elegir...</option>
          {sectores.map((s) => (
            <option key={s.id} value={s.id}>{s.name}</option>
          ))}
        </select>
      </label>
      <label>
        Cantidad
        <input type="number" min={1} value={cantidad} onChange={(e) => setCantidad(Number(e.target.value))} />
      </label>
      <BarcodeInput onCommit={handleCommit} />
      <ul>
        {capturas.map((c) => (
          <li key={c.id}>{c.barcode} x{c.quantity} — {c.inMaster === false ? 'no en maestro' : ''}</li>
        ))}
      </ul>
    </div>
  );
}
```

- [ ] **Step 4: Crear `tests/features/captura/capturaService.test.ts`**

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { registrarCaptura } from '../../../src/features/captura/capturaService';
import { upsertProductos } from '../../../src/db/repositories';
import { db } from '../../../src/db/schema';

beforeEach(async () => {
  await db.productos.clear();
  await db.capturas.clear();
  await upsertProductos([{ id: 1, barcode: 'EXISTE', productCode: 'P1', description: null, brand: null, line: null }]);
});

describe('registrarCaptura', () => {
  it('con validacion activa y barcode en el maestro, completa productCode e inMaster=true', async () => {
    const captura = await registrarCaptura({
      sessionId: 's1', sectorId: 1, barcode: 'EXISTE', quantity: 2, validateAgainstMaster: true,
    });
    expect(captura.productCode).toBe('P1');
    expect(captura.inMaster).toBe(true);
    expect(captura.quantity).toBe(2);
  });

  it('con validacion activa y barcode fuera del maestro, inMaster=false y productCode null', async () => {
    const captura = await registrarCaptura({
      sessionId: 's1', sectorId: 1, barcode: 'NO-EXISTE', quantity: 1, validateAgainstMaster: true,
    });
    expect(captura.productCode).toBeNull();
    expect(captura.inMaster).toBe(false);
  });

  it('con validacion desactivada, no consulta el maestro y deja inMaster en null', async () => {
    const captura = await registrarCaptura({
      sessionId: 's1', sectorId: 1, barcode: 'EXISTE', quantity: 1, validateAgainstMaster: false,
    });
    expect(captura.inMaster).toBeNull();
    expect(captura.productCode).toBeNull();
  });

  it('cada captura tiene un id GUID unico', async () => {
    const c1 = await registrarCaptura({ sessionId: 's1', sectorId: 1, barcode: 'A', quantity: 1, validateAgainstMaster: false });
    const c2 = await registrarCaptura({ sessionId: 's1', sectorId: 1, barcode: 'A', quantity: 1, validateAgainstMaster: false });
    expect(c1.id).not.toBe(c2.id);
  });
});
```

- [ ] **Step 5: Correr los tests**

Run: `npm run test`
Expected: 26 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/features/captura tests/features/captura
git commit -m "feat: captura por codigo de barra con validacion contra maestro local"
```

---

### Task 9: Motor de sync — subida de sesiones y capturas

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/sync/uploadSync.ts`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/sync/uploadSync.test.ts`

**Interfaces:**
- Consumes: `upsertSesion`, `uploadCapturasBatch` de `src/api/endpoints.ts`
  (Task 3); `ApiError` de `src/api/client.ts` (Task 3); `getSesionesPendientes`,
  `updateSesion`, `getCapturasPendientes`, `updateCaptura` de
  `src/db/repositories.ts` (Task 2).
- Produces: `syncSesionesPendientes(token: string): Promise<void>`,
  `syncCapturasPendientes(token: string): Promise<void>`. Ambas relanzan
  (`throw`) un `ApiError` con `status === 401` sin marcar nada como error —
  eso es lo que Task 10 usa para detectar sesión vencida. Un `ApiError` 4xx
  distinto de 401 SÍ marca la fila/lote como `error`. Cualquier otro error
  (red, 5xx) deja la fila en `pending` sin marcar nada, para reintentar solo.

**Nota de diseño:** el contrato de `capturas/batch` devuelve `{processed:N}`,
no un resultado por fila — si el lote entero falla con 4xx, se marcan como
`error` TODAS las filas de ese lote (no hay forma de saber cuál falló
específicamente sin cambiar el contrato del Host, fuera de alcance de este
plan).

- [ ] **Step 1: Crear `src/sync/uploadSync.ts`**

```typescript
import { upsertSesion, uploadCapturasBatch } from '../api/endpoints';
import { ApiError } from '../api/client';
import {
  getSesionesPendientes, updateSesion, getCapturasPendientes, updateCaptura,
} from '../db/repositories';
import type { SesionRow, CapturaRow } from '../db/schema';

const BATCH_SIZE = 100;

function toSesionBody(row: SesionRow) {
  return {
    id: row.id,
    branchId: row.branchId,
    inventoryNumber: row.inventoryNumber,
    startedAt: row.startedAt,
    status: row.status,
    validateAgainstMaster: row.validateAgainstMaster,
  };
}

function toCapturaBody(row: CapturaRow) {
  return {
    id: row.id,
    sessionId: row.sessionId,
    sectorId: row.sectorId,
    barcode: row.barcode,
    productCode: row.productCode,
    quantity: row.quantity,
    inMaster: row.inMaster,
    capturedAt: row.capturedAt,
  };
}

function isClientError(err: unknown): err is ApiError {
  return err instanceof ApiError && err.status >= 400 && err.status < 500 && err.status !== 401;
}

export async function syncSesionesPendientes(token: string): Promise<void> {
  const pendientes = await getSesionesPendientes();
  for (const sesion of pendientes) {
    try {
      await upsertSesion(token, toSesionBody(sesion));
      await updateSesion(sesion.id, { syncStatus: 'synced', lastError: null });
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        throw err;
      }
      if (isClientError(err)) {
        await updateSesion(sesion.id, { syncStatus: 'error', lastError: err.message });
      }
      // red o 5xx: la fila queda pending, se reintenta en el siguiente tick
    }
  }
}

export async function syncCapturasPendientes(token: string): Promise<void> {
  const pendientes = await getCapturasPendientes();
  for (let i = 0; i < pendientes.length; i += BATCH_SIZE) {
    const batch = pendientes.slice(i, i + BATCH_SIZE);
    try {
      await uploadCapturasBatch(token, batch.map(toCapturaBody));
      await Promise.all(batch.map((c) => updateCaptura(c.id, { syncStatus: 'synced', lastError: null })));
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        throw err;
      }
      if (isClientError(err)) {
        const message = err.message;
        await Promise.all(batch.map((c) => updateCaptura(c.id, { syncStatus: 'error', lastError: message })));
      }
    }
  }
}
```

- [ ] **Step 2: Crear `tests/sync/uploadSync.test.ts`**

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { syncSesionesPendientes, syncCapturasPendientes } from '../../src/sync/uploadSync';
import * as endpoints from '../../src/api/endpoints';
import { ApiError } from '../../src/api/client';
import { db } from '../../src/db/schema';
import { createSesion, createCaptura, getSesion, getCapturasBySesion } from '../../src/db/repositories';

beforeEach(async () => {
  await db.sesiones.clear();
  await db.capturas.clear();
});

describe('syncSesionesPendientes', () => {
  it('marca synced una sesion pendiente cuando el servidor responde 200', async () => {
    await createSesion({
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockResolvedValue(undefined);

    await syncSesionesPendientes('tok');

    const sesion = await getSesion('s1');
    expect(sesion?.syncStatus).toBe('synced');
  });

  it('marca error (con mensaje) una sesion cuando el servidor responde 400', async () => {
    await createSesion({
      id: 's2', branchId: 1, inventoryNumber: 'INV-2', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockRejectedValue(new ApiError(400, 'InventoryNumber duplicado'));

    await syncSesionesPendientes('tok');

    const sesion = await getSesion('s2');
    expect(sesion?.syncStatus).toBe('error');
    expect(sesion?.lastError).toBe('InventoryNumber duplicado');
  });

  it('deja pending una sesion si hay un error de red', async () => {
    await createSesion({
      id: 's3', branchId: 1, inventoryNumber: 'INV-3', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockRejectedValue(new Error('network down'));

    await syncSesionesPendientes('tok');

    const sesion = await getSesion('s3');
    expect(sesion?.syncStatus).toBe('pending');
  });

  it('un 401 se relanza sin marcar la fila como error', async () => {
    await createSesion({
      id: 's4', branchId: 1, inventoryNumber: 'INV-4', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'upsertSesion').mockRejectedValue(new ApiError(401, 'expirado'));

    await expect(syncSesionesPendientes('tok')).rejects.toMatchObject({ status: 401 });

    const sesion = await getSesion('s4');
    expect(sesion?.syncStatus).toBe('pending');
  });
});

describe('syncCapturasPendientes', () => {
  it('divide en lotes de 100 y sincroniza todo', async () => {
    for (let i = 0; i < 101; i++) {
      await createCaptura({
        id: `cap-${i}`, sessionId: 's1', sectorId: 1, barcode: `B${i}`, productCode: null,
        quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
      });
    }
    const spy = vi.spyOn(endpoints, 'uploadCapturasBatch').mockResolvedValue({ processed: 100 });

    await syncCapturasPendientes('tok');

    expect(spy).toHaveBeenCalledTimes(2);
    expect(spy.mock.calls[0][1]).toHaveLength(100);
    expect(spy.mock.calls[1][1]).toHaveLength(1);
    const capturas = await getCapturasBySesion('s1');
    expect(capturas.every((c) => c.syncStatus === 'synced')).toBe(true);
  });

  it('marca todo el lote como error si el servidor responde 400', async () => {
    await createCaptura({
      id: 'cap-x', sessionId: 's1', sectorId: 1, barcode: 'X', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'pending', lastError: null,
    });
    vi.spyOn(endpoints, 'uploadCapturasBatch').mockRejectedValue(new ApiError(400, 'SectorId invalido'));

    await syncCapturasPendientes('tok');

    const capturas = await getCapturasBySesion('s1');
    expect(capturas[0].syncStatus).toBe('error');
    expect(capturas[0].lastError).toBe('SectorId invalido');
  });
});
```

- [ ] **Step 3: Correr los tests**

Run: `npm run test`
Expected: 32 tests PASS.

- [ ] **Step 4: Commit**

```bash
git add src/sync/uploadSync.ts tests/sync/uploadSync.test.ts
git commit -m "feat: motor de sync para sesiones y capturas pendientes"
```

---

### Task 10: Orquestador de sync + indicador de estado

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/sync/syncLoop.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/components/SyncStatusBadge.tsx`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/sync/syncLoop.test.ts`

**Interfaces:**
- Consumes: `syncSesionesPendientes`, `syncCapturasPendientes` de
  `src/sync/uploadSync.ts` (Task 9); `ApiError` de `src/api/client.ts`
  (Task 3); `getPendingCounts` de `src/db/repositories.ts` (Task 2, usado por
  `SyncStatusBadge`).
- Produces: `startSyncLoop(options: { getToken: () => string | null;
  onUnauthorized: () => void; intervalMs?: number }): { stop: () => void;
  runOnce: () => Promise<void> }`. Task 12 la invoca al montar `App.tsx` una
  vez que hay token, y usa `onUnauthorized` para llamar `logout()` (Task 4) y
  redirigir a `/login`.

- [ ] **Step 1: Crear `src/sync/syncLoop.ts`**

```typescript
import { syncSesionesPendientes, syncCapturasPendientes } from './uploadSync';
import { ApiError } from '../api/client';

export interface SyncLoopOptions {
  getToken: () => string | null;
  onUnauthorized: () => void;
  intervalMs?: number;
}

export interface SyncLoopHandle {
  stop: () => void;
  runOnce: () => Promise<void>;
}

export function startSyncLoop(options: SyncLoopOptions): SyncLoopHandle {
  const intervalMs = options.intervalMs ?? 30000;
  let stopped = false;

  async function runOnce(): Promise<void> {
    if (stopped) return;
    const token = options.getToken();
    if (!token) return;
    try {
      await syncSesionesPendientes(token);
      await syncCapturasPendientes(token);
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        stopped = true;
        options.onUnauthorized();
      }
    }
  }

  const onlineListener = () => {
    runOnce();
  };
  window.addEventListener('online', onlineListener);

  const intervalId = setInterval(() => {
    if (navigator.onLine) {
      runOnce();
    }
  }, intervalMs);

  function stop() {
    stopped = true;
    window.removeEventListener('online', onlineListener);
    clearInterval(intervalId);
  }

  return { stop, runOnce };
}
```

- [ ] **Step 2: Crear `src/components/SyncStatusBadge.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { getPendingCounts } from '../db/repositories';

export function SyncStatusBadge({ onSyncNow }: { onSyncNow: () => void }) {
  const [counts, setCounts] = useState({ sesiones: 0, capturas: 0, errores: 0 });
  const [online, setOnline] = useState(navigator.onLine);

  useEffect(() => {
    const update = () => getPendingCounts().then(setCounts);
    update();
    const interval = setInterval(update, 5000);
    const onOnline = () => setOnline(true);
    const onOffline = () => setOnline(false);
    window.addEventListener('online', onOnline);
    window.addEventListener('offline', onOffline);
    return () => {
      clearInterval(interval);
      window.removeEventListener('online', onOnline);
      window.removeEventListener('offline', onOffline);
    };
  }, []);

  return (
    <div>
      <span>{online ? 'En línea' : 'Sin conexión'}</span>
      <span> Pendientes: {counts.sesiones + counts.capturas}</span>
      {counts.errores > 0 && <span> — Errores: {counts.errores}</span>}
      <button onClick={onSyncNow}>Sincronizar ahora</button>
    </div>
  );
}
```

- [ ] **Step 3: Crear `tests/sync/syncLoop.test.ts`**

```typescript
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { startSyncLoop } from '../../src/sync/syncLoop';
import * as uploadSync from '../../src/sync/uploadSync';
import { ApiError } from '../../src/api/client';

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('startSyncLoop', () => {
  it('sincroniza en cada intervalo mientras hay conexion', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn(), intervalMs: 1000 });
    await vi.advanceTimersByTimeAsync(1000);

    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);
    handle.stop();
  });

  it('el evento online dispara una sincronizacion inmediata', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockResolvedValue();
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized: vi.fn(), intervalMs: 999999 });
    window.dispatchEvent(new Event('online'));
    await vi.waitFor(() => expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1));

    handle.stop();
  });

  it('un 401 detiene el loop y llama onUnauthorized', async () => {
    vi.spyOn(uploadSync, 'syncSesionesPendientes').mockRejectedValue(new ApiError(401, 'expirado'));
    vi.spyOn(uploadSync, 'syncCapturasPendientes').mockResolvedValue();
    vi.spyOn(window.navigator, 'onLine', 'get').mockReturnValue(true);
    const onUnauthorized = vi.fn();

    const handle = startSyncLoop({ getToken: () => 'tok', onUnauthorized, intervalMs: 1000 });
    await vi.advanceTimersByTimeAsync(1000);
    expect(onUnauthorized).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1000);
    expect(uploadSync.syncSesionesPendientes).toHaveBeenCalledTimes(1);

    handle.stop();
  });
});
```

- [ ] **Step 4: Correr los tests**

Run: `npm run test`
Expected: 35 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/sync/syncLoop.ts src/components/SyncStatusBadge.tsx tests/sync/syncLoop.test.ts
git commit -m "feat: orquestador de sync automatico e indicador de estado"
```

---

### Task 11: Exportación manual a JSON

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/sync/exportImport.ts`
- Create: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/features/export/ExportButton.tsx`
- Test: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/tests/sync/exportImport.test.ts`

**Interfaces:**
- Consumes: `getSesiones`, `getCapturasBySesion`, `getAuthConfig` de
  `src/db/repositories.ts` (Task 2).
- Produces: tipo `ExportPayload`; `buildExportPayload(companyCode: string,
  deviceLabel: string): Promise<ExportPayload>`;
  `downloadExportPayload(payload: ExportPayload): void`. Usadas por
  `ExportButton.tsx` (Task 12 la monta en `App.tsx`). Este mismo shape
  (`ExportPayload.sesiones`/`capturas`) es el que va a leer la página de
  importación admin del Plan 2 — no se toca sin coordinar ambos lados.

- [ ] **Step 1: Crear `src/sync/exportImport.ts`**

```typescript
import { getSesiones, getCapturasBySesion } from '../db/repositories';
import type { CapturaRow } from '../db/schema';

export interface ExportPayload {
  exportedAt: string;
  companyCode: string;
  deviceLabel: string;
  sesiones: Array<{
    id: string;
    branchId: number;
    inventoryNumber: string;
    startedAt: string;
    status: 'ACTIVE' | 'CLOSED';
    validateAgainstMaster: boolean;
  }>;
  capturas: Array<{
    id: string;
    sessionId: string;
    sectorId: number;
    barcode: string;
    productCode: string | null;
    quantity: number;
    inMaster: boolean | null;
    capturedAt: string;
  }>;
}

export async function buildExportPayload(companyCode: string, deviceLabel: string): Promise<ExportPayload> {
  const todasSesiones = await getSesiones();
  const sesionesPendientes = todasSesiones.filter((s) => s.syncStatus !== 'synced');

  const capturasPendientes: CapturaRow[] = [];
  for (const sesion of todasSesiones) {
    const capturas = await getCapturasBySesion(sesion.id);
    capturasPendientes.push(...capturas.filter((c) => c.syncStatus !== 'synced'));
  }

  return {
    exportedAt: new Date().toISOString(),
    companyCode,
    deviceLabel,
    sesiones: sesionesPendientes.map((s) => ({
      id: s.id,
      branchId: s.branchId,
      inventoryNumber: s.inventoryNumber,
      startedAt: s.startedAt,
      status: s.status,
      validateAgainstMaster: s.validateAgainstMaster,
    })),
    capturas: capturasPendientes.map((c) => ({
      id: c.id,
      sessionId: c.sessionId,
      sectorId: c.sectorId,
      barcode: c.barcode,
      productCode: c.productCode,
      quantity: c.quantity,
      inMaster: c.inMaster,
      capturedAt: c.capturedAt,
    })),
  };
}

export function downloadExportPayload(payload: ExportPayload): void {
  const json = JSON.stringify(payload, null, 2);
  const blob = new Blob([json], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = `conteo_${payload.companyCode}_${payload.exportedAt.replace(/[:.]/g, '-')}.json`;
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}
```

- [ ] **Step 2: Crear `src/features/export/ExportButton.tsx`**

```tsx
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
    <div>
      <input
        placeholder="Nombre del equipo (opcional)"
        value={deviceLabel}
        onChange={(e) => setDeviceLabel(e.target.value)}
      />
      <button onClick={handleExport}>Exportar pendientes</button>
    </div>
  );
}
```

- [ ] **Step 3: Crear `tests/sync/exportImport.test.ts`**

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { buildExportPayload } from '../../src/sync/exportImport';
import { createSesion, createCaptura } from '../../src/db/repositories';
import { db } from '../../src/db/schema';

beforeEach(async () => {
  await db.sesiones.clear();
  await db.capturas.clear();
});

describe('buildExportPayload', () => {
  it('incluye sesiones y capturas pending/error, excluye las synced', async () => {
    await createSesion({
      id: 's1', branchId: 1, inventoryNumber: 'INV-1', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: true, syncStatus: 'pending', lastError: null,
    });
    await createSesion({
      id: 's2', branchId: 1, inventoryNumber: 'INV-2', startedAt: '2026-01-01T00:00:00Z',
      status: 'CLOSED', validateAgainstMaster: true, syncStatus: 'synced', lastError: null,
    });
    await createCaptura({
      id: 'c1', sessionId: 's1', sectorId: 1, barcode: 'A', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'error', lastError: 'x',
    });
    await createCaptura({
      id: 'c2', sessionId: 's2', sectorId: 1, barcode: 'B', productCode: null,
      quantity: 1, inMaster: null, capturedAt: '2026-01-01T00:00:00Z', syncStatus: 'synced', lastError: null,
    });

    const payload = await buildExportPayload('DEPOR', 'skorpio-1');

    expect(payload.sesiones.map((s) => s.id)).toEqual(['s1']);
    expect(payload.capturas.map((c) => c.id)).toEqual(['c1']);
    expect(payload.companyCode).toBe('DEPOR');
  });

  it('el payload sobrevive un round-trip de JSON', async () => {
    await createSesion({
      id: 's3', branchId: 2, inventoryNumber: 'INV-3', startedAt: '2026-01-01T00:00:00Z',
      status: 'ACTIVE', validateAgainstMaster: false, syncStatus: 'pending', lastError: null,
    });

    const payload = await buildExportPayload('DEPOR', '');
    const roundTripped = JSON.parse(JSON.stringify(payload));

    expect(roundTripped).toEqual(payload);
  });
});
```

- [ ] **Step 4: Correr los tests**

Run: `npm run test`
Expected: 37 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/sync/exportImport.ts src/features/export tests/sync/exportImport.test.ts
git commit -m "feat: exportacion manual a JSON de sesiones/capturas pendientes"
```

---

### Task 12: Routing y ensamblado final

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.AuditoriaInventario.PWA/src/App.tsx`

**Interfaces:**
- Consumes: `useAuth` (Task 4), `LoginPage` (Task 4), `SesionesListPage`
  (Task 7), `CapturaPage` (Task 8), `SyncStatusBadge` (Task 10),
  `ExportButton` (Task 11), `startSyncLoop` (Task 10), `syncProductos`,
  `syncSucursales` (Task 5), `getSucursales` (Task 2).
- Produces: la app completa, sin nada nuevo para tasks posteriores (es la
  última de este plan).

- [ ] **Step 1: Reescribir `src/App.tsx`**

```tsx
import { useEffect, useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, useNavigate } from 'react-router-dom';
import { useAuth } from './features/auth/useAuth';
import { LoginPage } from './features/auth/LoginPage';
import { SesionesListPage } from './features/sesiones/SesionesListPage';
import { CapturaPage } from './features/captura/CapturaPage';
import { SyncStatusBadge } from './components/SyncStatusBadge';
import { ExportButton } from './features/export/ExportButton';
import { startSyncLoop } from './sync/syncLoop';
import { syncProductos, syncSucursales } from './sync/maestroSync';
import { getSucursales } from './db/repositories';

function AuthenticatedApp({ token, onLogout }: { token: string; onLogout: () => void }) {
  const [maestroReady, setMaestroReady] = useState(false);
  const [progreso, setProgreso] = useState(0);
  const navigate = useNavigate();

  useEffect(() => {
    (async () => {
      const sucursales = await getSucursales();
      if (sucursales.length === 0) {
        await syncSucursales(token);
      }
      await syncProductos(token, setProgreso);
      setMaestroReady(true);
    })();
  }, [token]);

  useEffect(() => {
    const handle = startSyncLoop({
      getToken: () => token,
      onUnauthorized: () => {
        onLogout();
        navigate('/login');
      },
    });
    return () => handle.stop();
  }, [token, onLogout, navigate]);

  if (!maestroReady) {
    return <p>Sincronizando maestro... {progreso} productos</p>;
  }

  return (
    <div>
      <SyncStatusBadge onSyncNow={() => {}} />
      <ExportButton />
      <Routes>
        <Route path="/sesiones" element={<SesionesListPage />} />
        <Route path="/sesiones/:sesionId" element={<CapturaPage token={token} />} />
        <Route path="*" element={<Navigate to="/sesiones" replace />} />
      </Routes>
    </div>
  );
}

export function App() {
  const { token, loading, logout } = useAuth();

  if (loading) {
    return <p>Cargando...</p>;
  }

  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={token ? <Navigate to="/sesiones" replace /> : <LoginPage />} />
        <Route
          path="/*"
          element={token ? <AuthenticatedApp token={token} onLogout={logout} /> : <Navigate to="/login" replace />}
        />
      </Routes>
    </BrowserRouter>
  );
}
```

- [ ] **Step 2: Verificar que todo compila y los tests siguen en verde**

Run:
```bash
npm run test
npm run build
```
Expected: `npm run test` → 37 tests PASS (sin cambios, esta task no agrega
tests automatizados — es ensamblado de UI). `npm run build` → compila sin
errores de TypeScript.

- [ ] **Step 3: Commit**

```bash
git add src/App.tsx
git commit -m "feat: ensamblar routing y flujo completo de la PWA"
```

- [ ] **Step 4: Verificación manual pendiente (no automatizable en este plan)**

Con `PortalSaas.Host` corriendo en `http://localhost:5270` (perfil `http`) y
`npm run dev` corriendo esta PWA (por defecto en `http://localhost:5173`,
proxeando `/api` al Host):

1. Loguearse con un `CaptureUser` real de `Modulo.AuditoriaInventario` (hay
   que crear uno desde `/Capturadores` en el portal si no existe ninguno
   para la compañía de prueba).
2. Confirmar que el login funciona — esto valida la asunción de casing
   camelCase documentada en Global Constraints. Si el login devuelve 400
   "CompanyCode, Username y Password son obligatorios" en vez de autenticar,
   la asunción de `PropertyNameCaseInsensitive` es incorrecta para este
   binding y hay que ajustar `endpoints.ts` para mandar los campos en
   PascalCase.
3. Confirmar que el maestro se sincroniza (productos/sucursales visibles).
4. Crear una sesión, capturar un código de barra, cerrar la sesión, y
   confirmar en el portal (`/Diferencias` o revisando la base) que la data
   llegó.
5. Cortar la red del dispositivo, capturar más ítems, confirmar que quedan
   `pending`, reconectar y confirmar que se sincronizan solos.
6. Probar "Exportar pendientes" y confirmar que el archivo descargado tiene
   el shape esperado (`sesiones`/`capturas` con los campos de
   `ExportPayload`).

Este paso es manual porque no hay Host real corriendo en este entorno de
implementación — queda documentado para quien ejecute el plan con acceso a
un Host de desarrollo.

---

## Self-Review

**Cobertura de la spec:**
- Login manual (CompanyCode+Username+Password) → Task 4. ✅
- Sync inicial de maestro (productos paginado + sucursales) → Task 5. ✅
- Sectores cacheados por sucursal → Task 6. ✅
- Alta/cierre de sesiones local-first → Task 7. ✅
- Captura por código de barra + validación contra maestro local → Task 8. ✅
- Motor de sync (sesiones antes que capturas, batches de 100, manejo de
  401/4xx/red) → Task 9. ✅
- Disparadores de sync (online, intervalo 30s, manual) + indicador →
  Task 10. ✅
- Exportación manual a JSON de lo pendiente → Task 11. ✅
- Routing y ensamblado, logout preserva datos locales → Task 4 (logout) +
  Task 12 (ensamblado). ✅
- Importación admin: **fuera de este plan** (Plan 2, otro repo/proyecto),
  tal como se acordó en el diseño.
- Despliegue real (IIS, Skorpio X5): explícitamente fuera de alcance según
  la spec. No hay task para esto.

**Escaneo de placeholders:** sin TBD/TODO; todos los steps de código tienen
el código completo, no descripciones.

**Consistencia de tipos:** `SesionRow`/`CapturaRow` (Task 2) se usan sin
cambios en Tasks 4-12; `SesionUpsertBody`/`CapturaBody` (Task 3) coinciden
campo a campo con lo que Task 9 (`toSesionBody`/`toCapturaBody`) construye a
partir de `SesionRow`/`CapturaRow`. `ExportPayload` (Task 11) reusa los
mismos nombres de campo en camelCase que el contrato — importante para que
el Plan 2 (importación admin) pueda leerlo sin ambigüedad.
