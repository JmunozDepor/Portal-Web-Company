# Personalización de menús por organización

Fecha: 2026-08-25

## Contexto

Punto 4 del pedido original del usuario ("dentro de la consola de administración
`/organizacion/*` el administrador pueda... dar orden lógico a los Menús"). La creación
de usuarios ya existe (`/organizacion/usuarios`). Lo que falta es personalizar el árbol
de menú por organización: hoy `Menu` es un catálogo global sincronizado por los plugins
(`MenuSyncService`), sin ninguna pantalla para reordenar, ocultar o renombrar un nodo
puntual — brecha ya documentada contra el original (`IMenuAdminService`,
`docs/08-BRECHA-FUNCIONAL-VS-PORTALSAP-V2.md`).

Alcance acordado explícitamente: **reordenar + ocultar + renombrar** nodos existentes.
Crear carpetas/páginas manuales nuevas queda fuera (alcance "completo" descartado).

## Modelo de datos

Nueva entidad `OrganizationMenuOverride` (`PortalSaas.Data.Entities`), tabla
`organization_menu_overrides`:

- `OrganizationId` (Guid, FK a `Organization`)
- `MenuId` (long, FK a `Menu`)
- Clave primaria compuesta `(OrganizationId, MenuId)` — una fila por combinación.
- `CustomLabel` (string?, null = usa `Menu.Name` sin cambios)
- `CustomOrder` (int?, null = usa `Menu.Order` sin cambios)
- `IsHidden` (bool, default `false`)

No se modifica `Menu` — sigue siendo el catálogo global sincronizado por
`MenuSyncService`, sin `OrganizationId` propio (mismo criterio que `MenuGroup`/
`Profile`). El override vive aparte, mismo patrón ya usado por
`OrganizationModuleVisibility` para ocultar módulos completos.

FK `MenuId` con `DeleteBehavior.Restrict` — si un nodo se desactiva/desaparece del
catálogo global (`MenuSyncService` lo marca `IsActive=false`, nunca lo borra), el
override queda huérfano de forma inofensiva (simplemente deja de aplicarse porque
`MenuNavigationService` ya filtra por `IsActive` antes de leer overrides).

## Resolución en runtime

`MenuNavigationService.GetVisibleMenuAsync` (`PortalSaas.Core.Infraestructura`):
después de cargar `activeMenus` y aplicar los dos filtros ya existentes (módulos
contratados, módulos ocultos por `OrganizationModuleVisibility`), agregar un tercer
paso, **en el mismo lugar y con el mismo criterio** (antes del bypass de
administrador — es preferencia de la organización, no permiso individual):

1. Cargar los overrides de `_currentUser.OrganizationId` (una consulta, sin N+1).
2. Para cada nodo con override: reemplazar `Name` por `CustomLabel` si no es null,
   `Order` por `CustomOrder` si no es null.
3. Para cada nodo con `IsHidden=true`: remover ese nodo **y todo su subárbol**
   (recorrido descendente sobre `activeMenus` ya cargado en memoria, mismo criterio
   sin-N+1 que ya usa `ExpandWithAncestors`) — evita dejar hijos huérfanos visibles
   sin su carpeta contenedora.

Este paso corre siempre, incluso sin ninguna fila de override (organización nueva) —
en ese caso no cambia nada, mismo criterio que el filtro de módulos contratados
(`catalogedModules.Count > 0` como guard ya establecido).

## UI

Nueva página `/organizacion/menus` en `plugins/Modulo.Administracion` (self-service de
organización, mismo patrón que `/organizacion/modulos`):

- Lista el árbol completo de `Menu` (`IsActive=true`), indentado por `Level` — mismo
  criterio visual que ya usa `/Admin/MenuGroups/Edit` para indentar por profundidad.
- Un único formulario (bulk-save, mismo patrón que `Permissions.cshtml`/
  `ProfileByMenu`: diccionario `MenuId -> valores`, un solo POST) con 3 campos por
  fila:
  - Texto: nombre personalizado (vacío = usa el original, mostrado como placeholder).
  - Número: orden personalizado (vacío = usa el original).
  - Checkbox: ocultar.
- Guardar hace upsert de `OrganizationMenuOverride` por cada fila que tenga algún
  valor distinto del default (`CustomLabel`/`CustomOrder` no vacíos, o `IsHidden`
  marcado) y borra la fila de override si una fila vuelve a sus 3 valores por
  defecto (mismo criterio "borrar para volver a heredar" que ya usa
  `Permissions.cshtml`/`UserMenuProfile`).

### Backend

Nuevo servicio `IOrganizationMenuOverrideService` (`PortalSaas.Abstractions.Contratos`,
implementado en `PortalSaas.Core.Administracion`), acotado a
`ICurrentUserContext.OrganizationId` (mismo patrón que `TenantUserAdminService`):

- `ListAsync()`: devuelve todos los nodos de `Menu` activos con su override actual
  (si existe) para la organización actual, ya indentados por `Level`.
- `SaveOverridesAsync(Dictionary<long, MenuOverrideInput> overridesByMenuId)`: upsert/
  delete por fila, todo en una transacción.

## Testing

- `OrganizationMenuOverrideServiceTests` (EF Core InMemory, mismo patrón que
  `TenantUserAdminServiceTests`): aislamiento entre organizaciones (el override de una
  organización no afecta a otra), upsert/delete correcto según los 3 valores, filas
  sin override no generan entradas en la tabla.
- Ampliar `MenuNavigationServiceTests` (si no existe, crear) o cobertura equivalente
  para: nodo renombrado aparece con el nombre custom; nodo reordenado respeta el
  orden custom; nodo oculto desaparece junto con su subárbol completo; sin ningún
  override, el árbol se comporta exactamente igual que hoy (regresión cero).
