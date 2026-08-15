# Motor de Integración ERP — Fase 2 (conector real + tooling + UI admin) — Diseño

**Fecha:** 2026-08-15
**Estado:** Aprobado para plan de implementación
**Precede a:** `docs/superpowers/specs/2026-08-14-motor-integracion-erp-design.md` (motor base, ya mergeado a `main`)

## Contexto

El motor base (`PortalSaas.Integrations`) fue mergeado a `main`: contratos, entidades EF Core, `IntegrationFieldMappingService`, `SapDocumentConnector` (placeholder que lanza `NotSupportedException`), `IntegrationSyncHostedService`, y registro DI en Host. La revisión final de ese plan dejó 8 puntos de seguimiento identificados; esta ronda cubre 3 de ellos:

1. **Conector SAP real** — hoy `SapDocumentConnector` no hace nada; hay que conectarlo a `SalesDocumentService`/`PurchaseDocumentService`/`InventoryDocumentService` de `PortalSaas.Core`.
2. **Gap de tooling en `PortalSaas.Host.csproj`** — `dotnet ef migrations add --startup-project src/PortalSaas.Host` falla porque Host no tiene el paquete EF Design ni referencia a los proyectos de migraciones.
3. **UI admin** — no existe ninguna forma de ver/operar las integraciones sin tocar la base de datos a mano.

Dos puntos de la lista original de 8 quedan fuera de esta tanda:
- **Punto 5 (cifrado en escritura)**: sin objeto en esta ronda porque la UI admin no incluye formulario de creación (ver más abajo) — no hay flujo de escritura donde conectar el cifrado todavía.
- **Punto 6 (migrar Wms)**: investigado y descartado para esta tanda — `Modulo.Wms` no tiene todavía ninguna tabla de staging con datos reales pendientes de enviar a SAP (`wms_oracle_inbound_stage` y afines existen solo en `ARQUITECTURA.md`, no en código). Implementar `IIntegrationEntityReader` hoy sería una interfaz sin dato real que mover. Queda para un ciclo separado, después de construir esa tabla de staging.
- **Puntos 7 y 8** (conector Sorter, Rendiciones↔ERP): siguen bloqueados por falta de información externa (protocolo del Sorter, ERP elegido para Rendiciones), sin cambios respecto a la ronda anterior.

## Decisión

### 1. `SapDocumentConnector` se traslada a `PortalSaas.Core`

En vez de mover interfaces de los document services a `PortalSaas.Abstractions` (dependencia invertida, más trabajo) o inyectar delegados desde Host (patrón menos claro), se traslada el archivo `SapDocumentConnector.cs` de `PortalSaas.Integrations` a `PortalSaas.Core` (namespace `PortalSaas.Core.Integraciones`). `IIntegrationConnector` vive en `PortalSaas.Abstractions` y cualquier proyecto puede implementarlo — no hace falta tocar la regla de dependencias (`Integrations` sigue sin referenciar `Core`; `Host` ya referencia ambos y sigue registrando el conector igual, solo cambia el `using`).

`PushAsync` invoca el document service correspondiente según un campo `TipoDocumento` presente en los datos ya mapeados del `IntegrationRecord` (el campo local ya fue definido por el `IntegrationFieldMapping` de cada integración — este conector no inventa un esquema nuevo, usa lo que el mapeo ya produjo). Los tres casos soportados: `Sales` → `SalesDocumentService.CreateAsync`, `Purchase` → `PurchaseDocumentService.CreateAsync`, `Inventory` → `InventoryDocumentService.CreateAsync`. Un `TipoDocumento` no reconocido lanza `NotSupportedException` con mensaje explícito (mismo patrón que el resto del motor).

`PullAsync` se mantiene sin cambios (`NotSupportedException` — bajada explícitamente fuera de alcance, decisión ya tomada en el fix de la revisión final de la ronda anterior).

### 2. Fix de tooling en `PortalSaas.Host.csproj`

Se agrega `Microsoft.EntityFrameworkCore.Design` como `PackageReference` (versión alineada con el resto de paquetes EF Core del proyecto) y `ProjectReference` a `PortalSaas.Data.Migrations.PostgreSql` y `PortalSaas.Data.Migrations.SqlServer`. Esto es puramente aditivo — no cambia comportamiento en runtime, solo habilita el flujo de tooling documentado en el plan original.

### 3. UI admin en `PortalSaas.Host/Pages/Admin/Integraciones/`

Sigue el patrón ya establecido por `Admin/Organizations/Index.cshtml`. Dos páginas Razor:

- **`Index.cshtml`**: tabla de `IntegrationDefinition` (nombre, módulo origen, tipo de conector, dirección, activo/inactivo, próxima ejecución). Botón "Ejecutar ahora" por fila — un `POST` que marca `NextRunAt = DateTimeOffset.UtcNow` y redirige a la misma página (el `IntegrationSyncHostedService` la recoge en su próximo ciclo de polling, sin ejecución inline en el request, consistente con la decisión de rendimiento ya tomada). Enlace "Ver bitácora" por fila.
- **`Bitacora.cshtml`** (`?id={integrationDefinitionId}`): lista paginada de `IntegrationRunLog` para esa integración, más reciente primero (fecha inicio/fin, resultado, registros procesados/con error, detalle de error si aplica).

**Sin formulario de creación/edición en esta tanda** — hoy solo existe un tipo de conector (`Sap`, con `PushAsync` recién conectado pero sin ningún módulo consumidor real todavía, ver punto 6 diferido), así que un formulario de creación completo no tendría con qué probarse de verdad. Las `IntegrationDefinition` de prueba se insertan por seed/migración de datos mientras tanto. Esto es también la razón por la que el punto 5 (cifrado en escritura) queda sin objeto: no hay flujo de escritura de `ConectorConfigCifrado` en esta ronda.

## Fuera de alcance (explícito)

- Formulario de creación/edición de `IntegrationDefinition`/`IntegrationFieldMapping` (y por lo tanto, cifrado en escritura).
- Migración de `Modulo.Wms` al motor genérico (requiere construir primero la tabla de staging real).
- Conector REST/Archivo, conector Sorter, consumidor Rendiciones — sin cambios respecto al plan anterior.
- `PullAsync` de `SapDocumentConnector` — sigue sin implementar.

## Criterio de éxito

- `dotnet ef migrations add --startup-project src/PortalSaas.Host` funciona sin el workaround de usar cada proyecto de migraciones como startup.
- Una `IntegrationDefinition` de tipo `Sap` con dirección `Subida`, sembrada manualmente con datos de prueba, ejecuta un `PushAsync` real contra `InventoryDocumentService` (o el document service que corresponda) cuando se dispara desde la UI admin.
- La UI admin permite ver el estado de las integraciones y su bitácora sin acceso directo a la base de datos.
