# Diseño — Página "Mapeo de Campos" (Modulo.Wms)

Fecha: 2026-08-11
Estado: aprobado, pendiente de implementación

## Contexto

`ARQUITECTURA.md` documenta que Fase 1 del plugin `Modulo.Wms` está scaffolded
(modelos, `WmsDbContext`, migraciones aplicadas, `ModuloWms.cs` con menú base)
pero sin páginas Razor detrás todavía. El propio documento sugiere empezar por
**Mapeo de Campos**, por ser el CRUD más simple de los tres pendientes (Mapeo
de Campos / Configuración del Servicio / Estado del Servicio) y porque tiene
un precedente real en producción: `WmsPortal.Web` → `Admin/FieldMapping`
(tabla HANA `INT_SAP_FIELD_MAPPING`).

Este spec cubre exclusivamente esa página. No toca `WmsSapIntegration.Service`
ni generaliza el mapeo a campos estándar SAP→WMS (eso es Fase 2, ítem 3).

## Objetivo

Dar de alta la primera página Razor real del plugin: CRUD sobre
`wms_oracle_field_mappings`, portando el motor de templates de `WMS_Suite`
(`{lpn}`, `{header.MessageId}`, concatenación con `;`, literal `Y`) tal cual —
es lógica de negocio real ya en producción, no se reescribe.

## Decisiones de este diseño

- **Patrón UI: Razor Pages server-rendered**, igual que
  `Modulo.Rendiciones/Pages/Configuracion/TiposGasto/Index.cshtml` — tabla con
  inputs inline por fila, un `<form>` por fila asociado por `form="id"`,
  handlers `OnPost*Async` (`Guardar`/`Activar`/`Desactivar`/`Crear`). No se
  porta el modal JS + fetch AJAX del original, para mantener consistencia con
  el resto del Portal (sin JS custom más allá de lo que ya usa el layout).
- **MapperKey por dropdown fijo**, no texto libre. Las 6 claves conocidas
  (`ORDER_CONFIRM_STOCKTRANSFER`, `ORDER_CONFIRM_DELIVERYNOTE`,
  `ORDER_CONFIRM_XDK_STOCKTRANSFER`, `RECEIPT_CONFIRM_STOCKTRANSFER`,
  `RECEIPT_CONFIRM_RETURN`, `RECEIPT_CONFIRM_PURCHASE_DELIVERY`) se mueven de
  `WMS_Suite/WmsPortal.Core/Models/FieldMappingModels.cs`
  (`FieldMappingKeys.Labels`) a `PortalSaas.Abstractions` como constante
  compartida — decisión ya registrada en `ARQUITECTURA.md` ("Constantes
  compartidas movidas a `PortalSaas.Abstractions`"). Evita typos que rompan el
  contrato manual de strings con `WmsSapIntegration.Service`.
- **MapperKey y Campo UDF bloqueados en edición.** Son la identidad lógica de
  la fila (`UNIQUE(company_id, mapper_key, field_name)`) y así los espera
  `WmsSapIntegration.Service` del lado standalone. Solo Valor/Plantilla y
  Activo son editables en una fila existente; para cambiar Documento o Campo
  hay que crear un mapeo nuevo y desactivar el anterior.
- **Soft state, no delete.** Activar/Desactivar vía handlers dedicados, mismo
  patrón que `TiposGasto` — nunca se borra una fila.
- **Scoping por `ICurrentCompanyAccessor.CompanyId`**, sin fallback a
  `Organization` — regla dura del Portal, ya aplicada en `WmsDbContext`.
- **`UpdatedBy` = usuario logueado del Portal.** Los handlers de
  `Crear`/`Guardar`/`Activar`/`Desactivar` toman `ICurrentUserContext.Username`
  y lo pasan al servicio — no es un campo que ingresa el usuario en el
  formulario, se asigna automático en cada escritura, igual que hace
  `WMS_Suite` hoy (columna `UpdatedBy` de `INT_SAP_FIELD_MAPPING`).
- **Estilo: clases del Portal ya existentes** (`admin-card`, `admin-table`,
  `admin-form-asignar`, `btn-erp-primary`, `btn-module-action`, etc.), mismas
  que usa `TiposGasto` — sin CSS propio de WMS, sin reescribir el look de los
  formularios base.

## Arquitectura

```
Modulo.Wms/
  Services/
    IFieldMappingService.cs
    FieldMappingService.cs      -- capa sobre WmsDbContext
  Pages/
    MapeoCampos/
      Index.cshtml
      Index.cshtml.cs
```

- `IFieldMappingService`: `ListAllAsync(companyId, ct)`,
  `CreateAsync(companyId, mapperKey, fieldName, valueTemplate, isActive, updatedBy, ct)`,
  `UpdateAsync(id, companyId, valueTemplate, isActive, updatedBy, ct)` (no
  recibe `mapperKey`/`fieldName` — bloqueados en edición, ver arriba).
  Mismo patrón que `IExpenseTypeService` de `Modulo.Rendiciones`.
- `ModuloWms.RegisterServices` agrega
  `services.AddScoped<IFieldMappingService, FieldMappingService>()`.
- Ruta `/wms/mapeo-campos` — ya reservada en `ModuloWms.GetMenu()`
  (`Code = "mapeo-campos"`).
- `WmsFieldMapperKeys` (nueva clase en `PortalSaas.Abstractions`): diccionario
  clave → label legible, puerto directo de `FieldMappingKeys.Labels`.

## UI

Tabla: Documento (label del MapperKey) | Campo UDF | Valor/Plantilla | Activo
| Actualizado | Acciones.

- **Filas existentes:** Documento y Campo UDF como texto plano (no
  editables). Valor/Plantilla y Activo editables inline, guardado vía
  `form="mapeo-{id}"` → `OnPostGuardarAsync`.
- **Activar/Desactivar:** botones POST a handlers dedicados, igual patrón que
  `TiposGasto` (formulario propio por fila, fuera de la tabla, con los valores
  actuales como `asp-route-*` para no perderlos al cambiar solo el estado).
- **Nuevo mapeo:** formulario aparte debajo de la tabla —
  `<select>` de MapperKey (las 6 claves), input Campo UDF (placeholder
  `U_...`), input Valor/Plantilla, checkbox Activo. Handler
  `OnPostCrearAsync`.
- Texto de ayuda bajo el formulario, portado del original: explica el
  lenguaje de placeholders (`{lpn}`, `{header.NombreColumna}`, literal `Y`
  para valores fijos).

## Validación y errores

- `[Required]` en `ModelState` para Campo UDF y Valor/Plantilla en el
  formulario de creación.
- `CreateAsync`/`UpdateAsync` capturan la violación de
  `UNIQUE(company_id, mapper_key, field_name)` y la traducen a
  `ErrorMessage`: "Ya existe un mapeo para ese documento y campo UDF." — mismo
  patrón `try/catch` + `GetErrorMessage(ex)` que usa `TiposGasto`.

## Fuera de alcance

- No se toca `WmsSapIntegration.Service` (sigue leyendo estas 3 tablas vía su
  propia conexión de solo lectura, ya cubierto en Fase 1 de
  `ARQUITECTURA.md`).
- No se generaliza el motor de mapeo para campos estándar SAP→WMS (Fase 2,
  ítem 3).
- No se implementan `Configuración del Servicio` ni `Estado del Servicio`
  (siguientes páginas pendientes, fuera de este spec).
