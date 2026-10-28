# Módulo Facturas de Proveedor — Etapa 1: aprobación y rechazo ante el SII

**Fecha:** 2026-10-27 · **Estado:** diseño para revisión del usuario
**Antecede:** `Portal SaaS - Core/docs/superpowers/specs/2026-10-22-pasarela-documentos-electronicos-design.md`
(§6 "Recepción"). Este spec **activa la fase P2** de esa pasarela (recepción) y define el primer consumidor.
**Sigue:** Etapa 2, ingreso a SAP de las facturas aprobadas. Es otro módulo y tendrá su propio spec.
**Boceto de pantallas:** `mockups/2026-10-27-facturas-proveedor-boceto-v2.html`. Se abre en el navegador y es la
referencia visual aprobada (estructura de los módulos hermanos).

Convenciones del documento:
- **[validar]**: dato tributario o del operador que hay que confirmar antes de producción.
- **(diseñador)**: decisión del diseñador. El usuario no la tomó; está en §17 para revisión.

---

## 1. Problema

Hoy las facturas de proveedor se responden a mano:

1. La factura llega a FACELE.
2. Finanzas / pago a proveedores entra al portal de FACELE, busca en SAP si existe la OC (la referencia 801 de la
   factura es el `DocNum` de la OC en SAP), imprime la factura y consigue la firma de la jefatura que corresponde.
   La jefatura se identifica por la OC, por el proveedor o por la glosa.
3. Con esa firma, Finanzas acepta o rechaza en el portal de FACELE.
4. A fin de mes se exporta el libro de compras, se completan a mano OC, cuentas y centros de costo, y se arma una
   planilla de DataTransfer para cargar las facturas en SAP (eso lo resuelve la Etapa 2).

La ley da **8 días corridos desde la recepción en el SII** para reclamar una factura. Si nadie lo hace, la factura queda
aceptada sin posibilidad de reclamo (Ley 19.983, modificada por la Ley 20.956) **[validar con asesor]**. Con el proceso
manual, una factura mal emitida puede pasar sin reclamo solo porque se cumplió el plazo.

## 2. Solución en una frase

El portal lee de forma continua las facturas recibidas por el operador de documentos electrónicos de la compañía. Las
pasa por **reglas configurables por empresa, con excepciones por proveedor**, y las **acepta o rechaza ante el SII**.
Lo hace solo cuando una regla lo decide, o a través de una jefatura cuando el proveedor requiere revisión manual.
Todo queda registrado y listo para que la Etapa 2 las ingrese a SAP.

## 3. Decisiones del usuario (2026-10-27)

1. **Son dos módulos.** Este, de aprobación y rechazo, y otro, de ingreso a SAP. En el de SAP, el encargado indica si
   la factura se carga ahora o espera, y se controla su estado.
2. **Si la OC existe en SAP, ya hubo una aprobación válida.** Cuando exista el módulo de OC con flujo de aprobación, eso
   quedará formalizado. Una factura que referencia una OC **no requiere nueva aprobación**: solo se valida con reglas.
3. **Solo los gastos fijos mensuales sin OC pasan por revisión y aprobación manual** (luz, arriendo, etc.). También
   pasan los proveedores marcados como "requiere atención especial".
4. **Las reglas se configuran y activan en una pantalla.** El catálogo debe poder crecer con reglas nuevas.
5. **Hay una pantalla de proveedores**, con RUT tomado de SAP, para los que tienen excepciones a las reglas generales.
   Ejemplo: a un proveedor de gasto fijo no le aplican las reglas de OC, pero requiere aprobación manual.
6. **El plazo de respuesta (8 días) es un parámetro.**
7. **Al llegar al último día, la acción es una regla configurable: aceptar o rechazar.** Toda aceptación o rechazo
   sale de una regla o de una persona, nunca de un comportamiento implícito.
8. **La jefatura aprueba.** No estaba en el plan original, pero el usuario lo adopta como criterio de flujo.
9. **El operador de documentos es intercambiable por compañía.** Hoy DEPOR usa FACELE; mañana pueden ser Acepta, BES u
   otros. Las reglas son las mismas cualquiera sea el operador.
10. **Pantallas con el mismo diseño y estructura que los módulos hermanos.** No hay estilos propios.
11. **Reglas de ejemplo del usuario:**
    - una factura sin OC se avisa primero (se puede arreglar asociando la OC a mano) y, si no se corrige, se rechaza;
    - una OC con más de 30 días se rechaza;
    - un monto mayor al de la OC se rechaza. Si la factura es parcial, se suma lo ya facturado y nunca puede superar
      el total de la OC;
    - las reglas de OC solo corren para proveedores que trabajan con OC.
12. **El saldo de la OC se compara por el total del documento.** La OC va con IVA o exenta, así que se compara siempre
    el total de la factura contra el total de la OC, nunca el neto.
13. **Las facturas cargadas hoy a SAP por DataTransfer no quedan relacionadas con la OC.** Por eso SAP no sirve, por
    ahora, para saber cuánto se facturó contra una OC (§7.5).

## 4. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Ninguna factura recibida se vence sin que una regla o una persona haya decidido.
- Las facturas que calzan con su OC se aceptan solas, sin intervención de nadie.
- Finanzas ve en una sola bandeja qué está pendiente, quién lo tiene y cuántos días quedan.
- Reglas, parámetros y excepciones se configuran sin programar. Una regla nueva es una pieza de código del catálogo y
  aparece sola en la pantalla para activarla.
- El módulo no sabe qué operador hay detrás.
- Hay trazabilidad legal: qué regla o qué persona decidió, cuándo, con qué motivo y qué respondió el operador.

**No-objetivos (esta etapa)**
- **Ingresar la factura a SAP:** queda para la Etapa 2.
- **Módulo de OC con flujo de aprobación:** es futuro. Por ahora, OC existente en SAP = aprobada.
- **Notas de crédito y débito recibidas (61/56):** no se aceptan ni se rechazan, porque el SII no las reclama por esta
  vía y FACELE solo acepta los tipos 33, 34 y 43 en `aprobarDTERecibidos`.
- **Acuse de recibo de mercaderías (ERM):** se deja fuera hasta que el asesor lo indique (§16).
- **Correos al proveedor** avisando el rechazo: el SII y el operador ya lo informan.
- **Adaptadores Acepta o BES:** el contrato queda listo, pero esta etapa construye solo FACELE.

**Anti-objetivos (sería un fracaso aunque funcione)**
- Aceptar o rechazar sin una regla activa (configurada por alguien y registrada) o sin una persona. Tiene efecto legal.
- Responder dos veces la misma factura, o responder distinto de lo decidido, por un reintento o un doble clic.
- Rechazar solas facturas antiguas que ya estaban en el operador al activar el módulo.
- Dejar vencer el plazo en silencio: todo lo pendiente debe verse y escalarse antes.
- Aceptar dos facturas contra el mismo saldo de una OC porque la primera aún no está en SAP.
- Un módulo con `if (operador == "FACELE")`, o que llame al operador directamente.
- Operar una compañía con la configuración de otra.
- Pantallas con layout propio, distinto al de los módulos hermanos.

## 5. Arquitectura

```
Modulo.FacturasProveedor (plugin, base propia por compañía)
  Motor de reglas (dominio puro, sin I/O, testeable)
     catálogo de reglas · perfiles de proveedor · precedencia · plazos
  Orquestador (BackgroundService del plugin, un ciclo por compañía)
     evalúa nuevas/pendientes · ejecuta vencimientos · envía respuestas · avisa
  Páginas: Bandeja · Detalle · Mis aprobaciones · Configuración
           │ IDteInboxService (PortalSaas.Abstractions)
           ▼
Plataforma: Documentos Electrónicos — Recepción (PortalSaas.Core)
  dte_received (bandeja de recibidos por compañía) · sincronizador (job)
  respuesta idempotente · referencias de OC (801 + asociadas a mano)
           │ IDteReceptionProvider
     ┌─────┴──────────┬──────────────┐
  FACELE (DTEService)  Acepta / BES (futuro)
           
Etapa 2 (otro plugin): lee de IDteInboxService las facturas aceptadas y marca su ingreso a SAP.
```

Fronteras:
- **La plataforma solo transporta y archiva:** trae los documentos, guarda el XML, responde al operador y recuerda lo
  respondido. No conoce reglas.
- **El plugin es dueño de la decisión:** reglas, excepciones, aprobadores, plazos y avisos.
- **Los dos módulos (aprobación y SAP) no se referencian entre sí:** los plugins solo ven `PortalSaas.Abstractions`.
  Comparten la bandeja de plataforma, que guarda lo que es un hecho del documento (estado de respuesta y OC
  referenciadas). *(diseñador)*

## 6. Plataforma: recepción (fase P2 de la pasarela)

### 6.1 Operador FACELE — `DTEService`

WSDL de prueba: `https://caoba.docele.cl/cl-dol/DTEService?wsdl` (revisado el 2026-10-27; aún no se ejecutaron llamadas).

| Operación | Uso |
|---|---|
| `consultar(rutAbonado, userMail, operacion=RECEPCION, filtros de fecha, estados, offset, limit, url)` | Listar recibidos. Cada registro trae RUT y razón social del emisor, tipo, folio, emisión, `fechaRegistroSII`, neto, IVA, exento, impuestos adicionales, total, `estadoSII`, `estadoLey19983`, `estadoLey20956`, `formaPago`, `url`, `cedido`, `anulado` |
| `consultarReferencia(rutAbonado, userMail, rutEmisor, tipoDTE, folio, operacion=RECEPCION)` | Documentos que la factura referencia (`referenciado[]`: tipo, folio, fecha, `codigoReferencia`, `razonReferencia`). Ahí debe venir la OC (tipo 801) **[validar en caoba]** |
| `aprobarDTERecibidos(rutAbonado, userMail, aprobacion[]{rutEmisor, tipoDTE 33\|34\|43, folio, accion})` | Respuesta ante el SII. `accion`: `Acepta_Contenido_Documento` (ACD), `Reclamo_Contenido_Documento` (RCD), `Reclamo_Falta_Parcial_Mercaderias` (RFP), `Reclamo_Falta_Total_Mercaderias` (RFT), `Otorga_Recibo_Mercaderias` (ERM). **No tiene campo de motivo** |
| `actualizarObservacionesDTE(rutAbonado, tipoDTE, folio, observacionesTXT)` | Dejar el motivo escrito. **No recibe RUT del emisor**: hay que confirmar que sirve para documentos recibidos **[validar]** |
| `marcarDTE(..., documento[], MARCAR\|DESMARCAR)` | Marca visible en el portal de FACELE. Opcional (ej. "procesada por el portal") |
| `consultarResumen(rutAbonado, operacion, ...)` | Totales por período (por recepcionar, por aprobar, aprobados, reclamados). Útil para conciliar |

El XML y el PDF del documento recibido no están en `DTEService`. Hay que obtenerlos con la `url` de `consultar` o con
`Obtener` de `DocumentosRecibidosService` (spec de plataforma §6) **[validar]**. Si no se pueden obtener, el módulo
igual funciona: las reglas usan cabecera y referencias, y solo se pierde la pestaña "Detalle del documento".

Autenticación: headers HTTP `facele.user` / `facele.pass`, igual que en la emisión. `userMail` es el correo del
usuario FACELE con que se opera **[validar si es por compañía o por persona]**.

### 6.2 Contrato del proveedor (`IDteReceptionProvider`, en Core)

Mismo estilo que `IDteProvider`: capacidades declaradas y nunca el nombre del proveedor.
- `ListReceivedAsync(ctx, ventana, offset)`
- `GetReferencesAsync(ctx, rutEmisor, tipo, folio)`
- `GetDocumentAsync(ctx, ..., xml|pdf|url)`
- `RespondAsync(ctx, rutEmisor, tipo, folio, acción)`
- `SetNoteAsync(...)`, si la capacidad lo permite
- `TestAsync`

Capacidades: `SupportsReception`, `RespondableTypes` (FACELE: 33, 34, 43), `SupportedActions`, `SupportsResponseNote`,
`SupportsDocumentDownload`. Acciones neutras: `AcceptContent`, `ClaimContent`, `ClaimPartialGoods`, `ClaimTotalGoods`
y `GoodsReceipt` (esta última no se usa en esta etapa).

### 6.3 Bandeja de plataforma `dte_received`

Tabla de plataforma con `company_id`. Una fila por documento, con llave única (compañía, RUT emisor, tipo, folio).

| Grupo | Columnas |
|---|---|
| Identidad | `provider_code`, `environment`, `issuer_tax_id`, `issuer_name`, `dte_type`, `folio`, `issue_date`, `sii_received_at` |
| Montos | `net_amount`, `exempt_amount`, `vat_amount`, `other_taxes_json`, `total_amount` |
| Estado del operador | `sii_status`, `commercial_status` (19.983), `claim_status` (20.956), `is_assigned` (cedido), `is_voided`, `provider_url` |
| Contenido | `references_json` (de `consultarReferencia` y del XML), `xml` (archivado, si se pudo obtener) |
| OC | `purchase_order_refs_json`: lista de `{doc_num, source: dte\|manual, user_id, at}` |
| Respuesta | `response_status` (`none` / `pending` / `sent` / `failed` / `external`), `response_action`, `response_reason`, `response_actor` (usuario o `rule:<código>`), `response_key`, `responded_at`, `response_attempts`, `last_error` |
| Etapa 2 | `processing_module`, `processing_state`, `processing_ref` (ej. DocEntry SAP), `processing_at` |
| Control | `first_seen_at`, `updated_at` (cursor para los módulos) |

Las OC asociadas a mano viven aquí y no en el plugin, porque son un hecho del documento que la Etapa 2 también
necesita. *(diseñador)*

### 6.4 Sincronizador (job de plataforma)

- Recorre las compañías con recepción habilitada (`company_dte_settings`, columnas nuevas `reception_enabled` y
  `reception_interval_minutes`), con `ICurrentCompanyOverride`, igual que `DteReconciliationHostedService`.
- En cada ciclo consulta una **ventana móvil** de días hacia atrás y hace upsert. No usa una marca única, porque un
  documento puede aparecer tarde en el operador. *(diseñador)*
- La **primera sincronización** de una compañía trae un historial más largo: lo que pida el módulo consumidor, con un
  mínimo de 60 días. Así el módulo de aprobación puede calcular el saldo de las OC (§7.5).
- En documentos nuevos pide las referencias y, si la capacidad existe, el XML.
- **Detecta respuestas hechas fuera del portal.** Si el estado del operador cambió y el portal no tenía respuesta
  enviada, marca `response_status = external`. Así el módulo deja de actuar sobre esa factura. Cubre a quien siga
  respondiendo en el portal de FACELE durante la transición.

### 6.5 Contrato para módulos (`IDteInboxService`, en Abstractions)

| Operación | Qué hace |
|---|---|
| `ListAsync(filtro)` / `ListChangedSinceAsync(cursor)` | Bandeja de la compañía de la sesión, o cambios desde un cursor (para jobs) |
| `GetAsync(id)` / `GetXmlAsync` / `GetPdfAsync` | Documento interpretado al modelo canónico `DteDocument`, más archivos |
| `GetCapabilitiesAsync()` | Tipos que se pueden responder, acciones y si soporta motivo. El módulo arma la pantalla de rechazo con esto |
| `RespondAsync(id, acción, motivo, actor, responseKey)` | **Idempotente.** Misma `responseKey` = mismo resultado. Si ya hay una respuesta enviada con otra clave, devuelve "ya respondida" y no llama al operador. Si el operador falla, queda `failed` y se puede reintentar con la misma clave |
| `SetPurchaseOrderRefsAsync(id, docNums, usuario)` | Asociar OC a mano. No borra las que vienen en el DTE |
| `MarkProcessingAsync(id, módulo, estado, ref)` | Para la Etapa 2 |

## 7. Motor de reglas (plugin)

### 7.1 Forma

Mismo patrón que las reglas de Importación Genérica (`IGenericImportValidationRule`) y que las condiciones del
tarifario de Despachos (`ISurchargeCondition`): cada regla es una clase registrada en DI, y el catálogo es el conjunto
registrado.

```
ISupplierInvoiceRule
  Code, Name, Description
  Scope               OcSuppliers | MarkedSuppliers | AllSuppliers | Pending (temporal)
  Parameters          definición de parámetros (nombre, tipo, valor por defecto, validación)
  AllowedActions      subconjunto de Reject | WarnThenReject(días) | ManualReview | Accept
  DefaultAction, DefaultActive
  Evaluate(InvoiceEvaluationContext, RuleSettings) → Pass | NotApplicable | Objection(acción, mensaje, datos)
```

- **`InvoiceEvaluationContext`** se arma una vez por ciclo con los datos ya traídos: factura, OC referenciadas y
  asociadas, datos SAP de esas OC, lo ya facturado contra cada OC, perfil del proveedor y fecha y hora Chile. Las
  consultas a SAP se hacen en lote, con el mismo criterio anti-N+1 de Importación Genérica. `Evaluate` no hace I/O.
- **La configuración por compañía** (`fp_rule_settings`) guarda, por código de regla, si está activa, la acción, los
  días de aviso y los parámetros. Una regla nueva del catálogo aparece en la pantalla con sus valores por defecto,
  **inactiva salvo que se defina lo contrario**.
- **Cada cambio de configuración queda registrado** (quién, cuándo, antes y después). Este registro es la "regla
  explícita aprobada" que exige la pasarela.

### 7.2 Catálogo inicial

| # | Código | Regla | Aplica a | Parámetros | Acciones permitidas | Por defecto |
|---|---|---|---|---|---|---|
| 1 | `oc-referenciada` | La factura referencia (801 o asociación manual) al menos una OC que existe en SAP | Proveedores con OC | — | Avisar y rechazar · Rechazar · Revisión | Avisar y rechazar en 3 días, activa |
| 2 | `oc-mismo-proveedor` | Toda OC referenciada pertenece al proveedor que emite (RUT del socio = RUT emisor) | Proveedores con OC | — | Rechazar · Revisión | Rechazar, activa *(diseñador)* |
| 3 | `oc-antiguedad` | Días entre la fecha de la OC y la emisión de la factura ≤ máximo | Proveedores con OC | máximo de días (30) | Rechazar · Revisión · Avisar y rechazar | Rechazar, activa |
| 4 | `oc-saldo` | Total de la factura ≤ saldo disponible (en total) de las OC referenciadas (§7.5) | Proveedores con OC | tolerancia % (0) y tolerancia en monto (0) | Rechazar · Revisión | Rechazar, activa |
| 5 | `oc-abierta` | Las OC no están cerradas ni canceladas en SAP | Proveedores con OC | — | Revisión · Rechazar | Revisión, **inactiva** *(diseñador)* |
| 6 | `proveedor-en-sap` | El RUT emisor existe como proveedor activo en SAP | Todos | — | Revisión · Rechazar | Revisión, activa *(diseñador)* |
| 7 | `revision-manual-proveedor` | El proveedor está marcado como gasto fijo o como "siempre revisión manual" | Proveedores marcados | — | Revisión | Siempre activa (no se desactiva) |
| 8 | `plazo-por-vencer` | Factura pendiente (en revisión o en aviso) cuando faltan N días para el fin del plazo | Pendientes (temporal) | días antes (0 = último día), hora de corte (10:00) | Aceptar · Rechazar | Rechazar, activa |

Parámetro general **"Si ninguna regla objeta"**: Aceptar (por defecto) o Revisión manual. Así, la aceptación
automática también es una decisión configurada.

### 7.3 Perfil del proveedor y excepciones

Todo proveedor es **"con OC"** salvo que figure en *Proveedores con excepciones* con una de estas condiciones:

| Condición | Efecto |
|---|---|
| **Gasto fijo mensual** | Reglas 1 a 5 = no aplica. Regla 7 lo envía a revisión manual del aprobador asignado |
| **Siempre revisión manual** | Se evalúan todas las reglas, pero **ninguna responde sola**: sus objeciones se muestran al aprobador como recomendación y la decisión es humana. La regla 8 sí actúa al vencer el plazo, para no perderlo *(diseñador)* |
| **Personalizada** | Ajuste regla por regla: "como la empresa", "no aplica", otro parámetro u otra acción |

Toda excepción tiene aprobador (grupo), suplente opcional, vigencia (desde / hasta opcional), nota y estado. La regla 8
también se puede ajustar por proveedor (ej. aceptar al vencer para un servicio básico).

Si una factura cae en revisión y su proveedor no tiene aprobador asignado (ej. regla 6), va al grupo de Finanzas (§9).

### 7.4 Decisión

1. Se aplican las excepciones del proveedor y se descartan las reglas que no aplican.
2. Se evalúan las reglas activas (salvo la 8, que es temporal).
3. Si hay objeciones, gana la más severa: **Rechazar > Avisar y rechazar > Revisión manual**. El motivo que se informa
   es el de la regla ganadora; las demás quedan en el detalle.
4. Si no hay objeciones, se aplica el parámetro "Si ninguna regla objeta".
5. En proveedores "siempre revisión manual", el resultado de 3 o 4 se convierte en *recomendación* y la factura va a
   revisión.

**Avisar y rechazar** deja la factura **en aviso** hasta `fecha de recepción en el SII + N días`. Mientras está en
aviso, Finanzas puede **asociar una OC** (por `DocNum`). Con eso se reevalúa al instante. Si se cumple la fecha y nada
cambió, se rechaza. La configuración valida que N sea menor que el plazo legal.

**Reevaluación:** toda factura sin respuesta enviada se reevalúa en cada ciclo, para tomar cambios en SAP (ej. la OC
se creó después) o en la configuración. Una factura con respuesta enviada no se reevalúa nunca.

### 7.5 Saldo de la OC (regla 4)

- **Base de comparación: total del documento** (decisión del usuario, §3.12). Se compara el total de la factura
  (`MntTotal`: neto + exento + IVA + impuestos adicionales) contra el total de la OC en moneda local (`DocTotal`). La OC
  puede ser afecta o exenta, y en ambos casos su total es lo comprometido. La tolerancia (% o monto) absorbe diferencias
  de redondeo.
- **Saldo** = total de la OC − ya facturado contra ella.
- **Ya facturado contra la OC.** Como hoy las facturas cargadas por DataTransfer no quedan vinculadas a la OC en SAP
  (§3.13), **la fuente es la bandeja de recibidos y no SAP**:
  - cuenta toda factura recibida que referencia esa OC (801 o asociación manual) y que quedó **aceptada**: por el portal,
    fuera del portal (en FACELE, durante la simulación) o tácitamente por vencimiento del plazo;
  - también cuenta la que ya está decidida para aceptar y espera envío (`por_enviar`);
  - suma sus totales;
  - las rechazadas no cuentan.
- **Cobertura del historial.** La primera sincronización debe traer, como mínimo, *antigüedad máxima de OC (regla 3) +
  plazo legal* días hacia atrás (con los valores por defecto, 38 días; se sugieren 60).
  - Como la regla 3 rechaza las OC más antiguas que ese máximo, toda factura anterior que pueda consumir el saldo de una
    OC todavía vigente queda dentro de la bandeja.
  - Si el máximo de la regla 3 se amplía (o se desactiva para un proveedor), la pantalla advierte que las facturas
    anteriores a la ventana cargada no se cuentan.
- **SAP como fuente complementaria (opcional).** La consulta `facturado-por-orden-compra` (§7.6) suma las facturas de
  proveedor que en SAP sí están vinculadas a la OC. Esas son las que creará la Etapa 2 con referencia a la OC.
  - Se **deduplica por RUT del proveedor + folio** contra lo que ya cuenta la bandeja.
  - Mientras no exista la Etapa 2, la consulta puede quedar vacía sin afectar el cálculo.
- **Orden dentro de un ciclo.** Las facturas de una misma OC se evalúan en orden de recepción en el SII, y cada una
  descuenta del saldo a las anteriores aceptadas en ese ciclo. Así, dos facturas parciales que llegan juntas no se
  aceptan ambas si en conjunto superan la OC.
- **Decisión humana sobre una factura con OC** (proveedores "siempre revisión manual"): al aceptar, el saldo se
  recalcula en ese momento y se advierte si se supera.
- **Varias OC referenciadas:** el saldo es la suma de los saldos, y cada OC debe cumplir las reglas 1 a 3.
- **Mismo `DocNum` en más de una serie de OC:** la referencia es ambigua y la factura va a revisión manual con ese
  motivo. *(diseñador)*
- **Notas de crédito:** no se descuentan en esta etapa. El cálculo es conservador.

### 7.6 Consultas a SAP configurables

Mismo patrón que `sap_document_queries` de Despachos: SQL de solo lectura editable por compañía, con contrato de
columnas fijo y seeds HANA cargados la primera vez. Sirve para que Block (otra base SAP, otros UDF) se adapte sin
programar.

| Consulta | Parámetro | Columnas del contrato |
|---|---|---|
| `orden-compra-por-docnum` | `:doc_num` | `doc_entry`, `doc_num`, `series`, `card_code`, `supplier_tax_id`, `doc_date`, `total_local`, `currency`, `status` (open/closed/canceled) |
| `facturado-por-orden-compra` (opcional, §7.5) | `:doc_entry` | `supplier_tax_id`, `folio`, `total_amount_local`, `invoice_doc_entry` |

El proveedor por RUT se resuelve con `ISupplierCatalogService` de la plataforma.

## 8. Ciclo de vida de una factura (en el módulo)

```
                     ┌──────────── asociar OC / cambio en SAP ───────────┐
                     ▼                                                   │
nueva ──evaluar──▶ en_aviso ──vence aviso──▶ por_enviar(rechazo) ───────┤
   │                                                                     │
   ├──────────▶ en_revision ──decisión humana / regla 8──▶ por_enviar ──▶ aceptada | rechazada
   │
   └──────────▶ por_enviar (aceptar o rechazar por regla) ──────────────▶ aceptada | rechazada

modo simulación: en vez de por_enviar → simulada (no se envía; se compara con lo que pase en el operador)
siempre: respondida en el operador sin el portal → externa · plazo vencido sin respuesta → vencida (aceptada por ley)
anterior a "procesar desde" → fuera_de_alcance (solo se muestra)
```

- `por_enviar` → el job llama a `RespondAsync` con `responseKey = review_id + n° de decisión`. Si falla, reintenta
  con espera creciente. Si sigue fallando cuando falta poco para el plazo, avisa a Finanzas.
- Cada transición queda en la bitácora de la factura (`fp_review_events`) con usuario o regla, fecha y detalle.
- **Rechazo por regla:** la acción por defecto es `ClaimContent` (RCD). Una persona puede elegir falta parcial o total
  de mercaderías si el operador lo soporta. El motivo se envía como observación cuando la capacidad existe.

## 9. Revisión manual y aprobadores

- **Grupos de aprobadores:** son del módulo, con integrantes que son usuarios del portal (mismo criterio que los grupos
  de aprobación de Rendiciones). Cualquier integrante del grupo asignado, o del suplente, decide. Las jefaturas que hoy
  no son usuarios del portal se crean como usuarios, con acceso solo a este módulo.
- **Grupo de Finanzas:** un grupo marcado como "supervisión" (el área de pago a proveedores). Recibe escalamientos y
  avisos. Con el permiso correspondiente, puede decidir cualquier factura y asociar OC.
- **Mis aprobaciones:** muestra lo pendiente para los grupos del usuario conectado.
- **Decisión:**
  - **aceptar:** sin motivo obligatorio;
  - **rechazar:** motivo obligatorio y tipo de reclamo según las capacidades del operador. Se advierte que no se puede
    revertir.
- **Concurrencia:** si dos personas deciden a la vez, gana la primera (control de versión de fila) y la segunda ve el
  resultado.

## 10. Pantallas

Todas copian la estructura de una página hermana (regla "diseño/layout como módulos hermanos"): `card-surface`,
encabezado `h2` con acciones a la derecha, `filter-card`, `table-ps` dentro de `table-responsive-lg`, `badge-status`,
paneles laterales (`drawer-*`) para crear o editar, `doc-tabs` en el detalle. Páginas bajo
`Pages/FacturasProveedor/` (regla de colisión de rutas Razor), URLs internas con `~/`, responsivas a 375 px, y un
icono en cada ítem de menú.

| Ruta | Página | Hermana de referencia |
|---|---|---|
| `/facturas-proveedor/bandeja` | Bandeja: KPI, filtros (fechas, estado, texto), tabla con plazo, botón "Leer ahora", alerta de vencimientos | Despachos › Manifiestos |
| `/facturas-proveedor/bandeja/{id}` | Detalle: encabezado con estado y acciones (PDF, Aceptar, Rechazar, Asociar OC); pestañas Resumen · Evaluación de reglas · Detalle del documento · Historial | Despachos › Manifiesto (detalle) |
| `/facturas-proveedor/mis-aprobaciones` | Pendientes del usuario | Rendiciones › Aprobaciones |
| `/facturas-proveedor/configuracion/reglas` | Parámetros generales + tabla de reglas; edición en panel lateral | Despachos › Configuración |
| `/facturas-proveedor/configuracion/proveedores` | Proveedores con excepciones; buscador SAP en el panel | Despachos › Transportistas |
| `/facturas-proveedor/configuracion/aprobadores` | Grupos e integrantes; marca de grupo de Finanzas | Rendiciones › Grupos de aprobación |
| `/facturas-proveedor/configuracion/consultas-sap` | Consultas configurables con "probar" | Despachos › Consultas SAP |

Parámetros generales (en Reglas):
- plazo legal (8 días);
- escalar cuando falten (2 días);
- hora de corte para acciones del último día (10:00, hora Chile);
- "si ninguna regla objeta";
- procesar desde (fecha);
- modo (Simulación / Activo);
- aviso diario por correo a aprobadores (sí/no).

La frecuencia de lectura del operador es de plataforma (Administración › Documentos electrónicos) y aquí solo se
muestra.

## 11. Datos del módulo (base propia por compañía)

Base externa del módulo (`RequiresExternalDatabase = true`), resuelta con `IExternalDatabaseConnectionService`, motor
dual (Postgres / SQL Server), igual que Despachos.

| Tabla | Contenido |
|---|---|
| `fp_settings` | Parámetros generales (una fila) |
| `fp_rule_settings` | Por código de regla: activa, acción, días de aviso, `parameters_json`, orden, auditoría |
| `fp_config_events` | Historial de cambios de configuración (antes y después, usuario, fecha) |
| `fp_supplier_profiles` | `card_code`, `tax_id`, nombre (copia), condición, grupo aprobador, suplente, vigencia, nota, activo |
| `fp_supplier_rule_overrides` | Por perfil y regla: modo (no aplica / parámetros / acción) y valores |
| `fp_approver_groups`, `fp_approver_group_members` | Grupos (con marca de Finanzas) y usuarios |
| `fp_invoice_reviews` | Una por documento de `dte_received`: estado, fechas (recepción, fin de aviso, fin de plazo), acción propuesta y decidida, origen (regla o usuario), grupo asignado, motivo, simulada, intentos, último error, versión de fila |
| `fp_review_rule_results` | Resultado por regla y por evaluación (para la pestaña Evaluación) |
| `fp_review_events` | Bitácora |
| `fp_sap_queries` | Consultas SAP configurables (§7.6) |

Las fechas del plazo se calculan en **hora de Chile** (mismo criterio que `ChileClock` de Despachos).

## 12. Avisos

Por la campana (`INotificationService.TryEnqueueAsync`, regla de plataforma):
- **factura a revisión manual:** integrantes del grupo asignado;
- **factura en aviso por falta de OC:** grupo de Finanzas;
- **escalamiento** (faltan N días y sigue pendiente): grupo asignado y Finanzas;
- **falla al responder al operador, o factura respondida fuera del portal:** Finanzas.

Opcional (parámetro): correo diario a cada aprobador con sus pendientes, siguiendo el patrón del recordatorio diario
de Rendiciones.

## 13. Permisos

Por menú y acción, igual que Despachos (`HasActionAsync("FacturasProveedor.<menú>", acción)`):

| Menú | Ver | Editar |
|---|---|---|
| `bandeja` | ver todas las facturas | asociar OC, decidir cualquier factura (Finanzas) |
| `mis-aprobaciones` | ver y decidir lo de mis grupos | — |
| `reglas`, `proveedores`, `aprobadores`, `consultas-sap` | ver | crear, editar y eliminar |

## 14. Puesta en marcha

1. **Plataforma:** adaptador FACELE de recepción, `dte_received`, sincronizador e `IDteInboxService`. Migración de
   plataforma en las **4 bases** (regla de migraciones).
2. **Módulo en modo Simulación**, con "procesar desde" = fecha de inicio:
   - todo se evalúa y se muestra, pero nada se envía al SII;
   - Finanzas sigue respondiendo en FACELE y el sincronizador registra esas respuestas como `externa`;
   - la bandeja permite filtrar "simulada vs. lo que se respondió" para medir coincidencias y afinar reglas y
     excepciones.
3. **Paso a Activo:** decisión de Finanzas en la pantalla, que queda registrada.

## 15. Pruebas

- **Motor de reglas (dominio puro):**
  - cada regla con sus bordes;
  - precedencia;
  - excepciones por condición;
  - cálculo de saldo con facturas parciales, deduplicación portal/SAP, varias OC y DocNum ambiguo;
  - fechas de aviso y plazo en hora Chile, incluida la hora de corte.
- **Adaptador FACELE:** respuestas SOAP grabadas (consultar, referencia, aprobar, errores) y prueba E2E en caoba con
  documentos de prueba.
- **Plataforma:** `RespondAsync` idempotente (misma clave, otra clave, falla y reintento), detección de respuesta
  externa y upsert con la ventana móvil.
- **Plugin:**
  - pruebas de permisos por página (como `*PermisosTests` de Despachos);
  - pruebas guardianas existentes: `UrlsRelativasAlPathBaseTests`, `VistasResponsivasTests`, icono en menús;
  - ciclo del job con dos compañías y una sin conexión.

## 16. Preguntas abiertas

**FACELE**
1. ¿`consultarReferencia` devuelve las referencias 801 (OC) de las facturas recibidas?
2. ¿Cómo se obtienen el XML y el PDF de un recibido (`url` de `consultar` o `DocumentosRecibidosService.Obtener`)?
3. ¿`aprobarDTERecibidos` registra la acción en el SII (Registro de Reclamos) o solo en FACELE? ¿Qué devuelve si la
   factura ya fue respondida o el plazo venció?
4. ¿`actualizarObservacionesDTE` sirve para documentos recibidos (no recibe RUT del emisor)?
5. ¿`userMail` es un usuario técnico por compañía? ¿Hay límite de `limit` / paginación en `consultar`?
6. Credenciales de recepción en producción (canal seguro).

**Asesor tributario**
1. Plazo exacto: 8 días corridos desde la recepción en el SII. ¿Se cuenta el día de recepción?
2. ¿Corresponde otorgar también acuse de recibo de mercaderías (ERM) al aceptar?
3. Tipo de reclamo correcto para los rechazos por regla (contenido vs. falta de mercaderías).

**Administrador SAP**
1. ¿Hay más de una serie de OC con numeración que se pueda repetir?
2. Campo donde está el RUT del proveedor (normalmente `LicTradNum`) y su formato.

*Resuelto (2026-10-27): las facturas que hoy se cargan por DataTransfer no quedan vinculadas a la OC. Por eso, lo ya
facturado se toma de la bandeja de recibidos (§7.5).*

## 17. Decisiones del diseñador (revisar)

1. Los dos módulos se comunican solo a través de la bandeja de plataforma. Las OC asociadas a mano se guardan ahí.
2. "Siempre revisión manual": las reglas recomiendan y la persona decide; solo la regla de plazo actúa sola.
3. Regla 2 (OC del mismo proveedor) y regla 6 (proveedor existe en SAP), agregadas al catálogo; regla 5 (OC abierta)
   queda inactiva.
4. Lo ya facturado contra una OC sale de la bandeja de recibidos (aceptadas por el portal, en FACELE o por plazo),
   con una primera carga de al menos 60 días. SAP queda como fuente opcional, deduplicada por RUT + folio, y las notas
   de crédito quedan fuera.
5. DocNum ambiguo → revisión manual.
6. Modo Simulación y "procesar desde" como requisito de la puesta en marcha.
7. Sincronización por ventana móvil y detección de respuestas hechas fuera del portal.
8. Grupos de aprobadores propios del módulo, con un grupo de Finanzas de supervisión.
9. Hora de corte de las acciones del último día (10:00) para tener margen ante caídas del operador.

## 18. Riesgos

| Riesgo | Mitigación |
|---|---|
| Rechazo masivo por una regla mal configurada | Simulación previa; precedencia visible en el detalle; auditoría de configuración |
| Caída del operador el último día | Escalamiento anticipado, hora de corte temprana, reintentos y aviso a Finanzas |
| Saldo de OC mal calculado porque las facturas cargadas por DataTransfer no están vinculadas a la OC | Lo facturado sale de la bandeja de recibidos, con una primera carga ≥ antigüedad máxima de OC + plazo (§7.5) |
| Factura que consumió saldo de una OC pero llegó sin referencia 801 y nadie la asoció | Se reconoce el límite: solo cuenta lo que referencia la OC. "Asociar OC" está disponible también en facturas ya aceptadas, solo para el cálculo del saldo |
| La referencia 801 no viene en `consultarReferencia` | Leerla del XML; si tampoco, "Asociar OC" manual |
| Doble respuesta (portal y alguien en FACELE) | Detección de respuesta externa; `RespondAsync` idempotente |
| Interpretación legal del plazo y del tipo de reclamo | Puntos [validar] cerrados con el asesor antes de pasar a Activo |

## 19. Se deja al constructor

- Nombres de clases, DTO y servicios, y la división en proyectos, siguiendo la convención de los plugins existentes
  (`Modulo.FacturasProveedor`, `ModuleCode = "FacturasProveedor"`).
- Frecuencia del ciclo del job del módulo (sugerido 5 minutos) y política de reintentos.
- Tamaño de la ventana móvil del sincronizador.
- Forma exacta del `parameters_json` y su validación.
- KPI de la bandeja y su cálculo.
- Si `marcarDTE` se usa para marcar en FACELE las facturas procesadas.
