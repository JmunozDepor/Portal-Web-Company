# Diseño: conectar Mapeo de Campos al camino SAP→WMS (Subida)

Fecha: 2026-08-16
Estado: Aprobado para implementación

## Contexto

La tabla `wms_oracle_field_mappings` (modelo `WmsFieldMapping`) y su pantalla admin
"Mapeo de Campos" ya existen y funcionan (CRUD verificado E2E), pero **hoy no las
consume nada en este repo**. Su doc comment aclara el alcance real: los 6 `MapperKey`
actuales (`ORDER_CONFIRM_*`, `RECEIPT_CONFIRM_*`) cubren la dirección **WMS→SAP**
(confirmaciones), consumida por `WmsSapIntegration.Service` — un Windows Service
standalone fuera de este repo. Extender la tabla al camino **SAP→WMS** (lo que hace
`WmsCloudConnector` al armar el XML de Item/Store/Order/IbShipment) es la
generalización de Fase 2 que ese mismo comentario deja pendiente.

Hoy, `WmsCloudConnector.ArmarXml` arma cada nodo XML con nombres de campo y, en el
caso de Order, hasta valores literales (`"DEPOR"`, `"BO02"`, `"1"`, `"CREATE"`) fijos
en C# — específicos de Comercial Depor, sin ninguna forma de ajustarlos por cliente
sin recompilar.

## Objetivo

Que `WmsCloudConnector` resuelva los campos de salida contra `wms_oracle_field_mappings`
cuando existe una fila configurada para ese campo, y caiga al comportamiento actual
(mismo mapeo hardcodeado que hoy) cuando no la hay — sin romper ninguna integración
existente.

## Diseño

### 1. Motor de resolución de templates (nuevo, no existía en este repo)

`Servicios/WmsFieldTemplateResolver.cs`, clase estática:

```csharp
public static string? Resolve(string template, IntegrationRecord registro)
```

Reemplaza cada placeholder `{NombreCampo}` por `registro[NombreCampo]?.ToString()`
(vacío si el campo no existe o es null), dejando el resto del texto del template tal
cual. Un template sin placeholders es un literal puro (cubre los casos hoy
hardcodeados como `"DEPOR"`). Concatenar dos campos es simplemente escribir
`{A} ; {B}` en el `ValueTemplate` — no hace falta lógica especial, mismo lenguaje que
ya documenta ARQUITECTURA.md.

### 2. Nuevos `MapperKey`

Agregados a `WmsFieldMapperKeys.Labels` (`PortalSaas.Abstractions.Modelos`):

- `SAPWMS_ITEM`
- `SAPWMS_STORE`
- `SAPWMS_ORDER_HDR`
- `SAPWMS_ORDER_DTL`
- `SAPWMS_INBOUND_HDR`
- `SAPWMS_INBOUND_DTL`

Header y detalle separados para Order/IbShipment porque resuelven contra registros
distintos (`IntegrationRecord` de cabecera vs. cada `IntegrationRecord` de `Lineas`).

### 3. Campos mapeables por entidad

- **Item** (`SAPWMS_ITEM`): `item_alternate_code`, `description`, `barcode`.
- **Store** (`SAPWMS_STORE`): `code`, `name`, `parent_company_id`.
- **Order header** (`SAPWMS_ORDER_HDR`): `company_code`, `action_code`,
  `facility_code`, `order_nbr`, `order_type`, `ref_nbr`, `dest_dept_nbr`, `priority`,
  `cust_field_2`, `cust_field_3`, `cust_field_4`, `cust_field_5`,
  `cust_short_text_1`, `cust_short_text_2`, `customer_po_nbr`.
- **Order detalle** (`SAPWMS_ORDER_DTL`): `order_nbr`, `seq_nbr`,
  `item_alternate_code`, `ord_qty`.
- **IbShipment header** (`SAPWMS_INBOUND_HDR`): `shipment_nbr`, `shipment_type`.
- **IbShipment detalle** (`SAPWMS_INBOUND_DTL`): `seq_nbr`, `item_alternate_code`,
  `shipped_qty`, `facility_code`.

**Fuera de alcance**: `ord_date`, `exp_date`, `req_ship_date` (Order header) quedan
con su formato fijo `yyyyMMdd` — necesitan tipado de fecha real, no sustitución de
texto simple; no se mapean en esta entrega.

### 4. `WmsCloudConnector`

Gana dos dependencias nuevas por constructor: `IFieldMappingService` (ya registrado
como `Scoped` en `ModuloWms.RegisterServices`) e `ICurrentCompanyAccessor` (ya
disponible en el scope donde se resuelve el conector — `IntegrationSyncHostedService`
fija `ICurrentCompanyOverride` antes de resolver conectores/readers/writers, así que
`ICurrentCompanyAccessor.CompanyId` ya funciona sin tocar la firma de
`IIntegrationConnector`).

Al principio de `PushAsync`, carga una sola vez los mapeos activos de la compañía:
```csharp
var mapeos = (await _fieldMappingService.ListAllAsync(_currentCompany.CompanyId, ct))
    .Where(m => m.IsActive)
    .ToDictionary(m => (m.MapperKey, m.FieldName), m => m.ValueTemplate);
```

Nuevo helper privado reemplaza cada `new XElement(campo, valorFijo)`:
```csharp
private static XElement CampoXml(
    IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos,
    string mapperKey, string fieldName, IntegrationRecord registro, object? valorPorDefecto)
{
    if (mapeos.TryGetValue((mapperKey, fieldName), out var template))
        return new XElement(fieldName, WmsFieldTemplateResolver.Resolve(template, registro));
    return new XElement(fieldName, valorPorDefecto);
}
```

`valorPorDefecto` es exactamente lo que el campo devuelve hoy (`registro["ItemCode"]`,
o el literal `"DEPOR"` para los campos hoy hardcodeados) — sin fila configurada, el
XML sale idéntico al que sale hoy. Con una fila, la reemplaza.

## Fuera de alcance

- Fechas de Order (ver arriba).
- Cualquier cambio al camino WMS→SAP (`WmsSapIntegration.Service`, fuera de este
  repo) — los 6 `MapperKey` de confirmación existentes no se tocan.
- Validación en la UI de que el `FieldName` tipeado coincide con un campo XML real
  soportado por `WmsCloudConnector` — si el admin tipea un `FieldName` que no
  corresponde a ningún campo mapeable, esa fila simplemente no tiene efecto (no hay
  ningún `CampoXml(...)` que la busque). No se agrega validación cruzada en esta
  entrega.

## Testing

- Unit tests de `WmsFieldTemplateResolver` (sustitución simple, template con
  literales alrededor, template sin placeholders, campo ausente → vacío).
- Extender `WmsCloudConnectorTests.cs` (ya usa un `HttpMessageHandler` fake): un test
  confirmando que sin mapeos configurados el XML sale igual que hoy (regresión), y
  otro con una fila `SAPWMS_ITEM`/`item_alternate_code` configurada confirmando que el
  XML enviado usa el valor resuelto por el template en vez del default.
