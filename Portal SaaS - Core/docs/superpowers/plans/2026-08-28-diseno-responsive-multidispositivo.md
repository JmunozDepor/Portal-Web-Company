# Diseño responsive multidispositivo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que login, Home, el shell (`_LayoutMaestro`) y los 3 motores genéricos de documento se vean y funcionen bien en teléfono, tablet y escritorio.

**Architecture:** Cambios concentrados en los 4 CSS compartidos (`design-tokens.css`, `components.css`, `sidebar.css`, `site.css`) + markup mínimo en `_LayoutMaestro.cshtml`, `Home/Index.cshtml` y los 3 `_TabContent*.cshtml`, + un bloque nuevo en `sidebar.js` para el drawer off-canvas. Sin C# nuevo. El shell pasa a drawer off-canvas (sidebar `position: fixed` + scrim) en ≤900px; el resto son media queries.

**Tech Stack:** ASP.NET Core Razor Pages, Bootstrap 5.1 (vendored), CSS custom properties (`design-tokens.css`), vanilla JS.

## Global Constraints

- **CERO CSS por módulo.** Todo va en `design-tokens.css` / `components.css` / `sidebar.css` / `site.css`. Nunca un archivo CSS nuevo ni clases con prefijo local.
- **CERO estilos inline.** Prohibido `style="..."` en `.cshtml`/`.razor`. Única excepción tolerada en este plan: `style="--icon-color:var(--menu-color-N)"` de `Home/Index.cshtml` (valor dinámico por fila, sin alternativa) — se deja como está.
- **Única fuente de verdad: `design-tokens.css`.** Todo color/espaciado/sombra/tipografía vía `var(--token)`. Cero hex nuevos.
- **Transiciones:** solo `var(--transition-fast)` (150ms) y `var(--drawer-transition)` (300ms).
- **Componentes cerrados:** no crear equivalentes con otro nombre de `.btn-primary`/`.card-ps`/`.table-ps`/`.badge-status`.
- **Breakpoints (contrato):** `sm` = `max-width: 576px`; `md` = `max-width: 900px` (el shell pasa a off-canvas acá); `lg` = `min-width: 901px` (escritorio, sin cambios).
- **Build:** `dotnet build PortalSaas.sln` debe quedar 0 advertencias / 0 errores tras cada tarea.
- **Tests:** `dotnet test tests/PortalSaas.Core.Tests` = 200/200 (no hay C# nuevo; se corre una vez al final como regresión).
- **Verificación visual:** emulador de Chrome DevTools (`Ctrl+Shift+M`) a **360 / 414 / 768 / 1024 / 1440 px**. No hay herramienta de captura automatizada — la verificación visual es manual por quien ejecuta.
- **No tocar:** CSS de plugins (`Modulo.Rendiciones/wwwroot/css/rendiciones.css`), `_AdminLayout` (ya no existe), buscador global / notificaciones (siguen `disabled`).

**Spec:** `docs/superpowers/specs/2026-08-28-diseno-responsive-multidispositivo-design.md`

---

## Task 1: Contrato de breakpoints + unificar media queries dispersas

Establece los 3 cortes y alinea las media queries sueltas existentes al corte `md` (900px). Sin cambio visual en escritorio.

**Files:**
- Modify: `src/PortalSaas.Host/wwwroot/css/design-tokens.css` (agregar comentario-contrato al final del `:root` base, después de la línea `--drawer-transition: 300ms;` ~línea 155)
- Modify: `src/PortalSaas.Host/wwwroot/css/site.css:1527` (bloque `@media (max-width: 767.98px)`)
- Modify: `src/PortalSaas.Host/wwwroot/css/components.css:276` (bloque `@media (max-width: 480px)`)

**Interfaces:**
- Produces: cortes `576px` / `900px` como valores canónicos que las tareas 2–6 reutilizan literalmente.

- [ ] **Step 1: Agregar el comentario-contrato en `design-tokens.css`**

Después de `--drawer-transition: 300ms;` (dentro del `:root` base, ~línea 155), agregar:

```css

  /* ---------------------------------------------------------------------------
     BREAKPOINTS (contrato) -- CSS no admite var() en condiciones @media, así
     que estos valores se escriben literales en cada @media del proyecto, pero
     esta es la definición de referencia. NO agregar cortes nuevos sin
     actualizar acá.
       sm : max-width: 576px   -> teléfono vertical
       md : max-width: 900px   -> teléfono horizontal / tablet vertical.
                                  El shell (_LayoutMaestro/sidebar.css) pasa a
                                  drawer off-canvas en este corte.
       lg : min-width: 901px   -> escritorio. Layout base, sin overrides.
     --------------------------------------------------------------------------- */
```

- [ ] **Step 2: Alinear el breakpoint de `.form-row` en `site.css`**

En `site.css:1527`, cambiar la condición del `@media` de `767.98px` a `900px`. El bloque queda:

```css
@media (max-width: 900px) {
  .form-row,
  .doc-columnas-2 {
    grid-template-columns: 1fr;
  }

  .menu-perfil-fila {
    grid-template-columns: 1fr;
    row-gap: 4px;
  }
}
```

(Razón: en 768–900px el shell ya es off-canvas y el contenido tiene ancho completo, pero 2 columnas de campos ahí siguen apretadas.)

- [ ] **Step 3: Alinear el breakpoint del `.drawer-grid` en `components.css`**

En `components.css:276`, cambiar `@media (max-width: 480px)` a `@media (max-width: 576px)`:

```css
@media (max-width: 576px) {
  .drawer-grid {
    grid-template-columns: 1fr;
  }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 5: Verificación visual mínima (regresión)**

Con el Host corriendo, abrir cualquier formulario de documento (`/ventas/ordenes`, crear una orden) a **1440px**: la sección General sigue en 2 columnas. A **768px**: pasa a 1 columna (antes cambiaba a 767.98px, diferencia imperceptible).

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/design-tokens.css" "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/site.css" "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/components.css"
git commit -m "$(printf 'style(responsive): contrato de breakpoints sm/md/lg + unifica media queries dispersas\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Task 2: Arreglar el login en ≤900px

Causa raíz: el bloque `@media (max-width: 900px)` de login está **antes** de la regla base `.login-brand-panel { display: flex }`, así que nunca la pisa. Fix = mover ese `@media` al final del bloque `.login-*` y endurecerlo.

**Files:**
- Modify: `src/PortalSaas.Host/wwwroot/css/site.css` — quitar el `@media (max-width: 900px)` de las líneas ~1567–1575; agregar un bloque nuevo al FINAL del archivo (después de `.login-footer`, ~línea 1889); ajustar `.login-bg` base (~1555) y `.login-form-panel` base (~1804).

**Interfaces:**
- Consumes: corte `md` = 900px, corte `sm` = 576px (Task 1).
- Produces: nada que otras tareas consuman (pantalla aislada, `Layout = null`).

- [ ] **Step 1: Quitar `overflow: hidden` de `.login-bg` y pasar a `min-height`**

En `.login-bg` (~línea 1555), cambiar:

```css
.login-bg {
  min-height: 100vh;
  min-height: 100dvh;
  display: grid;
  grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr);
  font-family: var(--font-sans, inherit);
  color: var(--text-main);
}
```

Es decir: eliminar las declaraciones `height: 100vh;` / `height: 100dvh;` / `overflow: hidden;`, dejar solo `min-height: 100vh; min-height: 100dvh;`. (En teléfono horizontal la tarjeta ya no se corta; `.login-form-panel` scrollea.)

- [ ] **Step 2: Asegurar scroll del panel de formulario**

En `.login-form-panel` (~línea 1804), confirmar/ajustar a:

```css
.login-form-panel {
  background: var(--bs-body-bg);
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 24px;
  min-height: 100dvh;
  overflow-y: auto;
}
```

(Cambio: `height: 100%` → `min-height: 100dvh`, para que centre cuando sobra alto y crezca/scrollee cuando falta.)

- [ ] **Step 3: Borrar el `@media (max-width: 900px)` mal ubicado**

Eliminar por completo el bloque de las líneas ~1567–1575:

```css
@media (max-width: 900px) {
  .login-bg {
    grid-template-columns: 1fr;
  }

  .login-brand-panel {
    display: none;
  }
}
```

- [ ] **Step 4: Agregar el bloque responsive de login al FINAL de `site.css`**

Al final del archivo (después de la regla `.login-footer`, ~línea 1889), agregar:

```css

/* ==========================================================================
   Login responsive -- AL FINAL del bloque .login-* a propósito: una @media
   que matchea no suma especificidad, así que para pisar `.login-brand-panel
   { display: flex }` (regla base más abajo en el archivo) este bloque tiene
   que venir DESPUÉS de ella. Ver spec 2026-08-28.
   ========================================================================== */
@media (max-width: 900px) {
  .login-bg {
    grid-template-columns: 1fr;
  }

  /* Fallback en flujo -- si por lo que sea el panel se llega a mostrar en un
     viewport corto, su contenido no debe encimarse (las capas decorativas son
     position:absolute; los hijos reales quedan en flujo normal). */
  .login-brand-panel {
    display: none;
  }
}

@media (max-width: 576px) {
  .login-card {
    padding: 24px 20px;
  }

  .login-card .form-control {
    min-height: 44px;
  }

  .login-submit {
    padding: 14px 16px;
  }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 6: Verificación visual**

Host corriendo, `/Account/Login`, emulador:
- **360px** y **414px**: panel de marca oscuro NO visible; tarjeta de formulario ocupa el ancho con margen; sin scroll horizontal; inputs altos (~44px).
- **Landscape ~740×360**: la tarjeta scrollea verticalmente dentro del panel, no se corta el botón "Iniciar sesión".
- **768px**: igual que móvil (1 columna, sin panel de marca).
- **1024px** y **1440px**: split-screen con panel de marca a la izquierda, sin regresión.

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/site.css"
git commit -m "$(printf 'fix(login): la @media de <=900px iba antes de la regla base -> nunca ocultaba el panel de marca\n\nMueve el bloque responsive al final de .login-*, quita overflow:hidden del\nsplit-screen (cortaba la tarjeta en landscape) y agrega inputs de 44px en <=576px.\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Task 3: Shell — drawer off-canvas en ≤900px

`.sidebar` pasa a `position: fixed` fuera de pantalla y entra sobre un scrim al tocar un botón hamburguesa nuevo en la topbar. Sirve a los dos shells (tenant y `/Admin/*`).

**Files:**
- Modify: `src/PortalSaas.Host/wwwroot/css/sidebar.css` (agregar un bloque `@media (max-width: 900px)` al final del archivo, ~después de línea 653)
- Modify: `src/PortalSaas.Host/Pages/Shared/_LayoutMaestro.cshtml` (botón toggle + scrim en ambos shells; mover carga de `sidebar.js` a `isAuthenticated`)
- Modify: `src/PortalSaas.Host/wwwroot/js/sidebar.js` (bloque nuevo del drawer)

**Interfaces:**
- Consumes: corte `md` = 900px (Task 1); tokens `--sidebar-width` (250px), `--drawer-transition` (300ms), `--transition-fast` (150ms), `--card-bg`, `--card-border`.
- Produces: atributo `data-shell-nav="open"` en `<html>` (lo pone/quita `sidebar.js`); clases `.topbar-nav-toggle`, `.shell-scrim`; ids `#shell-nav-toggle`, `#shell-scrim`. Task 4 asume que el botón `.topbar-nav-toggle` es el primer hijo de `.topbar` en ≤900px.

- [ ] **Step 1: Markup — botón hamburguesa + scrim en el shell de PlatformAdmin**

En `_LayoutMaestro.cshtml`, rama `@if (isPlatformAdmin)`. Dentro de `<header class="topbar">` (~línea 203), como PRIMER hijo, antes de `<span class="topbar-title">`:

```html
                    <button type="button" class="topbar-nav-toggle" id="shell-nav-toggle" aria-label="Abrir menú" aria-expanded="false">
                        <i class="bi bi-list"></i>
                    </button>
```

Y justo después de `</aside>` (cierre del `<aside class="sidebar rail-sidebar">`, ~línea 201), antes de `<div class="shell-main">`:

```html
            <div class="shell-scrim" id="shell-scrim" hidden></div>
```

- [ ] **Step 2: Markup — mismo botón + scrim en el shell de tenant**

En `_LayoutMaestro.cshtml`, rama `else if (isAuthenticated)`. Dentro de `<header class="topbar">` (~línea 254), como PRIMER hijo, antes de `<span class="topbar-title">`:

```html
                    <button type="button" class="topbar-nav-toggle" id="shell-nav-toggle" aria-label="Abrir menú" aria-expanded="false">
                        <i class="bi bi-list"></i>
                    </button>
```

Y justo después de `</aside>` (cierre del `<aside class="sidebar rail-sidebar">`, ~línea 252), antes de `<div class="shell-main">`:

```html
            <div class="shell-scrim" id="shell-scrim" hidden></div>
```

- [ ] **Step 3: Markup — cargar `sidebar.js` también para PlatformAdmin**

En `_LayoutMaestro.cshtml`, hoy hay:

```html
    @if (isAuthenticated && !isPlatformAdmin)
    {
        <script src="~/js/sidebar.js" asp-append-version="true"></script>
        <script src="~/js/sidebar-resize.js" asp-append-version="true"></script>
        <script src="~/js/doc-list.js" asp-append-version="true"></script>
        <script src="~/js/doc-tabs.js" asp-append-version="true"></script>
        <script src="~/js/theme-switcher.js" asp-append-version="true"></script>
    }
```

Mover SOLO `sidebar.js` a la sección `@if (isAuthenticated)` que ya carga `drawer.js` (~línea 335):

```html
    @if (isAuthenticated)
    {
        <script src="~/js/drawer.js" asp-append-version="true"></script>
        <script src="~/js/sidebar.js" asp-append-version="true"></script>
    }
    @if (isAuthenticated && !isPlatformAdmin)
    {
        <script src="~/js/sidebar-resize.js" asp-append-version="true"></script>
        <script src="~/js/doc-list.js" asp-append-version="true"></script>
        <script src="~/js/doc-tabs.js" asp-append-version="true"></script>
        <script src="~/js/theme-switcher.js" asp-append-version="true"></script>
    }
```

(`sidebar-resize.js` NO se mueve — el `/Admin/*` no tiene handle de resize.)

- [ ] **Step 4: JS — bloque del drawer en `sidebar.js`**

Al final de `sidebar.js`, agregar (IIFE independiente, no depende del resto del archivo):

```javascript

/* ---------------------------------------------------------------------------
   Drawer off-canvas del shell en <=900px (ver sidebar.css @media). En
   escritorio el CSS neutraliza el efecto -- este código solo togglea un
   atributo inerte ahí. El drawer SIEMPRE arranca cerrado (no se persiste).
   --------------------------------------------------------------------------- */
(function () {
  var root = document.documentElement;
  var toggle = document.getElementById('shell-nav-toggle');
  var scrim = document.getElementById('shell-scrim');
  var sidebar = document.querySelector('.app-shell .sidebar');
  if (!toggle || !scrim || !sidebar) return;

  function open() {
    root.setAttribute('data-shell-nav', 'open');
    scrim.hidden = false;
    toggle.setAttribute('aria-expanded', 'true');
  }

  function close() {
    root.removeAttribute('data-shell-nav');
    scrim.hidden = true;
    toggle.setAttribute('aria-expanded', 'false');
  }

  toggle.addEventListener('click', function () {
    if (root.getAttribute('data-shell-nav') === 'open') { close(); } else { open(); }
  });

  scrim.addEventListener('click', close);

  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape' && root.getAttribute('data-shell-nav') === 'open') { close(); }
  });

  // Navegar dentro del drawer lo cierra (el destino carga en la misma pestaña).
  sidebar.addEventListener('click', function (e) {
    if (e.target.closest('a[href]')) { close(); }
  });

  // Rotar/agrandar a escritorio limpia el estado (evita un drawer "abierto"
  // invisible que igual bloquea clicks vía el scrim).
  window.addEventListener('resize', function () {
    if (window.innerWidth > 900) { close(); }
  });
})();
```

- [ ] **Step 5: CSS — bloque del drawer al final de `sidebar.css`**

Al final de `sidebar.css` (después de la regla `.content > .subnav-bar`, ~línea 653), agregar:

```css

/* ===========================================================================
   Shell responsive -- drawer off-canvas en <=900px (corte `md`, ver
   design-tokens.css). En >900px NO hay ningún override: el shell es idéntico
   al actual (sidebar en flujo, rail 64/250px, resize handle, topbar completa).
   Estado abierto = html[data-shell-nav="open"] (lo pone sidebar.js).
   =========================================================================== */

/* Botón hamburguesa: oculto en escritorio, visible en <=900px. */
.topbar-nav-toggle {
  display: none;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  flex-shrink: 0;
  border: none;
  background: transparent;
  color: var(--text-main);
  border-radius: var(--bs-border-radius-sm);
  font-size: 20px;
  cursor: pointer;
}

.topbar-nav-toggle:hover {
  background: var(--bs-secondary-bg);
}

.shell-scrim {
  display: none;
}

@media (max-width: 900px) {
  .topbar-nav-toggle {
    display: flex;
  }

  .app-shell .sidebar {
    position: fixed;
    top: 0;
    left: 0;
    height: 100dvh;
    width: var(--sidebar-width);
    z-index: 1040;
    transform: translateX(-100%);
    transition: transform var(--drawer-transition) ease;
    box-shadow: none;
  }

  html[data-shell-nav="open"] .app-shell .sidebar {
    transform: translateX(0);
    box-shadow: 0 0 40px -8px rgba(0, 0, 0, 0.35);
  }

  /* En el drawer el sidebar SIEMPRE muestra labels (no modo rail 64px) -- se
     fuerza el ancho expandido sin importar data-sidebar. */
  html[data-sidebar="collapsed"] .app-shell .sidebar,
  html[data-sidebar="expanded"] .app-shell .sidebar {
    width: var(--sidebar-width);
  }
  html[data-sidebar="collapsed"] .app-shell .sidebar .nav-texto,
  html[data-sidebar="collapsed"] .app-shell .sidebar .sidebar-brand-text {
    display: inline;
  }
  html[data-sidebar="collapsed"] .app-shell .sidebar .sidebar-nav a,
  html[data-sidebar="collapsed"] .app-shell .sidebar .sidebar-nav-grupo {
    justify-content: flex-start;
    padding-left: 8px;
  }

  /* El toggle interno rail<->250 y el handle de resize no aplican en drawer. */
  .app-shell .sidebar-toggle,
  .app-shell .sidebar-resize-handle {
    display: none;
  }

  /* El contenido toma el ancho completo -- el sidebar fixed ya no ocupa flujo. */
  .shell-main {
    width: 100%;
  }

  .shell-scrim {
    display: block;
    position: fixed;
    inset: 0;
    z-index: 1035;
    background: rgba(0, 0, 0, 0.4);
    opacity: 0;
    pointer-events: none;
    transition: opacity var(--transition-fast) ease;
  }

  html[data-shell-nav="open"] .shell-scrim:not([hidden]) {
    opacity: 1;
    pointer-events: auto;
  }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 7: Verificación visual**

Host corriendo. Loguearse como tenant, ir a `/Home`:
- **1440px**: sin cambios — sidebar en flujo, sin hamburguesa visible, rail/expand normal.
- **900px y 360px**: hamburguesa visible arriba a la izquierda de la topbar; contenido a ancho completo; sin sidebar visible al cargar.
- Tocar la hamburguesa: el drawer entra deslizándose desde la izquierda (250px, con labels), scrim oscuro detrás.
- Tocar el scrim / `Escape` / un link del menú: el drawer se cierra.
- Rotar a >900px con el drawer abierto: se cierra solo, el scrim no queda bloqueando.
- Repetir logueado en `/Admin/Organizations` (verifica que `sidebar.js` carga en `/Admin/*` y el drawer funciona ahí también).

- [ ] **Step 8: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/sidebar.css" "Portal SaaS - Core/src/PortalSaas.Host/Pages/Shared/_LayoutMaestro.cshtml" "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/js/sidebar.js"
git commit -m "$(printf 'feat(responsive): shell con drawer off-canvas en <=900px (tenant y /Admin)\n\nsidebar.css no tenia ni una media query. Ahora en <=900px el sidebar es\nposition:fixed fuera de pantalla y entra sobre un scrim con un boton\nhamburguesa nuevo en la topbar. Se carga sidebar.js tambien para PlatformAdmin.\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Task 4: Shell — topbar, panel de usuario y padding de contenido responsive

Elimina el scroll horizontal y el panel de usuario cortado en teléfono. Todo en `sidebar.css`.

**Files:**
- Modify: `src/PortalSaas.Host/wwwroot/css/sidebar.css` (agregar reglas dentro del `@media (max-width: 900px)` creado en Task 3 y un `@media (max-width: 576px)` nuevo a continuación)

**Interfaces:**
- Consumes: el bloque `@media (max-width: 900px)` de Task 3; clases existentes `.topbar`, `.topbar-search`, `.topbar-icon-btn`, `.topbar-title`, `.user-menu`, `.user-name`, `.user-menu-panel`, `.content`.

- [ ] **Step 1: Reglas de topbar/content dentro del `@media (max-width: 900px)` existente**

Dentro del bloque `@media (max-width: 900px)` de `sidebar.css` (el creado en Task 3), agregar:

```css
  .topbar {
    padding: 0 12px;
    gap: 8px;
  }

  .topbar-title {
    font-size: 13px;
    min-width: 0;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* Placeholders `disabled` -- en escritorio dicen "próximamente", en móvil
     solo estorban. */
  .topbar-search,
  .topbar-icon-btn {
    display: none;
  }

  .user-menu .user-name {
    max-width: 120px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .content {
    padding: 14px;
  }
```

- [ ] **Step 2: `@media (max-width: 576px)` nuevo, justo después**

Inmediatamente después del bloque `@media (max-width: 900px)`, agregar:

```css

@media (max-width: 576px) {
  .content {
    padding: 12px;
  }

  .user-menu .user-name {
    display: none;
  }

  .user-menu-panel {
    right: 8px;
    width: auto;
    min-width: 200px;
    max-width: calc(100vw - 16px);
  }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 4: Verificación visual**

Host corriendo, `/Home`, emulador:
- **360px**: la topbar muestra solo hamburguesa + título (truncado con "…" si es largo) + avatar. Sin buscador, sin iconos de campana/chat/+. Sin scroll horizontal en la página.
- **360px**: abrir el panel de usuario (tap en el avatar) — el panel NO se sale por la derecha, queda dentro del viewport.
- **768px**: `.user-name` visible pero truncado a ~120px.
- **1440px**: topbar completa, sin regresión.

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/sidebar.css"
git commit -m "$(printf 'fix(responsive): topbar sin desborde y panel de usuario sin cortarse en telefono\n\nOculta buscador/iconos placeholder en <=900px, trunca el titulo y el nombre,\nreduce el padding de .content y ancla .user-menu-panel dentro del viewport en <=576px.\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Task 5: Home — quitar estilos inline y grilla de accesos responsive

**Files:**
- Modify: `src/PortalSaas.Host/Pages/Home/Index.cshtml` (quitar 3 `style=` estáticos + `style="display:none"` del picker; ajustar los 2 helpers JS)
- Modify: `src/PortalSaas.Host/wwwroot/css/site.css` (clases `.inicio-encabezado` / `.inicio-panel`; ajustar `.inicio-accesos` en `sm`)

**Interfaces:**
- Consumes: corte `sm` = 576px (Task 1).
- Produces: clases `.inicio-panel`, `.inicio-encabezado`.

- [ ] **Step 1: `Index.cshtml` — reemplazar el contenedor y el encabezado**

Cambiar las líneas 8–12:

```html
<div class="card-ps inicio-panel">
    <div class="inicio-encabezado">
        <h2>Hola, @Model.Email</h2>
        <p>Organización: <strong>@Model.OrganizationSlug</strong>. Elegí un módulo para empezar.</p>
    </div>
```

(Se elimina `style="padding: 20px;"`, `style="margin: 0 0 6px;"` y `style="color: var(--bs-secondary-color); margin: 0;"`. El `</div>` de cierre del `card-ps` al final del archivo, línea 62, no cambia — pero ahora hay un `<div class="inicio-encabezado">` extra que cerrar: agregar su `</div>` después del `<p>`, ya incluido arriba.)

- [ ] **Step 2: `Index.cshtml` — picker sin `style="display:none"`**

Línea 37, cambiar:

```html
    <div id="acceso-inicio-picker" class="inicio-picker" hidden>
```

- [ ] **Step 3: `Index.cshtml` — ajustar el helper `toggleAccesoInicioPicker`**

Reemplazar la función (líneas ~65–68) por:

```javascript
    function toggleAccesoInicioPicker() {
        var picker = document.getElementById('acceso-inicio-picker');
        picker.hidden = !picker.hidden;
    }
```

(`filtrarAccesoInicioPicker` NO cambia — opera sobre `.inicio-picker-item`, no sobre el contenedor.)

- [ ] **Step 4: `site.css` — clases nuevas + grilla `sm`**

En `site.css`, sección "Inicio" (~línea 1368, antes de `.inicio-accesos`), agregar:

```css
.inicio-panel {
  padding: 20px;
}

.inicio-encabezado h2 {
  margin: 0 0 6px;
}

.inicio-encabezado p {
  margin: 0;
  color: var(--bs-secondary-color);
}
```

Y en el `@media (max-width: 900px)` existente de `site.css` (el de `.form-row`, editado en Task 1) agregar dentro:

```css
  .inicio-panel {
    padding: 16px;
  }
```

Y un `@media (max-width: 576px)` nuevo a continuación de ese bloque:

```css
@media (max-width: 576px) {
  .inicio-accesos {
    grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 6: Verificación visual**

Host corriendo, `/Home`, emulador:
- **360px**: la grilla de accesos muestra 2 columnas parejas; el card no rebalsa; el botón "Agregar" abre/cierra el picker (ahora vía `hidden`).
- **1440px**: sin cambios visibles (padding 20px, mismo layout).
- Confirmar en DevTools que el `<div class="card-ps inicio-panel">` no tiene `style=` y el picker usa `hidden`.

- [ ] **Step 7: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Pages/Home/Index.cshtml" "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/site.css"
git commit -m "$(printf 'style(responsive): Home sin estilos inline + grilla de accesos 2 col en telefono\n\nMigra los style= estaticos a .inicio-panel/.inicio-encabezado, el picker usa\n[hidden] en vez de style.display, y .inicio-accesos baja a minmax(120px) en <=576px.\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Task 6: Motores genéricos — tabs, filtros y editor de líneas en teléfono

Todo en `site.css` + reemplazo del `<div style="overflow-x:auto;">` inline por una clase en los 3 `_TabContent*.cshtml`.

**Files:**
- Modify: `src/PortalSaas.Host/wwwroot/css/site.css` (reglas responsive de `.doc-tabs`, `.doc-form`, `.doc-form-sticky`, `.filter-card .col-auto`; clase nueva `.line-items-wrapper` + primera columna sticky)
- Modify: `plugins/Modulo.Ventas/Pages/Shared/_TabContentVentas.cshtml:11`
- Modify: `plugins/Modulo.Compras/Pages/Shared/_TabContentCompras.cshtml:11`
- Modify: `plugins/Modulo.Inventario/Pages/Shared/_TabContentInventario.cshtml:11`

**Interfaces:**
- Consumes: cortes `md` = 900px y `sm` = 576px (Task 1); clases existentes `.doc-tabs`, `.doc-form`, `.doc-form-sticky`, `.filter-card`, `.line-items-table`.
- Produces: clase `.line-items-wrapper`.

- [ ] **Step 1: Reemplazar el wrapper inline en los 3 `_TabContent*.cshtml`**

En cada uno de los 3 archivos, línea ~11, cambiar:

```html
<div style="overflow-x:auto;">
```

por:

```html
<div class="line-items-wrapper">
```

(El `</div>` de cierre correspondiente no cambia.)

- [ ] **Step 2: `site.css` — clase `.line-items-wrapper` + primera columna sticky**

En `site.css`, junto a `.line-items-table` (~línea 780), agregar:

```css
.line-items-wrapper {
  overflow-x: auto;
  -webkit-overflow-scrolling: touch;
}

/* Al scrollear horizontal en teléfono, la 1ª celda (código de artículo) queda
   fija como referencia de fila. */
.line-items-wrapper .line-items-table th:first-child,
.line-items-wrapper .line-items-table td:first-child {
  position: sticky;
  left: 0;
  background: var(--card-bg);
  z-index: 1;
}

/* Que los inputs de cada línea no se aplasten -- ancho mínimo por columna. */
.line-items-wrapper .line-items-table th,
.line-items-wrapper .line-items-table td {
  min-width: 120px;
}
.line-items-wrapper .line-items-table th:first-child,
.line-items-wrapper .line-items-table td:first-child {
  min-width: 140px;
}
```

- [ ] **Step 3: `site.css` — `.doc-tabs` en una fila deslizable + densidad `sm`**

En el `@media (max-width: 900px)` de `site.css` (el de `.form-row`), agregar dentro:

```css
  .doc-tabs {
    flex-wrap: nowrap;
    overflow-x: auto;
    -webkit-overflow-scrolling: touch;
  }
```

En el `@media (max-width: 576px)` de `site.css` (el creado en Task 5, o uno nuevo si no se hizo Task 5), agregar dentro:

```css
  .filter-card .col-auto {
    min-width: 100%;
  }

  .doc-form {
    padding: 8px 10px;
  }

  .doc-form-sticky {
    margin: -8px -10px 0;
    padding: 11px 10px 0;
  }
```

- [ ] **Step 4: Build**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 5: Verificación visual**

Host corriendo. `/ventas/ordenes` (listado) a **360px**:
- Cada filtro de `.filter-card` ocupa la fila completa; el botón "Filtrar" abajo.
- La tabla del listado scrollea horizontal dentro de su wrapper; el resto de la página no.

Crear/abrir una orden (detalle) a **360px**:
- `.doc-tabs` (General/Contenido/…) en UNA fila que se desliza horizontal, no 3 filas apiladas.
- Sección General en 1 columna.
- Editor de líneas: scrollea horizontal; la primera columna (Artículo) queda fija al desplazar; los inputs no están aplastados.
- Repetir en `/compras/...` y `/inventario/...` para confirmar paridad (mismo componente).

A **1440px**: sin regresión en ninguno de los 3.

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/wwwroot/css/site.css" "Portal SaaS - Core/plugins/Modulo.Ventas/Pages/Shared/_TabContentVentas.cshtml" "Portal SaaS - Core/plugins/Modulo.Compras/Pages/Shared/_TabContentCompras.cshtml" "Portal SaaS - Core/plugins/Modulo.Inventario/Pages/Shared/_TabContentInventario.cshtml"
git commit -m "$(printf 'feat(responsive): motores genericos usables en telefono (tabs, filtros, lineas)\n\n.doc-tabs pasa a una fila deslizable en <=900px, cada filtro ocupa la fila en\n<=576px, y el editor de lineas se envuelve en .line-items-wrapper (reemplaza el\nstyle= inline de los 3 _TabContent) con la 1a columna sticky al scrollear.\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Task 7: Regresión completa + barrido visual

Gate final: nada roto en escritorio, tests en verde, matriz visual completa.

**Files:** ninguno (solo verificación).

- [ ] **Step 1: Build limpio**

Run: `dotnet build "PortalSaas.sln" -clp:ErrorsOnly`
Expected: `0 Advertencia(s) / 0 Errores`

- [ ] **Step 2: Tests**

Run: `dotnet test "tests/PortalSaas.Core.Tests" --nologo`
Expected: `Superado: 200, Total: 200`

- [ ] **Step 3: Barrido visual — matriz pantalla × ancho**

Host corriendo. Para cada ancho **360 / 414 / 768 / 1024 / 1440 px** en el emulador de Chrome, revisar:

| Pantalla | Qué mirar |
|---|---|
| `/Account/Login` | ≤900: sin panel de marca, form centrado, sin scroll H, inputs 44px en ≤576; landscape ~740×360 scrollea sin cortar; ≥1024 split-screen intacto. |
| `/Home` | ≤900: hamburguesa, contenido full width, drawer abre/cierra por botón+scrim+Escape+link; grilla de accesos 2 col en 360; panel de usuario no se sale; ≥1024 sin cambios. |
| `/ventas/ordenes` (listado) | ≤576: filtros 1 por fila; tabla scrollea internamente; paginación visible. |
| Detalle de una orden de venta | ≤900: `.doc-tabs` en fila deslizable; `.form-row` 1 col; editor de líneas scroll H con 1ª col sticky. |
| `/compras` y `/inventario` (detalle) | Igual que venta (paridad del componente compartido). |
| `/Admin/Organizations` | Drawer funciona en `/Admin/*`; acciones de fila (iconos lápiz/pausa + dropdown `…`) no desbordan a 360px. |

- [ ] **Step 4: Regresión desktop explícita (1440px)**

- Shell: sidebar en flujo, rail 64px, expand a 250px con el toggle, handle de resize funciona (solo tenant), topbar completa con buscador `disabled` e iconos.
- `/Admin/Organizations`: sin hamburguesa visible, layout idéntico a antes de este plan.

- [ ] **Step 5: Commit del avance del plan (checkboxes) si aplica**

Si el flujo de ejecución trackea el plan en git:

```bash
git add "Portal SaaS - Core/docs/superpowers/plans/2026-08-28-diseno-responsive-multidispositivo.md"
git commit -m "$(printf 'docs: marca plan responsive multidispositivo como completado\n\nCo-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>')"
```

---

## Self-review (cobertura del spec)

- Spec §1 (breakpoints) → Task 1.
- Spec §2 (shell off-canvas: CSS + markup + JS + cargar sidebar.js en admin) → Task 3.
- Spec §3 (topbar / user-menu / content) → Task 4.
- Spec §4 (login: reorden, `overflow`, `min-height`, `sm` inputs) → Task 2.
- Spec §5 Home (inline styles, `.inicio-accesos` sm, picker `hidden`) → Task 5.
- Spec §5 motores genéricos (`.doc-tabs`, `.filter-card`, `.doc-form` padding, `.line-items-wrapper` + sticky) → Task 6.
- Spec §6 (archivos tocados) → cubierto entre Tasks 1–6; `components.css` se toca en Task 1 (`.drawer-grid`).
- Spec §Pruebas → Task 7.
- Spec §Fuera de alcance → respetado (no se toca `rendiciones.css`, ni "tarjeta por línea", ni buscador global).

Sin placeholders. Nombres consistentes entre tareas: `data-shell-nav="open"`, `.topbar-nav-toggle` / `#shell-nav-toggle`, `.shell-scrim` / `#shell-scrim`, `.line-items-wrapper`, `.inicio-panel` / `.inicio-encabezado` — definidos una vez y referenciados igual en todas.
