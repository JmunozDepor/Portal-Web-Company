# DESIGN.md — Portal SaaS - Core

Extensión de `CLAUDE.md` §"Densidad visual estándar". Ese documento fija densidad,
color y comportamiento; este fija **tipografía** y **paridad visual `/Admin/*` vs.
resto del portal** — los dos huecos reales detectados en la auditoría visual del
10 ago 2026 (sin fuente propia declarada en ningún lado del CSS; `/Admin/*` quedó
fuera de las 4 rondas de rediseño y sigue en Bootstrap liso). Regla dura, igual
criterio que el resto del proyecto: **no reabrir sin una razón nueva y explícita.**

## 1. Tipografía

Hoy, en ~1730 líneas de CSS (`site.css`+`sidebar.css`), la única declaración de
`font-family` es `.login-bg { font-family: var(--font-sans, inherit); }` —
sin fallback real, cae al stack del sistema operativo del usuario. Ningún otro
selector del portal declara una fuente. Se corrige con dos tokens nuevos en
`:root` de `site.css`, consumidos por `body` (y por los pocos selectores que
necesitan la variante monoespaciada).

### 1.1 Fuente principal — IBM Plex Sans

```css
:root {
  --font-sans: "IBM Plex Sans", "Segoe UI", system-ui, -apple-system, sans-serif;
}

body {
  font-family: var(--font-sans);
  font-variant-numeric: tabular-nums;
}
```

**Por qué IBM Plex Sans, no otra:**
- Diseñada por IBM para software técnico/empresarial (misma familia de audiencia
  que un cliente SAP Business One) — se lee como una herramienta de trabajo, no
  como un producto de consumo.
- Trae soporte nativo de **tabular figures** (`font-variant-numeric:
  tabular-nums`) — cada dígito ocupa el mismo ancho, así que una columna de
  `Total`/`Cantidad`/`Precio Unitario` en `.document-list-table`/
  `.line-items-table` alinea verticalmente sin que los números "bailen" al
  cambiar de fila. Es la razón técnica concreta por la que se eligió sobre
  Inter/Segoe UI para este proyecto específico — las grillas densas tipo SAP B1
  son el caso de uso central del portal, no un extra.
- Pesos necesarios para el proyecto: 400 (texto), 500 (`.form-label`
  ya usa cierto peso medio), 600 (`.btn`/`.sidebar-nav a.active`/`.user-name`,
  que hoy fuerzan `font-weight: 600/700` sobre una fuente sin ese peso definido
  explícitamente), 700 (`.doc-status-badge`/`.topbar-title`/`.login-card h2`).

**Carga**: self-hosted (`wwwroot/fonts/`, formato `.woff2`), no Google Fonts CDN
— coherente con el resto del proyecto (Bootstrap/Bootstrap Icons/jQuery también
vendored a mano, sin `libman.json`, ver `CLAUDE.md`). Pesos a empaquetar: 400,
500, 600, 700 — los 4 que el CSS ya fuerza vía `font-weight` en distintos
selectores. `@font-face` con `font-display: swap` (evita texto invisible
mientras carga, acepta el salto de layout mínimo de una fuente de sistema a
Plex — aceptable para un backoffice, no para un sitio de marketing).

### 1.2 Fuente monoespaciada — IBM Plex Mono

```css
:root {
  --font-mono: "IBM Plex Mono", "Cascadia Code", "Consolas", monospace;
}
```

**Por qué IBM Plex Mono y no JetBrains Mono**: misma familia tipográfica que
`--font-sans` (Plex Sans + Plex Mono comparten métricas de diseño, pensadas para
convivir) — un código SAP en medio de una celda de texto (ej. `"ItemCode: A0012"`
dentro de una descripción) no choca de estilo con el resto de la fila. JetBrains
Mono es una alternativa válida si se prefiere más distinción visual entre prosa y
código, pero no es la elección por defecto de este documento — no mezclar las dos
familias mono en el mismo proyecto.

**Dónde aplicar `--font-mono`** (regla dura, no opcional — un valor SAP nunca se
lee en la fuente de texto corrido):
- `ItemCode`/`CardCode`/`WhsCode`/`DocEntry`/`AcctCode` — cualquier código crudo
  proveniente de SAP, sea en una celda de `.document-list-table`/
  `.line-items-table`, un `<input>` de código en `.doc-tab-seccion .form-group`,
  o el chip de "código — nombre" de un catálogo en modo solo-lectura (ver
  `CLAUDE.md`, regla de catálogos SAP).
- Claves de activación de licencia on-premise (`/Admin/Organizations/Licenses`).
- El token de sesión/diagnóstico si alguna vez se muestra en UI (hoy no aplica).

**Dónde NO aplicar** — nombres (`CustomerName`/`ItemDescription`), montos
(`Total`/`UnitPrice`, que ya resuelven su alineación con `tabular-nums` de
`--font-sans`, no necesitan mono), y cualquier texto de negocio. Aplicar mono a
un monto sería el error inverso — se lee como un valor de configuración técnica,
no como dinero.

```css
.sap-code {
  font-family: var(--font-mono);
  font-size: 0.92em; /* Plex Mono es visualmente más grande que Plex Sans al
                         mismo font-size nominal — se compensa acá una vez, no
                         por selector. */
}
```

Clase utilitaria única (`.sap-code`), aplicada en el markup donde corresponda
(Razor) — no un selector por tipo de columna, para que un catálogo nuevo la
adopte con solo agregar la clase, mismo criterio de reuso que `.line-items-table`/
`.doc-status-badge`.

### 1.3 Migración, sin tocar la densidad ya calibrada

`font-size` en `px` (no en `rem` relativos a la fuente) en todo `site.css` —
migrar la fuente no debería mover ningún tamaño ya validado
(`.form-control`: 13px, `.line-items-table thead th`: 11px, etc.). Verificar
después del cambio, no antes: IBM Plex Sans tiene un x-height ligeramente mayor
que Segoe UI al mismo `font-size` nominal — una vez cargada la fuente real,
revisar en navegador que `.doc-tab-seccion .form-group` (celdas de 3px 8px de
padding, las más ajustadas del sistema) no se vean apretadas. Si hace falta
ajustar, ajustar el `padding` de esa celda puntual, nunca bajar el `font-size`
por debajo de lo que ya se validó como legible.

## 2. Paridad visual `/Admin/*` vs. resto del portal

**Estado real (10 ago 2026)**: `/Admin/*` (backoffice de administrador de
plataforma — `Organizations`/`Plans`/`Profiles`/`MenuGroups`/`PlatformModules`/
etc.) nunca entró en ninguna de las 4 rondas de rediseño visual documentadas en
`CLAUDE.md`. Sigue con `<table class="table table-striped">` de Bootstrap sin
`.document-list-table`, y `.navbar.bg-dark` (ya cableado a `var(--bs-primary)`,
ver `site.css` línea ~354 — es la ÚNICA pieza de `/Admin/*` que ya hereda tema).
El resto del portal (shell de tenant, los 3 motores genéricos de documento) tiene
tabs tipo pill, tablas sticky con hover, badges `color-mix`, formularios en
grilla densa. Un admin de plataforma ve un Bootstrap por defecto de un lado y un
producto cuidado del otro — misma sesión, dos lenguajes visuales.

**Regla dura, sin excepción hacia adelante**: toda pantalla nueva bajo
`/Admin/*` se construye desde el día uno con las mismas clases que ya usa el
resto del portal — nunca `<table class="table table-striped">` a secas. Esto
alcanza a las pantallas existentes también, no solo a las nuevas (checklist
abajo).

### 2.1 Qué migrar, pantalla por pantalla

Todo `Pages/Admin/**/Index.cshtml` con una tabla de listado (`Organizations`,
`Plans`, `Profiles`, `MenuGroups`, `PlatformModules`, `Organizations/Users`,
`Organizations/Instances`, `Organizations/Companies`, `Organizations/
Subscriptions`, `Organizations/Licenses`) migra su `<table class="table
table-striped">` a:

```html
<div class="document-list-table-wrapper">
  <table class="table document-list-table">
    ...
  </table>
</div>
```

Sin envolver en `.card-ps`/`.doc-list` (ese patrón es específico del componente
`DocumentList` compartido por los 3 motores de documento, con su propio
paginador/filtro — `/Admin/*` no tiene ese componente ni falta agregarlo acá,
solo la tabla). El `.document-list-table-wrapper` ya trae borde/radio/scroll
propio y encabezado sticky sin cambios adicionales.

### 2.2 Formularios de `/Admin/*` (Create/Edit)

Los formularios de creación/edición (`Plans/Create`, `Organizations/Edit`, etc.)
hoy usan `row`/`col-md-*`/`mb-3` de Bootstrap crudo — mismo problema estructural
que tenían `_TabGeneral*.cshtml` de los 3 motores antes de la entrega "Auditoría
de paridad visual completa" (ver `CLAUDE.md`). Migrar al mismo patrón:

```html
<div class="doc-tab-seccion">
  <div class="form-row">
    <div class="form-group">
      <label asp-for="Input.Code" class="form-label"></label>
      <input asp-for="Input.Code" class="form-control" />
    </div>
    ...
  </div>
</div>
```

No hace falta el wrapper `.doc-tabs`/`.doc-tab-btn` (eso es específico de un
documento con múltiples secciones navegables) — un formulario simple de una
sola sección usa `.doc-tab-seccion` suelto, sin tabs.

### 2.3 Navbar/topbar de `/Admin/*`

`_AdminLayout.cshtml` usa un `<nav class="navbar navbar-dark bg-dark">` de
Bootstrap — visualmente distinto del `.topbar` del shell de tenant (blanco,
`--card-bg`, ver `sidebar.css`). **No se unifica al mismo componente `.topbar`**
a propósito: el backoffice de plataforma es un actor distinto (`PlatformAdmin`,
sin organización, ver `CLAUDE.md` §"Backoffice de administrador de plataforma")
y un fondo oscuro diferenciado es una señal útil real — evita que un
administrador confunda por error en qué sesión está parado (tenant vs.
plataforma), algo que ya importa en este proyecto (dos esquemas de cookie
separados, nunca deben mezclarse). Se mantiene `.navbar.bg-dark`, pero:

```css
.navbar.bg-dark {
  background-color: var(--bs-primary) !important; /* ya existe */
}

.navbar.bg-dark .navbar-brand,
.navbar.bg-dark .nav-link {
  font-family: var(--font-sans); /* nuevo -- hoy hereda default del navegador
                                     igual que el resto, ver §1 */
}
```

### 2.4 Botones y badges de `/Admin/*`

`.btn-primary`/`.btn-outline-secondary`/`.btn-outline-danger` ya están
redefinidos globalmente en `site.css` (no por página) — `/Admin/*` YA los
hereda sin cambios, confirmado leyendo el CSS (no hay ningún selector acotado a
`.sidebar`/`.topbar`/`/organizacion/*` que excluya al backoffice). Ningún botón
de `/Admin/*` necesita tocarse. Mismo caso para badges de estado
(`bg-success`/`bg-warning`/`bg-secondary`) — agregar la clase `doc-status-badge`
donde `/Admin/*` ya arma un badge de estado (ej. `Organizations/Index` estado
activo/inactivo, `Licenses/Index` estado de licencia) es la única migración
necesaria ahí, mismo criterio que ya se usó en los 3 motores de documento.

### 2.5 Checklist de migración (una vez, no por pantalla)

- [ ] Declarar `--font-sans`/`--font-mono` + `@font-face` de los 4 pesos de IBM
      Plex Sans y el peso 400 de IBM Plex Mono en `site.css`.
- [ ] `body { font-family: var(--font-sans); font-variant-numeric:
      tabular-nums; }`.
- [ ] Clase `.sap-code { font-family: var(--font-mono); }` agregada a
      `site.css`, sin consumidor todavía (se aplica pantalla por pantalla al
      tocar cada una, no en un solo pase — mismo criterio YAGNI del resto del
      proyecto: no reescribir 200 archivos `.cshtml` de una sola vez para
      agregar una clase cosmética).
- [ ] Cada `Pages/Admin/**/Index.cshtml` con tabla: envolver en
      `.document-list-table-wrapper`, agregar `.document-list-table` a la
      `<table>`.
- [ ] Cada `Pages/Admin/**/Create.cshtml`/`Edit.cshtml`: migrar de
      `row`/`col-md-*` a `.doc-tab-seccion > .form-row > .form-group`.
- [ ] `.navbar.bg-dark .navbar-brand/.nav-link`: heredar `--font-sans`
      explícito (ver §2.3).
- [ ] Badges de estado existentes en `/Admin/*`: agregar `doc-status-badge`
      junto a la clase `bg-*` que ya arma el `PageModel` (sin tocar C#, mismo
      patrón que los 3 motores).
- [ ] Verificar en navegador (pendiente real de todo el proyecto, ver
      `CLAUDE.md` — sin herramienta de captura de pantalla disponible en las
      sesiones hasta ahora) que `/Admin/Organizations/Index` y
      `/Admin/Plans/Create` se leen como la misma familia visual que
      `/ventas/ordenes`, no dos productos distintos.

**No incluido a propósito, mismo criterio YAGNI del resto del proyecto**: no se
migra `/Admin/*` a `.doc-tabs`/`.doc-tab-btn` (ninguna pantalla del backoffice
tiene múltiples secciones navegables hoy) ni a `.card-ps`/`.doc-list` completo
(ese patrón trae paginador/filtro del componente `DocumentList`, que
`/Admin/*` no usa) — la paridad que se busca es de **densidad y color**, no de
estructura de componente donde no aplica.
