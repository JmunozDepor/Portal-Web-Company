# Pasarela de Documentos Electrónicos (DTE) de plataforma — Diseño

Fecha: 2026-10-22
Estado: diseño (sin código). Primer consumidor: Despachos Etapa 2 (guías Res. 154),
ver `Portal SaaS - Plugins/Modulo.Despachos/docs/superpowers/specs/2026-10-22-modulo-despachos-etapa2-guias-res154-design.md`.

## 1. Problema

- **Varios módulos van a emitir DTE.** Despachos parte con la guía 52. Después vendrán facturas, notas de crédito y de débito, y posiblemente otros documentos desde otros módulos.
- **Varios módulos van a recibir DTE.** El siguiente proyecto es traer las facturas de proveedor desde el intermediario e inyectarlas en SAP.
- **El intermediario depende de la compañía:** FACELE para DEPOR, BES para Block y otros a futuro. Cada compañía tiene su propia base, y el usuario cambia de compañía para operar con uno u otro.

Si cada módulo arma su XML y llama a FACELE, cada módulo repite el protocolo, la idempotencia, la recuperación ante caídas y las credenciales. Además, cambiar o agregar un proveedor obliga a tocar todos los módulos.

## 2. Solución en una frase

Una **capacidad de plataforma "Documentos Electrónicos"** con dos direcciones, **Emisión** y **Recepción**:
- contratos en `PortalSaas.Abstractions`, sobre un modelo canónico del DTE del SII;
- un registro central de emisiones y una bandeja de recibidos;
- un adaptador por proveedor, resuelto siempre por la compañía de la sesión.

Los módulos hablan "DTE del SII" y nunca "FACELE".

## 3. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Emitir cualquier tipo de DTE: 33, 34, 46, 52, 56, 61 y, más adelante, 39/41 y exportación. Habilitar un tipo nuevo no requiere cambiar contratos ni adaptadores; se agregan reglas y el módulo que lo usa.
- Recibir DTE de proveedores en una bandeja por compañía y responderlos: aceptar, aceptar con discrepancia, rechazar, acusar recibo de mercaderías.
- Que agregar un proveedor sea agregar un adaptador más su configuración, sin tocar los módulos.
- No emitir dos veces lo mismo, aunque haya un reintento, un doble clic o una caída a mitad de la emisión.
- Mantener trazabilidad legal: XML emitido o recibido, folio, proveedor, RUT, quién y cuándo.

**No-objetivos (por ahora)**
- Que el portal administre los CAF y firme los documentos. Los proveedores actuales asignan el folio y firman; el modelo deja la puerta abierta mediante una capacidad declarada (§5.3).
- Reemplazar la emisión que hoy hace SAP con su add-on. Ver regla de canal único, §5.6.
- El libro de compra y venta (RCV) y la declaración: siguen en el SII y el proveedor.

**Anti-objetivos (sería un fracaso aunque funcione)**
- Un módulo con `if (proveedor == "FACELE")` o que arme el XML del proveedor.
- Un mismo documento SAP emitido por dos canales (portal y add-on SAP).
- Acuse de recibo o aceptación automática de facturas de proveedor sin una decisión humana o una regla explícita aprobada. Tiene efectos legales (Ley 19.983).
- Credenciales del proveedor en el repositorio, en logs o en pantalla.
- Que una compañía emita o lea con la configuración de otra.

## 4. Arquitectura

```
 Módulos emisores                        Módulos receptores
 (Despachos, Ventas, NC, …)              (Compras: facturas de proveedor → SAP)
        │ IDteEmissionService                    │ IDteInboxService
        ▼                                        ▼
┌──────────────────────── Plataforma: Documentos Electrónicos ────────────────────────┐
│ Modelo canónico DTE (SII)  ·  Reglas por tipo (pura, testeable)                     │
│ Registro de emisiones (dte_emissions)     Bandeja de recibidos (dte_received)       │
│ Reconciliador de emisiones (job)          Sincronizador de recibidos (job)          │
│ Avisos a módulos (IDteEmissionListener) + campana (TryEnqueueAsync)                 │
│ Resolución de proveedor por compañía + credenciales cifradas                        │
└───────────────────────────────┬─────────────────────────────────────────────────────┘
                                │ IDteProvider (emisión) · IDteReceptionProvider (recepción)
                  ┌─────────────┼──────────────┐
           Adaptador FACELE  Adaptador BES  Adaptador N
```

- **Contratos y modelo canónico:** `PortalSaas.Abstractions` (lo único que ven los plugins).
- **Orquestación, registro, bandeja y jobs:** `PortalSaas.Core`, igual que los demás servicios de plataforma.
- **Adaptadores:** un proyecto por proveedor (`PortalSaas.Dte.Facele`, `PortalSaas.Dte.Bes`). Implementan las interfaces de proveedor y se registran por código (`"FACELE"`, `"BES"`). *Decisión del diseñador:* no son plugins, porque no tienen páginas ni menú y necesitan acceso a credenciales de plataforma.

## 5. Emisión

### 5.1 Modelo canónico (`DteDocument`)

Es un espejo de la estructura del DTE del SII (formato 2.5), sin datos que dependan del proveedor:

| Bloque | Contenido |
|---|---|
| `IdDoc` | Tipo DTE, fecha de emisión, `IndTraslado`, `TipoDespacho`, forma de pago, vencimiento, indicadores de servicio y montos brutos |
| `Emisor` | **No lo informa el módulo.** Lo completa la plataforma desde la configuración de la compañía: RUT, razón social, giro, Acteco, dirección y sucursal SII. El módulo solo puede elegir la sucursal o dirección de origen |
| `Receptor` | RUT, razón social, giro, dirección, comuna, ciudad, contacto |
| `Transporte` | Res. 154: patente, carro, RUT transportista, chofer, destino, `Aduana` (peso, bultos), fecha y hora de salida, fecha de llegada |
| `Totales` | Neto, exento, IVA, tasa, impuestos adicionales, total; los calcula la plataforma o los valida contra el detalle |
| `Detalle[]` | Línea, códigos de ítem, nombre, descripción, cantidad, unidad, precio, descuento o recargo, exento, impuesto adicional, monto |
| `DscRcgGlobal[]` | Descuentos y recargos globales |
| `Referencia[]` | Tipo, folio y fecha del documento referido, `CodRef` (1 anula, 2 corrige texto, 3 corrige montos), razón; también referencias a OC, HES, etc. |
| Extras | Campos libres o adicionales del PDF (ej. N° de manifiesto) como lista clave-valor. Cada adaptador los mapea si su proveedor los soporta (FACELE: `A0..A10`) |

### 5.2 Reglas por tipo (capa pura, en plataforma)

Validan el documento **antes** de enviarlo, porque el ambiente de pruebas de FACELE no exige los campos obligatorios de la Res. 154 (prueba 11 del spec de Despachos). Por eso la plataforma no confía en que el proveedor valide.

- **52:** transporte Res. 154 siempre completo, `IndTraslado` obligatorio y `TasaIVA` presente aunque los montos sean 0.
- **33/34/46:** si `TipoDespacho` es 1, 2 o 3, bloque transporte (con "SIN DATO" donde se permita; pendiente con FACELE).
- **56/61:** al menos una referencia al documento que modifican. Con `CodRef` 1, anula el total; con 2, solo texto y montos en 0; con 3, ajusta montos.
- Reglas comunes: RUT válidos, cuadratura de totales, largos y cantidad máxima de líneas y referencias del formato.

El resultado es una lista de errores legibles para el usuario. Es lo mismo que muestra la vista previa.

### 5.3 Contrato del proveedor (`IDteProvider`) y capacidades

Cada adaptador declara **capacidades** y la plataforma se comporta según ellas. Nunca pregunta por el nombre del proveedor.

| Capacidad | FACELE (probado en caoba) |
|---|---|
| Tipos DTE soportados | 33, 34, 39, 52, 56, 61, … |
| Quién asigna el folio | Proveedor |
| Emisión en dos fases (reserva folio → confirma) | Sí (`generaDTE` → `obtieneDTE`) |
| Clave anti-duplicado | Sí (`uuid`; si se repite, rechaza y devuelve el folio original) |
| Vista previa sin folio | Sí (`obtieneBorrador`; no valida esquema) |
| Anular antes de confirmar | Sí (`controversia anula/libera`) |
| Anular un documento emitido | **No** (se corrige con NC o, en guías, según lo que indique FACELE o el asesor) |
| Listar emisiones pendientes de confirmar | Sí (`sinConfirmar`) |
| Consultar estado SII | Sí (`Consultar` en DocumentosEmitidos) |
| PDF y XML del emitido | Sí (`obtieneDTE` / `Obtener`) |
| Recepción | Sí (DocumentosRecibidosService, §6) |

Operaciones del proveedor, neutras:
- `Preview`
- `Reserve` (en un proveedor de una sola fase equivale a emitir)
- `Confirm`
- `Release` / `VoidUnconfirmed`
- `ListUnconfirmed`
- `GetStatus`
- `GetDocument(pdf | xml | url, cedible, copias)`

### 5.4 Contrato para los módulos (`IDteEmissionService`)

| Operación | Qué hace |
|---|---|
| `ValidateAsync(doc)` | Reglas §5.2 y configuración de la compañía. Sin red |
| `PreviewAsync(doc)` | Validación + PDF borrador del proveedor (sin folio) |
| `EmitAsync(request)` | Emite de forma idempotente. `request` = `EmissionKey` (la genera el módulo y es estable), `SourceModule`, `SourceRef` (ej. `"manifest:123/guide:2"` o `"sap:13:45678"`), `DteDocument`, opciones de impresión. Devuelve estado, folio y glosa del proveedor |
| `GetAsync(emissionKey)` | Estado actual, folio, fechas, error |
| `GetPdfAsync(emissionKey, cedible, copias)` | PDF para imprimir o reimprimir |
| `GetXmlAsync(emissionKey)` | XML timbrado (archivado en el registro) |

Llamar dos veces a `EmitAsync` con la misma `EmissionKey` **nunca** genera dos documentos: devuelve la emisión existente. Si la clave es igual pero el contenido distinto, es un error, porque el módulo debe generar una clave nueva al reemitir.

### 5.5 Registro de emisiones y estados

Tabla de plataforma `dte_emissions`, con `company_id`, como el resto de la plataforma. *Decisión del diseñador:* el registro va en la plataforma y no en cada módulo, porque la recuperación ante caídas, la unicidad y el archivo legal son iguales para todos los documentos.

Columnas:
- `emission_key` (única por compañía), `source_module`, `source_ref`;
- `dte_type`, `provider_code`, `issuer_tax_id`, `environment` (certificación / producción);
- `folio`, `status`, `attempts`, `last_error`;
- `request_hash` (detecta "misma clave, distinto contenido"), `xml_sent`, `xml_signed`, `sii_status`;
- fechas por etapa y `created_by`.

```
pendiente ──Reserve──▶ folio_reservado ──Confirm──▶ emitida ──estado SII──▶ aceptada_sii
    │                       │                         │                      aceptada_con_reparos
    └─error──▶ fallida      └─Release/Void──▶ liberada └──────────────────▶ rechazada_sii
```

- **Reconciliador (job):**
  - toma las emisiones en `pendiente` o `folio_reservado` con más de N minutos y las cruza con `ListUnconfirmed` y con el error de uuid duplicado;
  - según el caso, las **confirma** (el módulo sigue queriendo el documento) o las **libera**;
  - nunca llama `Confirm` sobre un folio liberado o anulado, porque FACELE responde OK igual (prueba 7).
- **Estado SII (job):** consulta las `emitida` hasta llegar a un estado final.
- **Aviso a módulos:**
  - cada módulo registra un `IDteEmissionListener` por `SourceModule`; la plataforma le avisa los cambios de estado (ej. Despachos marca la guía como rechazada);
  - además, campana a quien emitió en caso de rechazo o error (regla de plataforma `TryEnqueueAsync`).
- El módulo guarda solo su vínculo de negocio. Ej.: `transport_guides.emission_key` más su estado de negocio. **Esto ajusta el spec de Despachos §5.3:** la idempotencia, el folio y el XML viven en `dte_emissions`.

### 5.6 Regla de canal único

Un documento de origen se emite por **un solo canal**. Hoy las facturas de Comercial las emite SAP con su add-on hacia FACELE.

Si mañana un módulo del portal emite facturas o NC de documentos SAP, `source_ref` = `sap:<ObjType>:<DocEntry>` es único en el registro, y la configuración por compañía y tipo DTE dice qué canal es el dueño ("portal" o "externo"). Si el portal no es el dueño de ese tipo, rechaza la emisión con un mensaje claro.

Al emitir un documento de un objeto SAP, la plataforma escribe el folio de vuelta en SAP, en el campo que defina el administrador SAP (§9). Así SAP no lo reemite.

## 6. Recepción (facturas de proveedor) — modelo preliminar

FACELE expone `DocumentosRecibidosService` (WSDL revisado en caoba; aún no se ejecutaron consultas):

| Operación | Datos |
|---|---|
| `Consultar(rutContribuyente, rutEmisor?, tipoDTE?, folioDTE?, periodo/fecha de emisión o de recepción, offset)` | Registros: RUT y razón social del emisor, tipo, folio, fechas de emisión y recepción, monto total, `estadoRecepcion` (Pendiente / OK / Rechazada), `estadoAprobacion`, `acuseRecibo` |
| `Obtener(rutContribuyente, rutEmisor, tipoDTE, folioDTE, PDF\|XML\|URL_PDF)` | XML o PDF del documento recibido |
| `Aprobar(..., aprobacionComercial = Aprobado \| Aprobado_Con_Discrepancia \| Rechazado, motivo, acuseRecibo, recinto, nombreAprobador)` | Respuesta comercial y acuse de recibo de mercaderías (Ley 19.983) |

**Contrato del proveedor:** `IDteReceptionProvider` con `ListReceived(desde, offset)`, `GetReceived(pdf|xml)` y `Respond(respuesta comercial, acuse)`. La capacidad `SupportsReception` permite que una compañía tenga emisión y no recepción (o al revés).

**Bandeja de plataforma (`dte_received`):**
- una fila por documento recibido, con llave única (compañía, RUT emisor, tipo, folio);
- el XML se descarga y archiva, y se interpreta al mismo modelo canónico `DteDocument` (es el mismo formato del SII);
- estados del proveedor y estado de respuesta (pendiente / aceptado / con discrepancia / rechazado / acuse);
- fecha límite de reclamo: 8 días corridos desde la recepción en el SII, Ley 19.983 [validar con el asesor];
- el sincronizador (job) recorre cada compañía con recepción habilitada, pide lo recibido desde la última marca y avisa de lo nuevo.

**Contrato para módulos (`IDteInboxService`):**
- listar y filtrar la bandeja;
- obtener el documento interpretado, el XML y el PDF;
- responder (aceptar, rechazar, acusar recibo), con usuario y motivo registrados;
- marcar el procesamiento del módulo (`procesando` / `contabilizado` con referencia SAP / `descartado`).

**El módulo Compras (proyecto siguiente)** consume la bandeja y es dueño de la lógica de negocio. La plataforma solo entrega documentos.
- **Proveedor por RUT:** ya existe `ISupplierCatalogService`.
- **Artículos:** `IItemCrossReferenceService`, código del proveedor ↔ artículo SAP.
- **Calce con OC o entrada de mercadería SAP:** referencias 801 / HES del DTE, montos y tolerancias.
- **Inyección en SAP:** vía `IPurchaseDocumentService`. Hoy ese servicio solo tiene cotización y orden de compra, así que hay que **agregar factura de proveedor**, y posiblemente borrador.
- **Respuesta comercial dentro del plazo legal.**

Esto se diseña en su propio spec. Aquí solo se fija que la bandeja y el contrato de recepción lo soporten.

## 7. Configuración y aislamiento por compañía

- **Proveedor resuelto por la compañía de la sesión:** `ICurrentCompanyAccessor`. Los jobs recorren las compañías de forma explícita con `ICurrentCompanyOverride`. Nunca hay selección manual de proveedor ni respaldo hacia otro.
- **Conexión al proveedor:** se reutiliza el catálogo `company_external_connections` (`Kind = HttpApi`, URL base y credenciales cifradas de solo escritura) con un binding del módulo de plataforma `dte` y propósitos `Emision` / `Recepcion`. No se crea un almacén de secretos nuevo.
- **Configuración DTE por compañía** (tabla nueva, pantalla en Administración):
  - código de proveedor y ambiente;
  - datos del emisor (RUT, razón social, giro, Acteco, direcciones o sucursales SII);
  - tipos habilitados con su canal dueño (§5.6);
  - recepción habilitada y su frecuencia.
- **Compañía sin configuración completa:** la vista previa y la emisión fallan con un mensaje claro.
- **Trazabilidad:** cada emisión y cada recibido guarda el proveedor, el RUT y el ambiente con que se operó.
- **Ambientes:**
  - en desarrollo y QA, certificación (caoba);
  - en producción, producción;
  - un documento de certificación nunca se marca como emitido en producción.

## 8. Fases

| Fase | Contenido | Consumidor |
|---|---|---|
| **P0** (junto a Despachos Fase A, urgente 1-nov) | Contratos, modelo canónico, reglas 52, `dte_emissions` + reconciliador, adaptador FACELE (emisión), configuración por compañía | Guías 52 de Despachos |
| P1 | Estado SII (job) y aviso a módulos; reglas 33/56/61; regla de canal único y escritura del folio en SAP | El próximo módulo que emita (NC, factura) |
| P2 | Adaptador FACELE de recepción + bandeja `dte_received` + sincronizador + respuesta comercial | — |
| P3 | Módulo Compras: calce y factura de proveedor en SAP (spec propio) | Compras |
| P4 | Adaptador BES (emisión y recepción) para Block | Block |

P0 solo implementa lo que usa la guía 52, pero con los contratos completos de §5. Así P1 no cambia firmas.

## 9. Preguntas abiertas

**Usuario**
1. ¿Qué módulo será el próximo en emitir facturas o NC desde el portal y desde qué origen (documento SAP existente o documento creado en el portal)? Define la prioridad de P1 y si aplica la escritura de folio en SAP.
2. Facturas de proveedor:
   - ¿quién decide aceptar, rechazar o acusar recibo?;
   - ¿se inyectan en SAP como borrador para revisión o como definitivas cuando calzan con la OC o la entrada?
3. ¿BES también entrega documentos recibidos por servicio web? Hay que pedir su manual.

**Administrador SAP**
- Campo donde se guarda el folio y el estado DTE de un documento emitido por el portal.
- Cómo SAP distingue los documentos que emite el portal de los que emite el add-on.

**Asesor tributario**
- Plazo y efectos del acuse de recibo y del reclamo.
- Cómo corregir o anular una guía 52 ya emitida.

**FACELE**
- Además de lo pendiente en el spec de Despachos §13:
  - ¿la recepción usa las mismas credenciales que la emisión?;
  - ¿`Consultar` de recibidos pagina con `offset` de a cuántos registros?;
  - ¿`Aprobar` registra el reclamo en el SII o solo la respuesta comercial?

## 10. Riesgos

- **Plazo P0:** el contrato completo se diseña ahora, pero solo se implementa lo que usa la guía 52.
- **El ambiente de certificación valida menos que producción:** las reglas propias de la plataforma (§5.2) mitigan el riesgo.
- **Emisión duplicada entre el portal y el add-on SAP:** la regla de canal único (§5.6) lo evita.
- **Recepción con efectos legales:** nunca se responde de forma automática sin una regla aprobada.
