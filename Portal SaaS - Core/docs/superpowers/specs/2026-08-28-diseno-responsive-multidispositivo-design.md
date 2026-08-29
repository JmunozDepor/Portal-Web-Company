# Diseño responsive multidispositivo (teléfono / tablet / escritorio)

- **Fecha:** 2026-08-28
- **Estado:** aprobado (pendiente plan de implementación)
- **Alcance:** shell de la plataforma (`_LayoutMaestro`), login, Home, componentes
  compartidos de los 3 motores genéricos de documento (Venta/Compra/Inventario).
- **NO alcanza:** CSS de plugins (`rendiciones.css` ya tiene sus media queries),
  buscador global / notificaciones (siguen `disabled`), rediseño "tarjeta por línea"
  del editor de líneas.

## Problema

La app se rompe en viewports de teléfono/tablet:

1. **Shell sin adaptación móvil.** `wwwroot/css/sidebar.css` (que define `.app-shell`,
   `.sidebar`, `.topbar`, `.content`, `.shell-main`) no tiene ni una media query. En
   ~360–430px:
   - `.topbar` es un flex sin `wrap` con título + buscador (`flex:1; margin:0 24px`,
     aunque esté `disabled`) + 3 botones de icono + `.user-name` mostrando el correo
     completo → desborda a la derecha → scroll horizontal.
   - `.user-menu-panel` (`position:absolute; right:0; width:200px`) queda anclado a un
     `.user-menu` empujado fuera de pantalla → se ve cortado.
   - `--rail-width: 64px` siempre presente; expandido `--sidebar-width: 250px` deja
     ~110px de contenido. No hay off-canvas ni botón hamburguesa.
   - `.content { padding: 24px }` demasiado en teléfono.
   - Afordancias solo-puntero: `.sidebar-resize-handle` (drag), tooltips `:hover` del
     rail.

2. **Login roto por orden de reglas.** En `site.css`, el bloque
   `@media (max-width: 900px)` (línea ~1567) está **antes** de la regla base
   `.login-brand-panel { display: flex }` (línea ~1606). Una media query que matchea
   no suma especificidad → gana la regla posterior en el archivo → el panel de marca
   nunca se oculta. (`.login-bg { grid-template-columns: 1fr }` sí funciona porque su
   regla base está antes de la media query.) Resultado: en teléfono el panel oscuro se
   encima sobre el formulario. Confirmado que persiste tras hard-refresh (no es cache).
   Además: `.login-bg { height: 100dvh; overflow: hidden }` corta la tarjeta en
   teléfono horizontal (landscape).

3. **Motores genéricos.** Heredan fluidez parcial (`.filter-card` hace `flex-wrap`,
   `.form-row` colapsa a 1 columna en ≤768px, el listado scrollea dentro de
   `.document-list-table-wrapper`). Rompen en teléfono:
   - `.doc-tabs` hace wrap a 2–3 filas y come alto vertical.
   - `.line-items-table` (editor de líneas, varios inputs por fila) sin tratamiento
     móvil → inputs aplastados / scroll horizontal incómodo sin referencia de columna.
   - `.filter-card .col-auto { min-width: 160px }` deja 2 filtros por fila apretados en
     360px.

## Restricciones del proyecto (reglas duras de UI, ver `_LayoutMaestro.cshtml`)

- **CERO CSS por módulo.** Todo cambio va en los 4 archivos compartidos
  (`design-tokens.css`, `components.css`, `sidebar.css`, `site.css`).
- **CERO estilos inline.** `Home/Index.cshtml` hoy tiene `style="..."` — se migran a
  clases como parte de esta entrega (se está tocando el archivo).
- **Única fuente de verdad: `design-tokens.css`.** Colores/espaciados/tipografía vía
  `var(--token)`.
- **Componentes cerrados:** no crear equivalentes con otro nombre de
  `.btn-primary`/`.card-ps`/`.table-ps`/etc.

## Diseño

### 1. Sistema de breakpoints (`design-tokens.css`)

CSS no admite `var()` en condiciones `@media`, así que el "sistema" es un
contrato documentado + valores literales consistentes. Se agrega a `design-tokens.css`
un comentario de referencia y se migran las media queries sueltas existentes
(`900px` en login, `767.98px` en `.form-row`, `480px` en `.drawer-grid`, `768px` en
`html` font-size) a estos 3 cortes:

| Nombre | Condición | Uso |
|---|---|---|
| `sm` | `max-width: 576px` | Teléfono vertical |
| `md` | `max-width: 900px` | Teléfono horizontal / tablet vertical — **el shell pasa a off-canvas acá** |
| `lg` | `min-width: 901px` | Escritorio — layout actual sin cambios |

El breakpoint de `.form-row`/`.doc-columnas-2` (hoy `767.98px`) se sube a `900px` para
alinear con el corte del shell; a 768–900px el shell ya es off-canvas y el contenido
tiene ancho completo, así que 2 columnas de campos ahí siguen siendo apretadas.

### 2. App shell responsive — drawer off-canvas (`sidebar.css` + `_LayoutMaestro.cshtml` + `sidebar.js`)

**Patrón elegido: drawer off-canvas** (evaluadas y descartadas: rail-only 64px sin
drawer — sigue comiendo pantalla, navegación solo-iconos mala para descubrir;
bottom tab bar — el menú es dinámico/profundo por plugins, elegir 4 destinos es
arbitrario).

**CSS (`sidebar.css`, media query nueva `@media (max-width: 900px)`):**

- `.sidebar`: `position: fixed; top: 0; left: 0; height: 100dvh; z-index: 1040;
  transform: translateX(-100%); transition: transform var(--drawer-transition);`
  Ancho fijo del drawer = `--sidebar-width` (250px) — en móvil el drawer siempre
  muestra labels, no el rail.
- `html[data-shell-nav="open"] .sidebar`: `transform: translateX(0);`
- `.shell-scrim`: `position: fixed; inset: 0; z-index: 1035; background: rgba(0,0,0,.4);
  opacity: 0; pointer-events: none; transition: opacity var(--transition-fast);`
  visible + `pointer-events: auto` con `html[data-shell-nav="open"]`.
- `.shell-main` / `.content`: ancho completo (el `.sidebar` fixed ya no ocupa flujo).
- `.sidebar-resize-handle`: `display: none` en ≤900px (drag sin sentido en touch).
- `.topbar-nav-toggle`: botón hamburguesa, `display: none` en `lg`, `display: flex` en
  ≤900px. Icono `bi bi-list`.
- El toggle interno del rail (`#sidebar-toggle`, expandir/colapsar 64↔250) se oculta en
  ≤900px (`display: none`) — en móvil el drawer no tiene modo rail.

**Markup (`_LayoutMaestro.cshtml`), en AMBOS shells (`isPlatformAdmin` y `isAuthenticated`):**

- Dentro de `.topbar`, como primer hijo:
  `<button type="button" class="topbar-nav-toggle" id="shell-nav-toggle"
   aria-label="Abrir menú"><i class="bi bi-list"></i></button>`
- Después de `</aside>` (o al cierre de `.app-shell`, antes de `.shell-main`):
  `<div class="shell-scrim" id="shell-scrim" hidden></div>`
- **Cargar `sidebar.js` también para PlatformAdmin** — hoy solo se carga en la rama
  `isAuthenticated && !isPlatformAdmin`. Se mueve la condición a `isAuthenticated`
  para que el toggle del drawer funcione en `/Admin/*`. `sidebar-resize.js` sigue solo
  para tenant (no aplica a admin, que no tiene handle).

**JS (`sidebar.js`, bloque nuevo):**

- Al hacer click en `#shell-nav-toggle`: `document.documentElement.setAttribute(
  'data-shell-nav', 'open')`, quitar `hidden` del scrim.
- Cerrar (quitar el atributo, `hidden` al scrim) al: click en el scrim, tecla `Escape`,
  click en cualquier `.sidebar-nav a` (navegación), y `resize` a > 900px.
- No persiste en `localStorage` — el drawer siempre arranca cerrado en cada carga.
- Guardas: todo dentro de `if (matchMedia('(max-width: 900px)').matches)` no hace falta;
  el CSS ya neutraliza el efecto en desktop, el JS solo togglea un atributo inerte ahí.
  Igual se agrega el listener de `resize` para limpiar el estado si se rota/agranda.

### 3. Topbar responsive (`sidebar.css`)

En `@media (max-width: 900px)`:
- `.topbar-search { display: none }` (placeholder `disabled`, solo estorba en móvil).
- `.topbar-icon-btn { display: none }` (los 3 iconos placeholder `disabled`).
- `.topbar { padding: 0 12px; gap: 8px }`.
- `.topbar-title`: `font-size: 13px; white-space: nowrap; overflow: hidden;
  text-overflow: ellipsis; min-width: 0;` (que no empuje).

En `@media (max-width: 576px)`:
- `.user-menu .user-name { display: none }` (solo avatar).
- `.user-menu-panel { right: 8px; width: auto; min-width: 200px;
  max-width: calc(100vw - 16px) }`.

En el rango md (577–900px), fuera de `sm`:
- `.user-menu .user-name { max-width: 120px; overflow: hidden; text-overflow: ellipsis;
  white-space: nowrap }`.

`.content`:
- `@media (max-width: 900px)`: `padding: 14px`.
- `@media (max-width: 576px)`: `padding: 12px`.

### 4. Login (`site.css`, bloque `.login-*`)

- **Reordenar:** mover el bloque `@media (max-width: 900px) { .login-bg ... .login-brand-panel ... }`
  al FINAL del bloque `.login-*` (después de todas las reglas base `.login-brand-panel`,
  `.login-form-panel`, `.login-card`, etc.), para que `display: none` efectivamente gane.
- **`.login-bg`:** `height: 100dvh; overflow: hidden` → `min-height: 100dvh`
  (sin `overflow: hidden`). `.login-form-panel` ya tiene `overflow-y: auto` — la
  tarjeta scrollea en landscape en vez de cortarse.
- **Fallback en flujo:** `.login-brand-panel` — si por lo que sea se llega a mostrar en
  un viewport corto, su contenido (`.login-brand` / `.login-pitch-wrap` / `.login-footer`)
  no debe encimarse. Revisar que `.login-pitch-wrap { flex: 1; min-height: 0 }` +
  `overflow: hidden` del panel no produzcan solape; si hace falta, en ≤900px (antes de
  ocultar) degradar a `display: block` con los hijos en flujo normal. (En la práctica,
  con el fix de orden el panel queda `display: none` en ≤900px y esto es defensa en
  profundidad, no la ruta principal.)
- **`@media (max-width: 576px)`:** `.login-card { padding: 24px 20px }`;
  `.login-card .form-control { min-height: 44px }` (toque cómodo);
  `.login-submit { padding: 14px 16px }`.

### 5. Home + motores genéricos

**`Home/Index.cshtml`:**
- Quitar los 3 `style="..."` inline (`.card-ps` padding, `<h2>` margin, `<p>` color,
  `#acceso-inicio-picker` display). El `display:none` del picker se mantiene como
  atributo funcional pero via `hidden` + toggle de `hidden` en el JS existente (no
  `style.display`). El resto pasa a clases nuevas en `site.css`:
  - `.inicio-encabezado h2` / `.inicio-encabezado p` (reemplaza los `style` del header).
  - El `<div class="card-ps" style="padding:20px">` → `<div class="card-ps inicio-panel">`
    con `.inicio-panel { padding: 20px }` en `site.css` (≤576px: `16px`).
- Ajustar `toggleAccesoInicioPicker` / `filtrarAccesoInicioPicker` para usar
  `.hidden` (atributo) en vez de `style.display`.

**`site.css` (aplica a los 3 motores a la vez, son componentes compartidos):**
- `.inicio-accesos`: `minmax(140px, 1fr)` → en `@media (max-width: 576px)`
  `grid-template-columns: repeat(auto-fill, minmax(120px, 1fr))` (2 columnas parejas en
  360px).
- `.doc-tabs`: en `@media (max-width: 900px)` → `flex-wrap: nowrap; overflow-x: auto;
  -webkit-overflow-scrolling: touch` (una fila deslizable, no 3 apiladas).
- `.doc-form` / `.doc-form-sticky`: en `@media (max-width: 576px)` reducir padding y los
  márgenes negativos de `.doc-form-sticky` (`-10px -12px` → `-8px -10px`, coherente con
  el `padding` reducido de `.doc-form`).
- `.filter-card .col-auto`: en `@media (max-width: 576px)` → `min-width: 100%`
  (cada filtro ocupa la fila; `.filter-card` ya hace `flex-wrap`).
- `.line-items-table`: hoy los 3 `_TabContent*.cshtml` ya envuelven la tabla en
  `<div style="overflow-x:auto;">` — un **estilo inline** (viola la regla dura #2).
  Se reemplaza en los 3 por `<div class="line-items-wrapper">` y se define en `site.css`:
  `.line-items-wrapper { overflow-x: auto; -webkit-overflow-scrolling: touch }`.
  Sobre `.line-items-wrapper .line-items-table`: primera celda de cada fila
  (`td:first-child` / `th:first-child`, código de artículo) `position: sticky; left: 0;
  background: var(--card-bg); z-index: 1` para tener referencia al scrollear horizontal;
  `min-width` por columna vía `.line-items-table th`/`td` para que los inputs no se
  aplasten (valores concretos en el plan, ~110–140px según el tipo de campo).
- `.document-list-table-wrapper`: agregar `-webkit-overflow-scrolling: touch` y una
  sombra de borde derecha sutil (`mask` o `background-attachment: local` con gradiente)
  para señalar columnas ocultas. Opcional, si añade complejidad se omite.
- `.form-row`, `.doc-columnas-2`, `.menu-perfil-fila`: la media query existente
  `767.98px` se sube a `900px` (ver sección 1).

**NO se hace** (marcado como futuro): convertir `.line-items-table` a "tarjeta por
línea" en teléfono — es un rediseño de los 3 `_TabContent*.cshtml` +
`document-lines-editor.js`.

### 6. Archivos tocados

| Archivo | Cambio |
|---|---|
| `wwwroot/css/design-tokens.css` | Comentario-contrato de breakpoints. |
| `wwwroot/css/sidebar.css` | Media queries del shell: off-canvas drawer, `.shell-scrim`, `.topbar-nav-toggle`, topbar/user-menu/content responsive. (~90 líneas nuevas al final.) |
| `wwwroot/css/site.css` | Reordenar + arreglar bloque login; media queries de Home y `.doc-*`/`.line-items-table`; subir breakpoint `.form-row` a 900px; clases nuevas `.inicio-encabezado`/`.inicio-panel`. |
| `wwwroot/css/components.css` | Solo si `.filter-card` base necesita ajuste (probable, menor). |
| `wwwroot/js/sidebar.js` | Toggle del drawer (open/close por botón, scrim, Escape, click en link, resize). |
| `Pages/Shared/_LayoutMaestro.cshtml` | `<button class="topbar-nav-toggle">` + `<div class="shell-scrim">` en ambos shells; cargar `sidebar.js` para `isAuthenticated` (no solo tenant). |
| `Pages/Home/Index.cshtml` | Quitar `style="..."` inline → clases; picker con `hidden` en vez de `style.display`. |
| `plugins/Modulo.Ventas/Pages/Shared/_TabContentVentas.cshtml` | `<div style="overflow-x:auto;">` → `<div class="line-items-wrapper">`. |
| `plugins/Modulo.Compras/Pages/Shared/_TabContentCompras.cshtml` | Ídem. |
| `plugins/Modulo.Inventario/Pages/Shared/_TabContentInventario.cshtml` | Ídem. |

## Componentes y responsabilidades

- **`sidebar.css` (shell responsive):** define CÓMO se ve/comporta el shell en cada
  breakpoint. Depende de: tokens (`--sidebar-width`, `--drawer-transition`,
  `--transition-fast`), y del atributo `data-shell-nav` en `<html>`.
- **`sidebar.js` (estado del drawer):** único responsable de poner/quitar
  `data-shell-nav="open"` y `hidden` en el scrim. No toca CSS ni layout directo.
- **`_LayoutMaestro.cshtml` (markup del shell):** aporta el botón toggle y el scrim.
  No sabe de breakpoints (eso es CSS).
- **`site.css` bloque login / bloque doc:** responsive de pantallas concretas,
  independiente del shell.

## Pruebas

- `dotnet build PortalSaas.sln` → 0/0.
- `dotnet test tests/PortalSaas.Core.Tests` → 200/200 (no hay C# nuevo; cambios son
  CSS/markup/JS).
- Verificación visual manual en emulador de Chrome DevTools a **360 / 414 / 768 / 1024 /
  1440 px** en:
  - `/Account/Login` — panel de marca oculto ≤900px, formulario centrado, sin scroll
    horizontal, inputs 44px en ≤576px, landscape (~740×360) scrollea sin cortar.
  - `/Home` — drawer cerrado al cargar; hamburguesa abre/cierra; scrim cierra; grilla
    de accesos 2 columnas en 360px; sin scroll horizontal; panel de usuario no se corta.
  - `/ventas/ordenes` (listado) — `.doc-tabs` no aplica acá; filtros 1 por fila en
    ≤576px; tabla scrollea internamente; paginación visible.
  - Detalle de una orden — `.doc-tabs` en una fila deslizable; `.form-row` 1 columna;
    editor de líneas scrollea horizontal con la 1ª columna sticky.
  - `/Admin/Organizations` — drawer funciona en `/Admin/*` (sidebar.js cargado);
    acciones de fila (iconos + dropdown) no desbordan.
- Regresión desktop (>900px): shell idéntico al actual (sidebar en flujo, rail
  64/250px, resize handle, topbar completa).

## Fuera de alcance (futuro)

- "Tarjeta por línea" para `.line-items-table` en teléfono.
- Buscador global / notificaciones / botón "+" del topbar (siguen `disabled`).
- CSS de plugins (`Modulo.Rendiciones/wwwroot/css/rendiciones.css` ya tiene sus media
  queries propias).
- Modo landscape de tablet grande específico (queda cubierto por `lg`).
