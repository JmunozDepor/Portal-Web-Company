# Login a compañía por defecto y excepciones de permisos por usuario

Fecha: 2026-08-24

## Contexto

Se detectó que el usuario `prueba1`, con "Compañía por defecto" = DEPOR_TEST configurada en su perfil, no podía loguearse automáticamente en esa compañía. La causa raíz: `CompanySessionActivator.TryActivateAsync` exige que exista una fila real de acceso (`UserMenuGroups` o `UserMenuProfiles`) para el usuario en esa compañía; si falta, la activación falla silenciosamente y el flujo cae al selector manual mostrando un mensaje genérico ("Compañía inválida o sin acceso") sin explicar la causa.

A partir de este hallazgo, se revisaron 6 mejoras solicitadas y se descompusieron en bloques independientes. Este documento cubre los dos priorizados:

- **Bloque A**: login debe entrar directo a la compañía por defecto cuando hay acceso, y dar un mensaje claro cuando no lo hay.
- **Bloque B**: exponer en UI el mecanismo de excepciones de permisos por usuario (que ya existe a nivel de modelo/backend), para poder ajustar permisos puntuales sin depender solo de grupos.

Quedan fuera de este spec (para diseño posterior, bloques separados): administración de menús/orden dentro de `/organizacion` (punto 4) y recuperación de contraseña en login (punto 6).

## Hallazgos previos (ya confirmados, no requieren nuevo diseño)

- El selector de multi-compañía en el topbar (`CompanySwitcher`, `_Layout.cshtml:75`) **ya existe y funciona**: filtra correctamente por acceso real (`UserMenuGroups`/`UserMenuProfiles`), no se muestra si el usuario solo tiene una compañía. El punto 2 del pedido original no requiere desarrollo.
- El mecanismo de excepción puntual de permisos por usuario **ya existe a nivel de datos y resolución**: `UserMenuProfile` (UserId + MenuId + CompanyId + ProfileId) se consulta con prioridad sobre lo heredado del grupo, tanto en `CurrentUserContext.HasActionAsync` (`PortalSaas.Core/Seguridad/CurrentUserContext.cs:37-75`) como en `MenuNavigationService` (`PortalSaas.Core/Infraestructura/MenuNavigationService.cs:100-125`, que además unifica por unión con lo heredado del grupo). No falta modelo de datos; falta UI de administración.
- `/organizacion/usuarios` (tenant, self-service) y `/Admin/Organizations/Users` (PlatformAdmin) son dos implementaciones separadas e intencionales, con distinto esquema de autenticación, que comparten la tabla `users` pero no el servicio de negocio.

## Bloque A — Login a compañía por defecto

### Objetivo

Al loguearse, si el usuario tiene una compañía por defecto configurada y acceso real a ella, debe entrar directo sin pasar por el selector manual. Si no tiene acceso, debe recibir un mensaje explícito, no un mensaje genérico de "compañía inválida".

### Cambio

Archivo: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/SelectCompany.cshtml.cs`, método `OnGetAsync` (líneas ~73-82).

1. Si `_activator.TryActivateAsync(HttpContext, defaultCompanyId)` devuelve una sesión activada (no `null`), redirigir directo al Home — este camino ya funciona hoy cuando hay acceso.
2. Si devuelve `null` (sin acceso a la compañía por defecto):
   - Setear un mensaje de error específico y accionable, ej.: *"Tu compañía por defecto (DEPOR_TEST) no tiene permisos configurados. Contacta a tu administrador."* — usando el nombre/código de la compañía, no un mensaje genérico.
   - Consultar si el usuario tiene acceso real (`UserMenuGroups`/`UserMenuProfiles`) a **alguna otra** compañía de la organización.
     - Si tiene acceso a al menos una más, mostrar el selector como hoy, pero listando únicamente las compañías con acceso real (mismo filtro que ya usa `CompanySwitcherViewComponent`), junto con el mensaje de aviso de por qué no entró directo.
     - Si no tiene acceso a ninguna compañía, no mostrar el selector ni el dropdown — solo el mensaje de error y el link "Cerrar sesión" (ya existente en la vista).

### Vista

`SelectCompany.cshtml`: condicionar el bloque del `<select>` de compañía a que la lista de compañías con acceso no esté vacía; si está vacía, mostrar solo el mensaje de error y "Cerrar sesión".

### Fuera de alcance

- No se modifica `CompanySessionActivator.TryActivateAsync` ni el modelo de datos de acceso.
- No se toca `CompanySwitcher` (ya funciona correctamente).

## Bloque B — UI de excepciones de permisos por usuario

### Objetivo

Permitir que un administrador (tenant u operador de plataforma) ajuste el permiso de un usuario para una pantalla puntual, sin modificar el grupo de menú completo, reutilizando el mecanismo `UserMenuProfile` ya implementado.

### Cambio

En ambas consolas de administración de usuarios:
- `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Organizations/Users/Permissions.cshtml(.cs)` (PlatformAdmin)
- Equivalente self-service bajo `Modulo.Administracion/Pages/Usuarios/` (tenant, `/organizacion/usuarios`)

Agregar una sección **"Excepciones por pantalla"** en la pantalla de permisos del usuario, para la compañía activa/seleccionada:

1. **Listado**: mostrar las pantallas (`MenuGroupItem`/menús) que el usuario ve heredadas de los grupos asignados en esa compañía, con el `Profile` heredado (o "Sin perfil por defecto" si el grupo no da acceso).
2. **Override**: por cada pantalla, un selector para asignar un `Profile` distinto al heredado — incluye una opción explícita "Sin acceso" (equivalente a un `Profile` sin acciones, o ausencia de override que bloquee). Al guardar, crea o actualiza la fila `UserMenuProfile(UserId, MenuId, CompanyId, ProfileId)`.
3. **Quitar excepción**: acción para eliminar la fila `UserMenuProfile` correspondiente y volver a heredar del grupo.
4. El alcance de esta iteración es únicamente sobre pantallas **heredadas de un grupo** (no se permite en este spec agregar acceso a una pantalla que ningún grupo del usuario incluye — eso queda para una iteración futura si se necesita).

### Backend

- Tenant: usar `TenantUserAdminService` (o extenderlo) para las operaciones de alta/baja de `UserMenuProfile`, acotado a `OrganizationId` del contexto actual.
- PlatformAdmin: usar el código ya existente en `Pages/Admin/Organizations/Users/Permissions.cshtml.cs`, sin restricción de organización.
- Ambas superficies escriben/leen la misma tabla `UserMenuProfile` — no se duplica modelo de datos, solo la superficie de administración (siguiendo el patrón ya existente en el resto del sistema).

### Fuera de alcance

- No se modifica `CurrentUserContext.HasActionAsync` ni `MenuNavigationService` — la resolución en runtime ya prioriza `UserMenuProfile` correctamente.
- No se agrega un campo "Nivel" nuevo (Full/Lectura/Sin acceso) al modelo — se reutiliza el sistema de `Profile`/`PermissionAction` existente.

## Testing

- Bloque A: pruebas de `SelectCompany.OnGetAsync` cubriendo: (1) default con acceso → redirige directo; (2) default sin acceso pero con otra compañía accesible → selector filtrado + mensaje; (3) default sin acceso y sin ninguna compañía accesible → solo mensaje, sin selector.
- Bloque B: pruebas de alta/baja de `UserMenuProfile` desde ambas consolas, y verificación de que `CurrentUserContext.HasActionAsync` refleja el override inmediatamente después de guardarlo.
