# Módulo Despachos — Etapa 2: Guías de traslado Res. Ex. SII N°154

**Fecha:** 2026-10-22 · **Estado:** diseño aprobado en conversación, pendiente de revisión con asesor tributario y administrador SAP
**Antecede:** `2026-10-20-modulo-despachos-etapa1-design.md` (manifiestos, maestros, tarifario, snapshot Res.154)

Los puntos marcados **[validar]** son interpretación tributaria que debe confirmar el asesor antes de emitir en producción.

---

## 1. Problema

Desde el **1-nov-2026** rige la Res. Ex. SII N°154: todo documento que ampare un traslado debe llevar
datos de transporte (origen y destino con comuna, chofer nombre + RUT, RUT del transportista, patente
del vehículo/carro, tipo de traslado, fecha/hora de salida, fecha de llegada) y un detalle con
cantidad / peso / volumen / unidad.

Hay dos casos y ambos tienen la misma causa: **el documento tributario se emite en el momento comercial,
y los datos de transporte recién existen al despachar**. Un DTE emitido no se modifica.

| Caso | Hoy | Problema |
|---|---|---|
| **Factura anticipada (33)** | Comercial la emite antes del despacho (exigencia B2B de los clientes) | No lleva datos de transporte |
| **Guía de traslado interno (52)** a sucursales | Add-on SAP "Guía de Despacho" (UDO `@INX_ODGC` / `@INX_DGC1`) agrupa las transferencias (`OWTR`, 1 transferencia = 1 LPN del WMS) hechas a una sucursal y emite **una** guía | Detalle **totalizado** en una línea (ej. "103 CONVERSE — 9"), sin datos de transporte, sin peso. Incumple |

La recepción en tienda **no** es parte del problema: la integración POS ↔ SAP la resuelve a nivel de
transferencia.

## 2. Solución en una frase

El **manifiesto (= un viaje)** emite, al despachar, las guías de despacho electrónicas que amparan lo que
realmente sube al camión — una por destino y tipo de traslado, con detalle por artículo y datos de
transporte — a través de una **pasarela de documentos electrónicos de plataforma** con proveedor
intercambiable por compañía (FACELE para DEPOR, BES para Block, otros a futuro).

## 3. Decisiones tomadas (usuario, 2026-10-22)

1. **Facturas:** se mantiene la factura anticipada; al despachar se emite una guía de traslado que la referencia.
2. **Traslados internos:** modelo **híbrido**:
   - Despacho arma el manifiesto cargando las transferencias (por LPN escaneado o agregando en bloque las pendientes de una sucursal).
   - Queda **abierta** la opción de un **borrador de despacho / preparación** armado antes (dentro del manifiesto o en una pantalla aparte, similar al add-on actual) que el manifiesto luego "llama" para agrupar esas transferencias. Se diseña para que quepa (§7.3) pero no se construye en la primera entrega de traslados.
3. **Escaneo de LPN: opcional** en esta etapa (no cambiar la forma actual de trabajo). Parámetro por compañía para volverlo obligatorio después.
4. **Recepción en tienda:** fuera de alcance (la resuelve el POS).
5. **Transición:** **no** se sigue llenando `@INX_ODGC` / `@INX_DGC1`. El add-on se retira cuando entre la Fase B.
6. **Prioridad:** urgente **facturas primero** (Fase A), luego guías de traslado (Fase B). Primer proveedor: **FACELE** (hay experiencia previa de integración).
7. **Proveedor por compañía:** **DEPOR → FACELE**, **Block → BES**. Cada compañía tiene su propia base SAP y su propia base del módulo; el usuario **cambia de compañía en el portal** para trabajar los despachos y emitir las guías de cada una (§5.4).

## 4. Objetivos, no-objetivos, anti-objetivos

**Objetivos**
- Emitir guías que cumplan la Res. 154 sin que logística digite datos que el manifiesto ya tiene.
- Proveedor de documentos electrónicos intercambiable por compañía sin tocar el módulo Despachos.
- Que la guía refleje lo que efectivamente se despacha (no lo planificado).

**No-objetivos (esta etapa)**
- Emitir facturas, notas de crédito o débito.
- Recepción en tienda.
- Validación obligatoria de cada bulto (queda como opción por compañía).

**Anti-objetivos (sería un fracaso aunque "funcione")**
- Emitir dos veces la misma guía por un reintento o un doble clic.
- Que un camión salga con guía que no corresponde a su carga (chofer, patente o documentos distintos).
- Que Despachos quede acoplado a FACELE (cambiar de proveedor obligaría a reescribir el módulo).
- Detalle totalizado como el add-on actual.

## 5. Arquitectura

```
Modulo.Despachos
  Planificador de guías (dominio puro, sin I/O, testeable)
     manifiesto → grupos → detalle consolidado → referencias → división por límites del formato
  Orquestador de emisión
     tabla transport_guides, estados, idempotencia, reintentos, impresión
            │  contrato canónico (PortalSaas.Abstractions)
            ▼
Pasarela de documentos electrónicos (plataforma)
  IElectronicDocumentGateway : Emitir · ConsultarEstado · ObtenerPdf · Anular (si el proveedor lo soporta)
  proveedor y credenciales por compañía (cifradas, ISecretoCifradoService / config de conector)
            │
     ┌──────┴───────┬──────────────┐
  Adaptador FACELE  Adaptador BES  Adaptador N
```

### 5.1 Pasarela (plataforma)

- **Contrato en `PortalSaas.Abstractions`**, para que la usen otros módulos más adelante.
- **Modelo canónico** basado en la estructura del DTE del SII (encabezado: emisor, receptor, totales, tipo de traslado; bloque transporte; detalle; referencias). Cada adaptador solo traduce y transporta.
- **Capacidades declaradas por adaptador:** quién asigna el folio (proveedor o el portal con CAF), emisión síncrona o asíncrona, si soporta anulación, si entrega PDF o solo XML. El orquestador se comporta según ellas, nunca preguntando "¿es FACELE?".
- **Idempotencia:** cada emisión lleva un identificador propio estable (`emission_key`, UUID generado al planificar). FACELE ya acepta un `uuid` anti-duplicado. Ante un timeout, primero se consulta por ese identificador antes de reintentar.
- **Configuración por compañía:** proveedor activo, ambiente (certificación / producción), credenciales cifradas (escritura sola, nunca se muestran; nunca en el repositorio ni en el chat).
- **Ubicación del adaptador FACELE:** referencia técnica conocida, `DoceleOL.generaDTE`, formato XML, folio asignado por FACELE. El detalle del contrato sale de la experiencia previa del equipo con FACELE.

### 5.3b Adaptador FACELE (DoceleOL) — protocolo

Fuente: "Manual WebService Emisión 2022 v6" de FACELE + WSDL del ambiente de pruebas
(`https://caoba.docele.cl/cl-dol-auth/DoceleOLService?wsdl`, con header `facele.user` / `facele.pass`;
la consulta de estado está en `.../cl-dol/DocumentosEmitidosService?wsdl`). Las credenciales van cifradas
en la configuración de la compañía (en desarrollo, user-secrets); nunca en el repositorio.

La emisión es en **dos pasos**, y el adaptador debe tratarlos como una sola emisión idempotente:

| Paso | Operación | Efecto |
|---|---|---|
| 1 | `generaDTE(rutEmisor, tipoDTE, formato=XML, xml, uuid)` | Valida el XML y **asigna el folio** (`folioDTE`). El documento aún **no está confirmado** |
| 2 | `obtieneDTE(rutEmisor, tipoDTE, folioDTE, formato=PDF\|XML\|URL_PDF, cedible, cantidad)` | **Confirma** la emisión y devuelve el PDF (base64 **decodificado dos veces**), el XML o una URL |
| Recuperación | `sinConfirmar(rutEmisor)` | Lista folios generados y no confirmados (ej. se cayó la red entre 1 y 2) |
| Recuperación | `controversia(..., operacion=libera\|anula\|confirma)` | Resuelve un folio no confirmado: liberar (reutilizable), anular o confirmar |
| Vista previa | `obtieneBorrador(rutEmisor, tipoDTE, formato, xml)` | **PDF de prueba sin emitir ni consumir folio** (está en el WSDL, no en el manual) → base de la vista previa de la pestaña Guías y de las pruebas del XML |
| Estado SII | `Consultar` (DocumentosEmitidosService) | `estadoSII`: DOK, RLV (reparos leves), RPR (reparos por realizar), DPR (por recibir), RCH (rechazado); `estadoRecepcion` del receptor |
| Reimpresión | `reimprimeDTE` / `obtieneDTE` | Nueva copia del PDF |

- `uuid` = `emission_key` de la guía planificada (FACELE no acepta dos documentos con el mismo uuid).
- Respuestas con `estadoOperacion = 0` traen la glosa del error en `descripcionOperacion` → se guarda en la guía y se muestra.
- Mapeo de estados: `generaDTE` ok → `enviada` (con folio); `obtieneDTE` ok → `aceptada` (emitida); `Consultar` RLV/RPR → `aceptada_con_reparos`, RCH → `rechazada`.
- Campos libres (`A0..A10`, formato `XML_OBS`): disponibles para datos no normados (ej. N° de manifiesto, transportista); requieren habilitación por FACELE en el PDF.

**XML a enviar:** el DTE en el formato estándar del SII (`<DTE version="1.0"><Documento>` con `Encabezado` /
`Detalle` / `Referencia`), **sin** `Folio` (se puede omitir o enviar 0), **sin** `TED`, `TmstFirma` ni firma:
FACELE los agrega. FACELE devuelve y guarda el XML en **ISO-8859-1**. Ejemplos probados en
`docs/facele/ejemplo-guia52-venta-res154.xml` y `docs/facele/ejemplo-guia52-traslado-interno-res154.xml`.

#### Resultados de pruebas en ambiente caoba (2026-10-22, RUT 76030680-0)

| # | Prueba | Resultado |
|---|---|---|
| 1 | Autenticación | Headers **HTTP** `facele.user` / `facele.pass`. Sin ellos → HTTP 405 "Autenticacion ha fallado" |
| 2 | `obtieneBorrador` guía 52 venta y traslado interno | OK: PDF con marca "Borrador", sin folio. **No valida esquema** (acepta tags inválidos y guía sin transporte): sirve para vista previa, no para validar |
| 3 | `generaDTE` → `obtieneDTE` (PDF / XML / URL_PDF) | OK: folios 103, 104, 105 emitidos con timbre PDF417 ("Ambiente Certificación") |
| 4 | Repetir `uuid` | Rechazado: "UUID [...] ya utilizado para DTE: [... Folio: 103 ...]" → **idempotencia confirmada**; el mensaje trae el folio original |
| 5 | Folio generado sin confirmar | Aparece en `sinConfirmar` hasta `obtieneDTE` |
| 6 | `controversia libera` | OK; el folio se **reutiliza** (el 104 liberado volvió a asignarse) |
| 7 | `controversia anula` sobre folio no confirmado | OK. **Ojo:** después, `obtieneDTE` sobre ese folio igual responde OK → el adaptador nunca debe llamar `obtieneDTE` sobre un folio anulado/liberado |
| 8 | `controversia anula` sobre folio **ya confirmado** | Falla ("Problemas liberando folio") → **no hay anulación por WS de una guía emitida** [validar mecanismo con FACELE/asesor] |
| 9 | Validación de esquema en `generaDTE` | Sí valida contra el XSD del SII y el error lista los elementos esperados |
| 10 | Campos Res. 154 en `Transporte` | Soportados, en este orden: `Patente`, `PatenteCarro`, `RUTTrans`, `Chofer(RUTChofer, NombreChofer)`, `DirDest`, `CmnaDest`, `CiudadDest`, `Aduana`, `FchSalida`, `HraSalida`, `FchLlegada`. **No existe `HraLlegada`** |
| 11 | Guía **sin** bloque `Transporte` | **Aceptada** → FACELE no exige la Res. 154; la validación la hace el planificador del portal |
| 12 | Peso / bultos | No hay campos de peso en `Detalle`; existen a nivel documento en `Transporte/Aduana` (`PesoBruto`, `CodUnidPesoBruto`, `TotBultos`, `TipoBultos`…) [validar si aplican a guías nacionales Res. 154] |
| 13 | Traslado interno con montos 0 | Exige `TasaIVA` válida aunque todo sea 0 (`MntNeto 0`, `TasaIVA 19`, `IVA 0`, `MntTotal 0`) |
| 14 | Tildes y Ñ | OK en el PDF |
| 15 | `obtieneDTE` cedible + cantidad 2 | OK: PDF de 2 páginas |
| 16 | `reimprimeDTE` | No aplica: "Impresión Automática no seteada" (usar `obtieneDTE` / `Obtener`) |
| 17 | `Consultar` estado de las guías emitidas | En caoba las guías del día **no aparecieron** en `DocumentosEmitidosService` (los 33/61 antiguos sí, con `estadoSII` ING "Por Aprobar SII") → estado SII no verificable en pruebas |
| 18 | **Plantilla PDF de la guía 52** | **No imprime ningún dato de transporte** (patente, carro, transportista, chofer, fechas/hora de salida y llegada) aunque van en el XML → **FACELE debe actualizar la plantilla** |

| 19 | Guía con `PatenteCarro` = "SIN DATO" y peso en `Aduana/PesoNeto` + `NombreTransp` | Aceptada (folio 107, liberado) |
| 20 | Factura 33 con `TipoDespacho 2` y transporte "SIN DATO" en `RUTTrans` / `RUTChofer` | **Rechazada**: "'SIN DATO' does not match pattern for RUTType" → contradice el manual Res. 154 de FACELE [aclarar con FACELE] |

#### Mapeo Res. 154 / Formato DTE 2.5 (manual FACELE "Cambios Normativos Formato DTE 2.5", v1.4, 19-03-2026)

Obligatoriedad en **guía de despacho (52): siempre**. En facturas 33/34/46: cuando `TipoDespacho` es 1, 2 o 3
(si no se conoce el dato, "SIN DATO" — ver prueba 20).

| Dato | XML (`Encabezado/Transporte`) | Fuente en el portal |
|---|---|---|
| Patente vehículo | `Patente` | Snapshot manifiesto `plate` |
| Patente carro / remolque | `PatenteCarro` (nuevo; "SIN DATO" si no hay carro) | `trailer_plate` o "SIN DATO" |
| RUT transportista | `RUTTrans` | `carrier_tax_id` |
| RUT y nombre chofer | `Chofer/RUTChofer`, `Chofer/NombreChofer` | `driver_tax_id`, `driver_name` |
| Dirección / comuna / ciudad destino | `DirDest`, `CmnaDest`, `CiudadDest` | Documento SAP (dirección de despacho) + comuna resuelta |
| Peso (y transportista) | `Aduana/PesoNeto` (+ `NombreTransp`), según ejemplo FACELE | Suma de peso de las líneas SAP (maestro de artículos) [validar unidad] |
| Fecha y hora de salida | `FchSalida`, `HraSalida` (nuevos) | `departed_at` (hora Chile) al despachar |
| Fecha de llegada | `FchLlegada` (nuevo; no existe hora de llegada) | `estimated_arrival_date` → pasa a ser **obligatoria** al despachar |
| Origen | `Emisor/DirOrigen`, `CmnaOrigen`, `CiudadOrigen` | Origen del manifiesto |

Otros cambios del formato 2.5: el indicador de traslado 7 pasa a llamarse "Devolución de mercaderías";
`UnmdItem` deja de ser obligatorio en guías (solo en exportación / agente retenedor); campos de manejo de
madera no aplican a DEPOR/Block. El ejemplo de FACELE usa `IndTraslado 2` y `TipoDespacho 2` para una guía de venta.

**Consecuencias para el diseño:**
- El manifiesto debe exigir **fecha de llegada estimada** (hoy opcional) y la vista previa debe validar
  todos los campos "siempre obligatorios" de la guía, porque el ambiente de pruebas de FACELE **no** los exige (prueba 11).
- La **factura anticipada** de Comercial también cae en la Res. 154 si informa `TipoDespacho` 1/2/3: debe ir con
  transporte "SIN DATO" (o sin `TipoDespacho`, a confirmar con el asesor) y la guía del manifiesto lleva los datos reales.
  Esto es del proceso de facturación SAP, fuera de Despachos, pero hay que avisarlo a quien mantiene esa integración.

Detalles de protocolo: la respuesta de `ConsultarResponse` usa el elemento mal escrito `respuestaOperacon`
(sin "i"); `obtieneDTE` devuelve `<XML>` y `Obtener` devuelve `<xml>` (mayúsculas distintas).

### 5.4 Aislamiento por compañía

- El proveedor se resuelve **siempre por la compañía de la sesión** (la misma que hoy elige la base SAP y la base del módulo vía `company_module_connections`). No hay selección manual de proveedor ni emisión cruzada: un manifiesto de DEPOR solo puede emitir por FACELE con el RUT emisor de DEPOR, y uno de Block solo por BES con el de Block.
- Emisor (RUT, razón social, giro, dirección), credenciales, ambiente, tipos de traslado y consultas SAP son **configuración por compañía**.
- Compañía sin proveedor configurado o con configuración incompleta → la vista previa lo muestra como error y no se puede despachar (mensaje claro, sin caer a otro proveedor).
- La guía guarda el proveedor y el RUT emisor con que se emitió (trazabilidad si la configuración cambia después).

### 5.2 Planificador de guías (Despachos)

Entrada: manifiesto con sus unidades de carga + datos de SAP. Salida: lista de **guías planificadas**.

- **Llave de agrupación:** manifiesto × RUT receptor × dirección destino × tipo de traslado.
- **Detalle:** consolidado por artículo dentro del grupo (código, descripción, cantidad, unidad, peso unitario/total). Nunca totalizado por marca.
- **Referencias:** a cada factura 33 que ampara (Fase A) **[validar: obligatoriedad y formato de la referencia]**.
- **División automática** cuando el grupo excede los límites de líneas de detalle o de referencias del formato DTE (límites a confirmar contra el formato vigente del SII y del proveedor).
- **Bloque transporte** desde el snapshot Res.154 del manifiesto (Etapa 1): RUT transportista, chofer, patente, carro, origen, fecha/hora de salida y llegada estimada.
- **Validaciones previas** (bloquean la emisión): comuna destino resuelta, RUT receptor y transportista válidos, artículo con peso (o regla definida para peso faltante), emisor configurado.

### 5.3 Orquestador y estados

Tabla `transport_guides` (por manifiesto): grupo, `emission_key`, proveedor, folio, tipo de traslado, estado,
intentos, último error, referencias a XML/PDF, fechas. Líneas en `transport_guide_lines` y referencias en
`transport_guide_references`.

```
planificada → enviada → aceptada
                      → aceptada_con_reparos
                      → rechazada            (corregir y reemitir)
aceptada → anulada / reemplazada               (cambio de chofer/vehículo, Fase D)
```

Todo cambio queda en la bitácora del manifiesto (`manifest_events`). Rechazos y errores avisan por la campana
(regla de plataforma: `TryEnqueueAsync`).

## 6. Flujo de despacho

1. **Borrador:** la pestaña nueva **Guías** muestra la vista previa ("se emitirán N guías") con sus errores, para corregir antes de que el camión espere.
2. **Despachar:** valida (Etapa 1) → planifica → emite cada guía → si todas quedan aceptadas, el manifiesto pasa a **despachado** e imprime manifiesto + guías (PDF del proveedor).
3. **Falla del proveedor:** el manifiesto queda **"Listo — guía pendiente"** (no despachado) con botón Reintentar. El camión no sale sin guía. Modo contingencia solo si la normativa lo permite **[validar]**.
4. **Emisión parcial** (algunas guías aceptadas y otras no): las aceptadas no se reemiten (idempotencia); se reintenta solo lo pendiente.
5. **Cambio de chofer o vehículo después de emitir:** la guía no se edita → anular o reemplazar y emitir nueva (Fase D) **[validar: mecanismo de anulación de guía 52]**.

## 7. Fases

### 7.1 Fase A — Facturas anticipadas (URGENTE)

- Pasarela de plataforma + adaptador FACELE (certificación primero, luego producción).
- Configuración de proveedor y credenciales por compañía.
- Nueva consulta SAP configurable **"líneas de la factura"** (contrato fijo: código, descripción, cantidad, unidad, peso) + datos de receptor completos (RUT, razón social, giro, dirección).
- Planificador, pestaña Guías con vista previa, emisión al despachar, impresión, estados, reintento.
- Tipo/indicador de traslado para "mercadería ya facturada" = parámetro por compañía **[validar]**.

### 7.2 Fase B — Traslados internos (transferencias / LPN)

- Nuevo tipo de unidad de carga **transferencia** (`OWTR`), identificada por LPN (1:1 hoy). Reemplaza en el flujo a la guía ya emitida del add-on.
- Consultas SAP configurables: **"transferencia por LPN"**, **"transferencias pendientes por sucursal"** y **"líneas de transferencia"**.
- El escáner distingue lo que lee: timbre PDF417 = factura; código con formato LPN (patrón configurable por compañía, ej. `CTBO…` / `ICBO…`) = transferencia.
- **Carga en bloque:** "agregar todas las pendientes de la sucursal X". Escaneo de LPN **opcional** (parámetro por compañía para hacerlo obligatorio a futuro).
- Guía de traslado interno (tipo 5, traslado interno) con receptor = la misma empresa y dirección destino = sucursal.
- **Escritura en SAP** (Service Layer) en la transferencia: folio de la guía, N° de manifiesto, fecha de despacho (UDF a crear) **[validar con administrador SAP: UDF y permisos de actualización en transferencias cerradas]**.
- Retiro del add-on "Guía de Despacho"; las series de transferencia dejan de emitir guía.

### 7.3 Abierto (no construir aún): borrador de despacho / preparación

Agrupación de transferencias por sucursal armada **antes** del despacho (por quien prepara la reposición),
que el manifiesto luego incorpora completa. Para no bloquearlo, la Fase B modela la unidad de carga de forma
que un grupo preparado sea simplemente "un conjunto de transferencias que se agregan juntas". Decidir más
adelante si vive en una pantalla aparte o como borrador dentro del manifiesto.

### 7.4 Fase C — Segundo proveedor (BES, compañía Block)

Adaptador BES contra el mismo contrato. Requiere su documentación técnica y aclarar si asigna folio o si el
portal debe administrar CAF.

### 7.5 Fase D — Robustez

Anulación/reemplazo ante cambios, conciliación de estado SII, reportes de guías emitidas.

## 8. Datos requeridos y su origen

| Dato | Fuente |
|---|---|
| Transporte (chofer, RUTs, patentes, horas) | Snapshot del manifiesto (Etapa 1) |
| Origen (dirección + comuna) y RUT emisor | Despachos > Orígenes / ajustes (hoy incompletos en QA) |
| Destino y receptor | Consulta SAP de la factura / transferencia, ampliada |
| Detalle por artículo con peso | Nuevas consultas SAP de líneas; peso desde el maestro de artículos |
| Tipo de traslado por caso | Parámetro por compañía **[validar]** |
| Credenciales del proveedor | Bóveda cifrada de la plataforma |

## 9. Temas para el asesor tributario

1. Indicador / tipo de traslado de la guía que ampara mercadería **ya facturada** (factura anticipada).
2. Si esa guía debe referenciar la factura 33 y cómo.
3. Si la guía puede tener valores en $0 (traslado interno y mercadería ya facturada).
4. Mecanismo correcto para anular o reemplazar una guía 52 emitida ante cambio de chofer o vehículo.
5. Existencia de modo contingencia si el proveedor no responde al momento de despachar.
6. Tratamiento de artículos sin peso informado.

## 10. Temas para el administrador SAP

1. UDF en transferencia (`OWTR`) para folio de guía, N° de manifiesto y fecha; permisos de actualización vía Service Layer.
2. Desactivar la emisión de guía en las series de transferencia a sucursales (al entrar la Fase B).
3. Peso informado en el maestro de artículos (cobertura actual).
4. Cómo identificar una transferencia por LPN y las "pendientes de despacho" por sucursal.

## 11. Riesgos

| Riesgo | Mitigación |
|---|---|
| Plazo 1-nov-2026 | Fase A primero y acotada; FACELE con experiencia previa; confirmar con asesor si hay tolerancia |
| Doble emisión | `emission_key` idempotente + consulta antes de reintentar |
| Peso faltante en artículos | Revisión temprana del maestro (§10.3) y regla definida (§9.6) |
| Caída del proveedor al despachar | Vista previa anticipada, reintento, contingencia si está permitida |
| Interpretación tributaria | Puntos [validar] cerrados antes de producción |

## 13. Pendiente con FACELE

Resuelto con las pruebas del 2026-10-22: estructura del XML, nodos que agrega FACELE, campos Res. 154 del
esquema, RUT de DEPOR habilitado en caoba.

1. **Actualizar la plantilla PDF de la guía 52** para imprimir el bloque de transporte (patente, patente
   carro, RUT transportista, chofer RUT y nombre, fecha/hora de salida, fecha de llegada). **Bloqueante.**
2. Cómo se anula una guía 52 **ya emitida** (no se puede por `controversia`).
3. Si para Res. 154 en guías nacionales corresponde informar peso/bultos en `Transporte/Aduana` y si su PDF lo imprime.
4. Por qué las guías emitidas en caoba no aparecen en `Consultar` (para probar el seguimiento de estado SII).
5. Habilitación de campos libres en el PDF de la guía (N° de manifiesto).
5b. "SIN DATO" en `RUTTrans` / `RUTChofer`: el manual lo indica para facturas, pero el esquema de caoba lo rechaza (prueba 20). ¿Cómo debe informarse?
5c. Confirmar que la producción exige los campos obligatorios de la guía 52 (caoba no los exige).
6. URL y credenciales de producción (por canal seguro, no por correo ni chat).

## 12. Decisiones del diseñador (revisar)

1. La pasarela es de **plataforma** (no de Despachos), con contrato en Abstractions.
2. Una guía por destino y tipo de traslado (no una por documento).
3. El manifiesto no se marca despachado hasta tener todas sus guías aceptadas.
4. El escáner reconoce el tipo de código (PDF417 vs LPN) sin que el usuario elija.
