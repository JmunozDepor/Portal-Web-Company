# Diseño: Modo desatendido de SAP + catálogos locales (Pieza 1 de 3)

Fecha: 2026-08-14
Estado: Aprobado para implementación

## Contexto

Modulo.Rendiciones hoy depende de una conexión SAP Business One en vivo para dos
catálogos: centro de costo (`ICostCenterCatalogService`, OPRC) y plan de cuentas
(`IGeneralLedgerAccountCatalogService`, OACT). Ambos se consultan en cada request,
sin caché, en tres puntos: `Configuracion/CentrosCostoUsuario`,
`UserCostCenterService.GetAvailableAsync` (fallback cuando el usuario no tiene
asignaciones propias) y la búsqueda de cuentas en `Configuracion/TiposGasto`.

El resto del modelo de datos ya es "blando" hacia SAP: `CostCenterCode`,
`SapGlAccount`, `SupplierTaxId`, `Currency` son todos texto libre sin FK. No existe
hoy ningún motor de integración multi-ERP ni caché de catálogo — la documentación y
el código asumen SAP como único backend.

## Objetivo de esta pieza

Este es el primero de tres diseños relacionados:
1. **Esta pieza**: que Rendiciones pueda operar sin conexión SAP activa para una
   compañía dada, con catálogos propios como única fuente de lectura en el flujo
   diario.
2. (Futuro) Capa de integración genérica multi-ERP (más allá de SAP).
3. (Futuro) Sincronización bidireccional (subida de rendiciones aprobadas hacia el
   ERP, no solo bajada de catálogos).

Se diseñan por separado porque son subsistemas independientes; esta pieza sienta la
base de datos y la interfaz de sync que las piezas 2 y 3 van a extender.

Requisito explícito del usuario: SAP no se "apaga" globalmente — es una opción
**por compañía**, activable/desactivable desde administración. Una compañía puede
tener el plugin cargado sin conectarse nunca a SAP; otra compañía del mismo
despliegue puede seguir sincronizando con su SAP normalmente.

## Diseño

### 1. Toggle por compañía

`RendicionesSettings` (ya existe, keyed por `CompanyId`) gana un campo:

```
public bool SapCatalogSyncEnabled { get; set; } = true;
```

Default `true` para no romper compañías existentes que ya dependen de SAP en vivo
hoy. Se apaga explícitamente desde la nueva página de administración.

### 2. Catálogos locales como única fuente de lectura

Dos tablas nuevas en la BD propia de Rendiciones (mismo motor dual Postgres/SqlServer
que el resto del plugin):

```
CostCenter: Id, CompanyId, Code, Name, IsActive, Source (Sap|Manual), UpdatedAt
GlAccount:  Id, CompanyId, Code, Name, IsActive, Source (Sap|Manual), UpdatedAt
```

A partir de esta pieza, **todo el flujo de uso diario lee de estas tablas**, nunca
en vivo contra SAP:
- `Configuracion/CentrosCostoUsuario` — selector de centros de costo por usuario.
- `UserCostCenterService.GetAvailableAsync` — el fallback "sin asignaciones propias"
  pasa de "catálogo SAP completo en vivo" a "catálogo local completo activo". Esto
  simplifica el código actual (elimina la llamada en vivo y su manejo de error).
- `Configuracion/TiposGasto` — búsqueda de cuenta contable para asociar a un
  `ExpenseType`.

`Source` distingue si una fila llegó por sincronización SAP o fue cargada a mano —
solo informativo (se muestra en el listado), no afecta el comportamiento de lectura.

### 3. Cómo se llenan las tablas: `ICatalogSyncProvider`

Interfaz nueva **dentro del plugin** (no en `PortalSaas.Abstractions` — todavía no
hay un segundo proveedor real que justifique esa abstracción compartida; se
promueve a Abstractions en la Pieza 2 si corresponde):

```csharp
public interface ICatalogSyncProvider
{
    Task<CatalogSyncResult> SyncCostCentersAsync(Guid companyId, CancellationToken ct);
    Task<CatalogSyncResult> SyncGlAccountsAsync(Guid companyId, CancellationToken ct);
}

public sealed record CatalogSyncResult(int Created, int Updated, int Deactivated, IReadOnlyList<string> Warnings);
```

Única implementación por ahora: `SapCatalogSyncProvider`, que envuelve los
contratos SAP existentes (`ICostCenterCatalogService`/`IGeneralLedgerAccountCatalogService`)
y hace upsert en `CostCenter`/`GlAccount` marcando `Source = Sap`. Filas que ya no
aparecen en SAP se marcan `IsActive = false` (no se borran — pueden estar
referenciadas históricamente).

Sync es **manual únicamente** en esta fase (botón "Sincronizar ahora"). No se agrega
job periódico automático — evita diseñar ahora scheduling/conflictos/reintentos que
no están pedidos; se puede agregar después reusando `RendicionesReminderBackgroundService`
como referencia de patrón si hace falta.

### 4. Nueva página: `Configuracion/Integracion`

Página nueva (no reutiliza `Configuracion/Notificaciones`) porque es la config de
conectividad con el ERP, un dominio distinto, y es donde va a crecer la Pieza 2
(multi-ERP) más adelante — mejor que nazca en su propio espacio.

Contenido:
- Toggle `SapCatalogSyncEnabled` (on/off), con fecha/hora de la última vez que se
  cambió (auditoría simple, mismo criterio que otras páginas de Configuracion).
- Si **on**: botón "Sincronizar ahora" (dispara `SyncCostCentersAsync` +
  `SyncGlAccountsAsync`, muestra resultado: creados/actualizados/desactivados/warnings).
  Debajo, listado read-only de `CostCenter`/`GlAccount` (mismo patrón tabla que el
  resto del módulo).
- Si **off**: el listado se vuelve editable — Alta/Editar/Desactivar manual, mismo
  patrón listado+"Editar" ya usado en `Proveedores`/`CentrosCostoUsuario`. No hay
  botón de sync.
- Cambiar el toggle de on→off no borra las filas existentes (quedan como estaban,
  ahora editables a mano). Cambiar de off→on no descarta ediciones manuales previas:
  el próximo sync hace upsert por `Code`, así que una fila manual con el mismo código
  que trae SAP se sobrescribe (gana SAP), pero una fila manual sin equivalente en SAP
  queda intacta.

### 5. Autorización

Misma política que el resto de `Configuracion/*` (rol admin del módulo) — no se
introduce ningún nivel de permiso nuevo.

## Fuera de alcance (explícitamente, para esta pieza)

- Sincronización automática/periódica de catálogos.
- Cualquier ERP que no sea SAP (eso es la Pieza 2).
- Envío de datos hacia el ERP (rendiciones aprobadas, etc. — eso es la Pieza 3).
- Migrar `ExpenseType.SapGlAccount`/`UserCostCenter.CostCenterCode` a FKs reales
  contra las tablas nuevas — se mantienen como snapshot de texto libre por ahora
  (cambiarlo es una migración de datos aparte, no bloquea esta pieza).

## Testing

- Unit tests para `SapCatalogSyncProvider` (upsert, desactivación de filas ausentes)
  con SAP catalog service mockeado — mismo patrón que
  `UserCostCenterServiceTests` (EF Core InMemory).
- Unit test para `UserCostCenterService.GetAvailableAsync` confirmando que el
  fallback ahora lee de `CostCenter` local, no de SAP.
- Test manual: compañía con toggle off puede cargar un gasto usando un centro de
  costo cargado a mano, sin que el plugin intente ninguna llamada SAP.
