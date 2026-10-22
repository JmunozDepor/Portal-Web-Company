# Módulo Despachos — Etapa 1: manifiestos, mantenedores y tarifario

> Estado: diseño para aprobación. Fecha: 2026-10-20.
> Alcance de este documento: **Etapa 1** completa. Etapas 2, 3 y Agenda se describen
> solo como alcance futuro (§11) para que la Etapa 1 no las bloquee.

## 1. Intención y por qué

Comercial Depor necesita controlar **todo lo que sale del centro de distribución**:
qué documentos (facturas, guías de traslado) viajan en qué camión, con qué
transportista/chofer/patente, si se entregaron o se rechazaron al volver el camión, y
cuánto costó cada transporte para el cierre mensual.

Hoy existe un desarrollo básico (`Codigo OLD/SAPB1.Enterprise.Web/WebDepor/Pages/manifiesto/*`)
que guarda todo en UDOs de SAP (`@INX_OMNF`, `@INX_MNF1`, `@INX_MNF3`, `INX_VHE`,
`INX_DVR`, `OSHP`) y donde el **costo del transporte se digita a mano** en cada
manifiesto. Se reemplaza por un plugin de la plataforma con base propia, costo
calculado por tarifario y trazabilidad real.

Además, desde el **1-nov-2026 rige la Res. Ex. SII N°154** (postergada por Res. Ex.
N°52/2026): toda guía/factura que ampare un traslado debe llevar datos de transporte
(origen/destino con comuna, chofer nombre+RUT, RUT transportista, patente
vehículo/carro, tipo de traslado, fecha/hora de salida, fecha de llegada, detalle con
cantidad/peso/volumen/unidad). La facturación se emite **anticipadamente** (B2B de los
clientes) por el área Comercial, por lo que la solución acordada es que el **manifiesto
emita su propia guía de transporte** (Etapa 2). La Etapa 1 debe por tanto **capturar y
guardar desde ya todos los datos que la Etapa 2 necesita**, para que la Etapa 2 solo
agrupe y emita.

Usuario principal: **personal de logística del CD** con usuario del portal (la
"logística externa" es personal del CD, no los transportistas, que no acceden al
portal), separado de Comercial — nunca toca facturación.

## 2. Objetivos, no-objetivos, anti-objetivos

**Objetivos Etapa 1**
1. Mantenedores: transportistas, tipos de vehículo, vehículos, carros, choferes,
   turnos, zonas, motivos de rechazo, orígenes (bodegas/CD), tarifario.
2. Manifiesto: crear, asociar documentos SAP (escaneo PDF417 o digitación manual),
   despachar, imprimir PDF interno, registrar retorno por documento, cerrar.
3. Costo de transporte **calculado** por tarifario, ajustable con motivo, congelado al
   cierre mensual.
4. Trazabilidad completa (historial de eventos) por manifiesto y por documento.
5. Primer reporte mensual (forma del reporte actual) + base para más reportes.

**No-objetivos (Etapa 1)**
- No emite DTE ni habla con FACELE (Etapa 2).
- No valida bulto a bulto la carga (Etapa 3); solo **cuenta** bultos/unidades por
  documento desde SAP, como hoy.
- No genera notas de crédito ni devoluciones en SAP: un rechazo solo **avisa** a
  Comercial.
- No registra detalle de productos rechazados: el retorno es **estado + motivo +
  comentario** por documento (confirmado por el usuario).
- No migra ni consulta el histórico de `@INX_*`: **el sistema nuevo parte de cero**,
  sin convivencia con las tablas antiguas (confirmado por el usuario). La regla
  anti-duplicado solo mira manifiestos del sistema nuevo; numeración desde 1.
- Sin acceso de transportistas: los usuarios son **personal del CD** con usuario del
  portal (confirmado). No hay filtrado de datos por transportista ni vínculo
  usuario↔transportista.

**Anti-objetivos (sería un fracaso aunque funcione)**
- Que un mismo documento pueda quedar despachado en dos manifiestos abiertos a la vez.
- Que cambiar una tarifa altere el costo de meses ya cerrados.
- Que la Etapa 2 tenga que volver a pedir datos de transporte que la Etapa 1 pudo
  capturar (chofer RUT, transportista RUT, patentes, comunas SII, direcciones).
- Consultas SAP con nombres de UDO/UDF de Depor fijos en el código (regla dura de la
  plataforma: personalización por `CompanyId`).

## 3. Restricciones de plataforma (no negociables)

- Plugin **externo** en `Portal SaaS - Plugins/Modulo.Despachos/`, siguiendo
  `Portal SaaS - Core/docs/09-GUIA-DESARROLLO-PLUGINS.md` completo (solo referencia
  `PortalSaas.Abstractions`; `ModuleCode` estable `Despachos`; páginas bajo
  `Pages/Despachos/`; `[Authorize]` en PageModels base; checklist §9).
- **Base propia** resuelta con `IExternalDatabaseConnectionService.ResolveConnectionAsync(moduleCode, companyId)`;
  motor dual PostgreSQL + SQL Server; convención `docs/01-CONVENCION-NOMBRES-BD.md`
  (inglés, plural, snake_case, `id`, `company_id` en toda tabla de negocio, `status`
  con check). Sin Company activa → rechazar con mensaje claro.
- Toda configuración **por `CompanyId`**, sin fallback a organización.
- SAP se lee vía `IHanaService` (motor HANA o SQL Server del cliente).
- Avisos vía `INotificationService.TryEnqueueAsync` (regla: todo módulo avisa por la
  campana).
- Autorización vía `ICurrentUserContext.HasActionAsync`.
- Todo menú lleva icono `bi-*`.
- Migraciones se aplican en las 4 bases (`portalsaas_saas_prod/_qa`, `ps_comdepor/_qa`)
  y en la base propia del módulo por compañía — mismo criterio que Rendiciones.
- Cultura es-CL global: `<input type=number>` con `InvariantCulture`.
- **Lógica y layout iguales a los módulos hermanos** (requisito explícito del usuario
  al aprobar): tomar `Modulo.Rendiciones` como referencia principal de estructura
  (jerarquía de `*PageModelBase` con `[Authorize]` y chequeo de acción, helper
  estático `DespachosUi` para badges/iconos de estado, CSS propio
  `wwwroot/css/despachos.css`, mantenedores bajo `Pages/Despachos/Configuracion/`,
  página `SinAcceso`), `Portal SaaS - Core/DESIGN.md` para el sistema visual y el
  spec de diseño responsive (`docs/superpowers/specs/2026-08-28-diseno-responsive-multidispositivo-design.md`).
  Mantenedores con **Crear + Editar + Eliminar** según la regla de `Portal SaaS -
  Core/CLAUDE.md` (eliminar bloqueado con mensaje si tiene dependientes; desactivar en
  vez de eliminar para choferes/vehículos/transportistas ya usados en manifiestos,
  que son historial). No inventar componentes visuales nuevos si existe uno
  equivalente en los hermanos.

## 4. Modelo de datos (base propia del módulo)

Nombres indicativos; el builder ajusta detalles respetando la convención.
Todas las tablas llevan `company_id`, `created_at/by`, `updated_at/by`.

### 4.1 Mantenedores

| Tabla | Campos clave | Notas |
|---|---|---|
| `carriers` (transportistas) | `code`, `name`, `tax_id` (RUT), `kind` (propio / tercero / marketplace / retiro), `is_active` | Hoy: Propio, TSM, MTO (MercadoLibre), RETIRO… RUT obligatorio salvo `kind=retiro` (Res.154 exige RUT transportista) |
| `vehicle_types` | `code`, `name`, `capacity_m3`, `capacity_kg` | Base del tramo m³ del tarifario |
| `vehicles` | `plate` (patente, normalizada mayúsculas sin guiones), `vehicle_type_id`, `carrier_id`, `capacity_m3` (override), `is_trailer` | Carros/remolques son `vehicles` con `is_trailer=true` |
| `drivers` | `tax_id` (RUT con DV validado), `full_name`, `carrier_id`, `license_class`, `license_expires_at`, `is_active` | Aviso si licencia vencida al asignar (no bloquea; configurable) |
| `shifts` (turnos) | `code`, `name` | Diurno, Vespertino; abierto a más |
| `communes` | `cut_code`, `name`, `region_code`, `region_name` | Catálogo oficial de comunas (código CUT; el DTE usa el **nombre**, máx. 20 caracteres) (seed) |
| `zones` + `zone_communes` | `code`, `name`; N:M con comunas | Para tarifas por zona |
| `origins` | `code`, `name`, `address`, `commune_id`, `sap_warehouse_code` | Dirección de origen del traslado (Res.154 `DirOrigen/CmnaOrigen`) |
| `rejection_reasons` | `code`, `name`, `applies_to` (rechazo / no entregado) | Catálogo cerrado editable |
| `holidays` | `date`, `name` | Para recargos por feriado |

### 4.2 Tarifario: valor base + recargos (abierto a nuevas condiciones)

Modelo confirmado con el jefe de despachos (sus ejemplos, no una lista cerrada):
**cada transportista tiene un valor base por región/comuna**, y sobre él se aplican
**recargos** que dependen de condiciones del viaje — p. ej. "si el camión va a 2 zonas
distintas se agrega un adicional de $X" y "si el despacho sale después de las 17:00 se
incrementa 20% del valor base". Pueden existir más condiciones según el transportista,
por lo que los recargos son configurables, no programados caso a caso.

#### a) Valor base — `rate_cards`
- **Condiciones** (todas opcionales; nula = "cualquiera"): `carrier_id`, `shift_id`,
  `region_code`, `zone_id`, `commune_id`, `vehicle_type_id`, `m3_from`, `m3_to`.
- **Vigencia**: `valid_from`, `valid_to` (nullable).
- **Cálculo**: `calculation_method` + `amount`, `min_amount`, `parameters_json`.
  Modalidades Etapa 1: `fixed_per_trip` (lo de hoy), `per_m3`, `per_km`,
  `per_package`, `per_document`, `no_charge` (RETIRO). Estrategias por código
  (`IRateCalculator`): una modalidad nueva = una clase + un valor de enum.
- `priority` (int).

**Resolución**: candidatas = tarifas vigentes a `dispatch_date` cuyas condiciones
calzan. Gana la **más específica** (más condiciones no nulas; comuna > zona > región);
empate → mayor `priority`; empate persistente → error visible "tarifa ambigua".

#### b) Recargos — `rate_surcharges`
- `carrier_id` (nullable = aplica a todos), `name`, vigencia, `priority`/orden,
  `is_active`.
- **Condición** = `condition_type` + `condition_parameters_json`. Tipos Etapa 1:
  - `distinct_zones_over` — n.º de zonas distintas del viaje > N (ejemplo: > 1).
  - `departure_time_from` — hora de salida ≥ HH:MM (ejemplo: 17:00). Usa la hora
    **real** de salida (`departed_at`); en borrador se estima con la hora planificada.
  - `shift_is` — turno = X.
  - `weekday_in` / `is_holiday` — día de la semana / feriado (feriados como mantenedor).
  - `distinct_communes_over`, `documents_over`, `packages_over`,
    `vehicle_type_is` — mismo patrón.
  - `manual_quantity` — **adicional con cantidad**: logística ingresa la cantidad en cada
    manifiesto (pestaña Costo → Adicionales); con `fixed_per_extra_unit` cobra cantidad × valor.
    Agregado 2026-10-21 (ver §15).
  - Estrategias por código (`ISurchargeCondition`): un tipo nuevo = una clase.
- **Efecto** = `effect_type` + `value`:
  - `fixed_amount` — suma $X (ejemplo: adicional por 2 zonas).
  - `percent_of_base` — suma X% del valor base (ejemplo: 20% después de las 17:00).
  - `fixed_per_extra_unit` — $X por cada zona/comuna/parada extra sobre el umbral.
- Todos los recargos cuyas condiciones se cumplen se aplican y **suman**; los
  porcentuales se calculan siempre sobre el valor base (no en cascada). Configurable
  más adelante si un transportista lo exige distinto.

#### c) Mecánica de cálculo (definida para poder testear)
1. **Localidad = comuna destino** del documento; zona y región se derivan de ella.
2. Se resuelve el valor base por cada localidad del viaje → líneas de detalle
   (`manifest_cost_lines` tipo `base`: `commune_id`, `rate_card_id`, `amount`,
   `documents`, `units`, `packages`).
3. **Valor base del viaje = el de la localidad de mayor valor** (las demás quedan
   registradas en 0). El "va a varias zonas" no se suma por localidad: lo cobra el
   recargo `distinct_zones_over`, como describió el jefe de despachos.
4. Se evalúan los recargos del transportista → líneas tipo `surcharge`
   (`rate_surcharge_id`, `amount`, explicación legible: "Salida 17:42 ≥ 17:00 → 20%
   de $150.000").
5. `cost_calculated` = base + recargos. El detalle de líneas se muestra en el tab
   Costo y en el simulador, para que logística pueda explicar cada peso.
6. Comuna sin resolver o sin tarifa base → manifiesto "sin tarifa" (se puede
   despachar; bloquea el cierre mensual hasta corregir o ingresar costo manual con
   motivo).

Las líneas base por localidad sirven además para los reportes por zona (no se reparte
costo por documento en Etapa 1).

**m³** del tramo **y** de la modalidad `per_m3` = capacidad del camión (vehículo, o tipo
de vehículo si el vehículo no la define) — supuesto del diseñador, confirmar con
logística. El volumen/peso cargado no se usa para costo en Etapa 1, pero el contrato
SAP (§6) ya lo trae como columnas opcionales (`weight_kg`, `volume_m3`) porque la
Etapa 2 lo necesita.

### 4.3 Manifiesto

`manifests` (cabecera):
- `number` (correlativo por compañía, visible), `status`, `dispatch_date` (fecha
  operativa), `shift_id`, `origin_id`.
- `carrier_id`, `vehicle_id`, `trailer_id` (nullable), `driver_id`.
- **Snapshot Res.154** al despachar (texto, no FK): `carrier_tax_id`, `driver_tax_id`,
  `driver_name`, `plate`, `trailer_plate`, `origin_address`, `origin_commune`.
  Los maestros pueden cambiar; el manifiesto conserva lo que realmente salió.
- `departed_at` (fecha/hora real de salida, sello al despachar), `estimated_arrival_date`
  (+ hora opcional), `returned_at`.
- `odometer_start`, `odometer_end` (opcionales; base para `per_km`).
- `service_order` / `remarks` (hoy `U_SrvOrder`).
- Costo: `cost_calculated`, `rate_card_id` aplicada, `cost_override` (nullable),
  `cost_override_reason`, `cost_final` (= override ?? calculado), `cost_frozen_at`
  (fecha del cierre mensual que lo congeló).
- Totales snapshot: documentos, unidades, bultos.

`manifest_documents` (detalle):
- `document_kind` ∈ {`invoice` (OINV), `transfer_guide` (guía de traslado)}; abierto a
  más (`delivery`, `credit_note`…) vía la consulta configurable (§6).
- `sii_document_type` (33, 34, 52…), `folio`, `sap_doc_entry`, `sap_doc_num`,
  `document_date`, `total_amount`.
- Destinatario: `customer_code`, `customer_name`, `customer_tax_id`.
- Destino: `destination_address`, `destination_commune_id` (resuelta contra catálogo;
  si no calza queda texto + marca "comuna no resuelta" que la Etapa 2 exigirá
  corregir).
- Para la Agenda futura: `sap_base_order_entry` (ORDR), `customer_po` (`NumAtCard`).
- Snapshot cantidades: `units`, `packages` (bultos).
- Retorno: `delivery_status`, `rejection_reason_id`, `comments`, `returned_by`,
  `returned_at`.
- `capture_method` ∈ {`pdf417`, `manual`}.

`manifest_events` (trazabilidad, append-only): `manifest_id`, `manifest_document_id`
(nullable), `event_type`, `from_status`, `to_status`, `payload_json`, `user_id`,
`occurred_at`. Todo cambio de estado, alta/baja de documento, cambio de
chofer/vehículo, ajuste de costo, impresión y reimpresión genera un evento.

`monthly_closures`: `period` (yyyy-mm), `closed_at`, `closed_by`, `status`
(`open`/`closed`/`reopened`), totales. Reabrir exige permiso especial y queda en eventos.

## 5. Estados y reglas

### 5.1 Manifiesto

```
borrador ──despachar──▶ despachado ──registrar retorno──▶ retornado ──cerrar──▶ cerrado
   │                        │
   └──anular──▶ anulado     └──anular (con permiso + motivo)──▶ anulado
```

- **borrador**: se arma; se pueden agregar/quitar documentos y cambiar
  vehículo/chofer libremente.
- **despachar**: valida (§5.3), sella `departed_at`, toma snapshot Res.154 y de
  cantidades, calcula costo, pasa todos sus documentos a `despachado`, imprime PDF.
  Hoy el sistema actual cierra e imprime en el mismo paso; se mantiene ese hábito.
- **despachado**: ya no se agregan documentos. Cambiar chofer/vehículo se permite con
  motivo y queda en eventos (en Etapa 2 esto disparará reemisión de guía). El cambio
  **renueva el snapshot Res.154 y recalcula el costo** (salvo que el período ya esté
  cerrado; entonces solo se permite con reapertura).
- **Período del manifiesto** (cierre mensual y reportes) = `dispatch_date`.
- **retornado**: todos los documentos tienen estado de retorno registrado.
- **cerrado**: costo y retorno revisados; queda solo lectura salvo reapertura.
- **anulado**: libera sus documentos (pueden ir en otro manifiesto); nunca se borra.
- El cierre mensual (`monthly_closures`) congela `cost_final` de los manifiestos del
  período y exige que ninguno esté en `borrador`/`despachado` ni "sin tarifa".

### 5.2 Documento dentro del manifiesto

Equivalencias con los códigos actuales entre paréntesis:

- `pendiente` (en borrador) → `despachado` (003) → uno de:
  - `entregado` (004 Conforme)
  - `entregado_parcial` (005 Conforme parcial) — requiere motivo
  - `rechazado` (006 En devolución) — requiere motivo
  - `no_entregado` — requiere motivo; el documento queda **liberado para reprogramar**
    en otro manifiesto, conservando la traza de ambos viajes.

`entregado_parcial` y `rechazado` disparan aviso en la campana (`TryEnqueueAsync`,
uno por usuario) a una **lista de usuarios destinatarios configurada por compañía** en
el módulo (`notification_recipients`). El texto del aviso es autosuficiente
(manifiesto, folio, cliente, motivo, comentario); el enlace abre el detalle del
manifiesto, que exige solo la acción "ver" — a los usuarios de Comercial se les da ese
permiso.

### 5.3 Validaciones al agregar / despachar

Al agregar documento:
1. Existe en SAP (consulta configurable §6) y no está cancelado.
2. **No está en otro manifiesto no anulado con estado distinto de `no_entregado`**
   (anti-objetivo #1). Mensaje indica número de manifiesto que lo tiene.
3. No duplicado dentro del mismo manifiesto (escaneo repetido = aviso, no error).
4. Aviso (no bloqueo) si la fecha del documento es posterior a la de despacho o si su
   comuna destino no está resuelta.

Al despachar:
1. Vehículo, chofer y transportista asignados y activos; chofer pertenece al
   transportista del vehículo (aviso si no — subcontratos existen; bloqueo
   configurable).
2. RUT de chofer y transportista válidos (salvo transportista `retiro`).
3. Al menos un documento.
4. Tarifa resuelta o costo manual con motivo.

## 6. Lectura de SAP — consultas configurables por compañía

Las consultas actuales dependen de objetos propios de Depor: factura por
`OINV.FolioNum` con bultos desde entregas base (`ODLN.U_NX_LPN`), y guía de traslado
por el UDO consolidado `@INX_ODGC.U_FoliNo` → `@INX_DGC1` → `OWTR` → `OWTQ.ShipToCode`
→ `OWHS` para la dirección destino. Otra compañía tendrá otros objetos.

Por eso: tabla `sap_document_queries` (por compañía y `document_kind`) con el SQL de
**búsqueda por folio** que debe devolver un **contrato fijo de columnas**: `doc_entry`,
`doc_num`, `folio`, `sii_document_type`, `document_date`, `customer_code`,
`customer_name`, `customer_tax_id`, `destination_address`, `destination_commune`,
`customer_po`, `base_order_entry`, `units`, `packages`, `total_amount`, `is_canceled`;
opcionales `weight_kg`, `volume_m3` (nulos si la compañía no los tiene).
Parámetro único `:folio` (y `:sii_document_type` opcional), estilo de `IHanaService`. Se valida el contrato al
guardar la consulta (ejecución de prueba). Seeds: las consultas actuales de Depor
adaptadas al contrato, para HANA. Solo lectura; el builder decide el mecanismo
concreto de parametrización segura, manteniendo el SQL fuera del código.

## 7. Captura de documentos

- **Escáner PDF417**: el timbre (TED) del documento impreso contiene un XML con
  `<RE>` RUT emisor, `<TD>` tipo DTE, `<F>` folio, `<FE>` fecha, `<RR>` RUT receptor,
  `<MNT>` monto. Dos vías:
  1. Lector de mano 2D en modo teclado (entrada de texto en un campo con foco).
  2. Cámara del celular/tablet en el navegador (librería JS de lectura PDF417; el
     builder elige).
  Se extrae tipo + folio (+ RUT emisor para validar que es de la compañía activa, y
  monto como control cruzado contra SAP). Codificación del TED es ISO-8859-1.
- **Manual**: tipo de documento + folio.
- Pantalla de armado pensada para **escaneo continuo** (foco permanente, sonido/
  color de éxito/error, contador), usable en tablet/celular (responsive de la
  plataforma).

## 8. PDF interno del manifiesto

Documento interno (no tributario), imprimible y descargable:
- Cabecera: N° manifiesto + código de barras (Code128) del número, compañía, origen,
  fecha y turno, transportista (nombre+RUT), chofer (nombre+RUT), patente vehículo y
  carro, fecha/hora de salida, llegada estimada, observaciones.
- Detalle **agrupado por destino** (comuna/dirección): tipo, folio, cliente, OC,
  unidades, bultos. Subtotales por destino y totales generales.
- Pie: firmas (despacho, chofer, recepción del retorno).
- Reimpresión marcada como "COPIA" con fecha/hora y usuario; cada impresión es un
  evento.
- Librería: la elige el builder; debe tener licencia compatible con uso comercial
  SaaS (ojo QuestPDF Community tiene tope de ingresos).

## 9. Pantallas y permisos

Menú `Despachos` (icono propio) con:
- **Manifiestos**: listado con filtros (fecha, estado, transportista, turno, folio
  contenido) → detalle con tabs Cabecera / Documentos / Retorno / Costo / Historial.
- **Nuevo manifiesto** (armado con escaneo).
- **Retorno**: escanear N° de manifiesto (código de barras del PDF) → marcar estado
  por documento rápido (todos "entregado" por defecto con excepción explícita).
- **Mantenedores**: Transportistas, Tipos de vehículo, Vehículos y carros, Choferes,
  Turnos, Zonas, Orígenes, Motivos de rechazo, Consultas SAP.
- **Tarifario**: listado de tarifas + simulador ("¿qué tarifa aplicaría a este
  manifiesto?").
- **Cierre mensual** + **Reportes**.

Acciones (permisos): ver, crear/editar borrador, despachar, registrar retorno, anular,
ajustar costo, cerrar manifiesto, cierre mensual, reabrir período, administrar
mantenedores, administrar tarifario, administrar consultas SAP. Se mapean al catálogo
FIJO de la plataforma (`PortalActions`: VIEW/CREATE/EDIT/DELETE/APPROVE/EXPORT) por hoja
de menú — la tabla de mapeo está en el plan de implementación.

## 10. Reportes (Etapa 1)

1. **Reporte mensual de despachos** (forma del actual): KPIs del mes (costo total,
   unidades, documentos, bultos) + tabla por día expandible a turno × transportista
   con costo, unidades, documentos, bultos, subtotales por día. Exporta Excel
   (ClosedXML, ya usado en la plataforma).
2. Base reutilizable (consultas por período/transportista/zona/estado de entrega) para
   los reportes que logística definirá después. Explícitamente abierto.

## 11. Alcance futuro (no construir ahora, pero no bloquear)

- **Etapa 2 — Guía de transporte Res.154**: servicio que agrupa los documentos del
  manifiesto por llave (manifiesto × receptor RUT × dirección destino × tipo de
  traslado: ventas tipo 1 referenciando facturas `TpoDocRef 33`; transferencias tipo 5
  referenciando guías `52`), detalle consolidado por SKU con peso/unidad, división
  automática por límites del formato DTE (detalle / 40 referencias), emisión vía
  FACELE (`DoceleOL.generaDTE`, formato XML, folio asignado por FACELE, `uuid`
  anti-duplicado), reemisión con anulación ante cambio de chofer/vehículo. Contrato de
  emisión DTE en `PortalSaas.Abstractions` (lo usarán también otros módulos). Tipo de
  traslado para ventas = parámetro por compañía (validar con asesor tributario).
  Requiere de la Etapa 1: snapshots Res.154 y comuna destino resuelta.
- **Etapa 3 — opcionales**: validación de bultos SAP escaneados contra lo esperado
  (cartón en UDF, consulta configurable); bultos de marketplace por canal con formato
  por regex; guías manuales con glosa (DTE 52 sin SAP, tipos 4/5/6 por defecto).
- **Agenda de despachos** (evaluar): citas por OV/OC departamental, estados
  solicitada→confirmada→reprogramada→cumplida/perdida, propuesta al armar manifiesto,
  KPI de cumplimiento. La Etapa 1 ya guarda `sap_base_order_entry` y `customer_po`.

## 12. Decisiones del diseñador (revisar)

Tomadas por el diseñador, no confirmadas explícitamente por el usuario:
1. Plugin externo con base propia (no UDOs SAP) — el usuario validó "los datos salen
   de SAP".
2. Despachar = sellar hora + snapshot + imprimir en un solo paso.
3. Valor base del viaje = localidad de mayor valor; los adicionales por varias zonas
   y por horario son recargos configurables (§4.2).
4. m³ del tramo = capacidad del camión.
5. "Sin tarifa" bloquea el cierre mensual, no el despacho.
6. Chofer de otro transportista: aviso, no bloqueo (configurable).
7. `no_entregado` libera el documento para otro manifiesto.
8. Localidad = comuna; recargo por hora usa la salida real; recargos suman y los %
   van sobre el valor base (no en cascada).
9. `per_m3` cobra sobre capacidad del camión, no volumen cargado.
10. Período = `dispatch_date`; cambio de vehículo/chofer post-despacho recalcula costo
    y renueva snapshot.
11. Avisos de rechazo a lista de usuarios por compañía; Comercial recibe permiso "ver".
12. Sin reparto de costo por documento en Etapa 1 (reportes por zona usan las líneas
    por localidad).

## 13. Pendientes de negocio

- Lista completa de recargos por transportista (los dos ejemplos del jefe de
  despachos ya están cubiertos; el resto se configura, no se programa).
- Reportes adicionales (los define logística).
- Qué rol de Comercial recibe los avisos de rechazo.

## 14. Lo que queda al builder

Estructura interna del plugin, librerías de PDF/escaneo/código de barras (con la
restricción de licencia), diseño visual dentro del sistema de diseño existente,
mecanismo concreto de ejecución segura de las consultas configurables, estrategia de
tests (TDD de la resolución de tarifas y de las transiciones de estado es esperable).

## 15. Ajuste 2026-10-21: tarifa real TSM/MTO y adicionales con cantidad

Tarifa vigente entregada por logística (valores por viaje):

| Transportista | Concepto | Valor | Cómo se configura |
|---|---|---|---|
| TSM | Camión 8M3 / 11M3 / 40M3 / Rampla | $76.857 / $92.612 / $133.987 / $286.547 | Tipo de vehículo + tarifa base `fixed_per_trip` del transportista |
| TSM | Santiago Viña | $318.207 | Una tarifa base más (decisión del usuario: sin tramos) |
| TSM | Nocturno | $25.589 | Turno "Nocturno" en el manifiesto + recargo `shift_is` `fixed_amount` |
| TSM | Ayudante adicional | $24.455 | Recargo `manual_quantity` `fixed_per_extra_unit` |
| TSM | Mall extra (punto adicional) | $11.975 | Recargo `manual_quantity` `fixed_per_extra_unit`: un camión va por defecto a un punto; cada punto adicional se ingresa a mano |
| MTO | Camión 11M3 | $61.500 | Tarifa base `fixed_per_trip` |

Decisiones del usuario: el nocturno se marca con el turno del manifiesto (no por hora); los
puntos adicionales se ingresan a mano (sin maestro de destinos por ahora); no hay mantenedor de
tramos.

Modelo: `manifest_extra_charges` (`manifest_id`, `rate_surcharge_id`, `quantity`; único por
manifiesto y recargo; 0 = sin fila). Las cantidades se editan en manifiestos no anulados con el
período abierto (máximo 999); fuera del borrador cada cambio recalcula el costo. Cada cambio deja el
evento `extra_charges_changed`. Un recargo usado en algún manifiesto no se puede eliminar, solo
desactivar. El simulador del tarifario no pide cantidades: los adicionales no aparecen en la simulación.

