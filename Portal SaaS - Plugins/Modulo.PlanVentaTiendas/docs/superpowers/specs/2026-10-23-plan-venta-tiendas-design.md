# Modulo.PlanVentaTiendas — Especificación de diseño

> Estado: **borrador para revisión** (2026-10-23). Sin desarrollo iniciado.
> Autor del requerimiento: Jorge Muñoz (Comercial Depor).
> Ubicación: plugin externo del Portal SaaS
> (`C:\PROYECTOS\Proyecto Portal Web-Company\Portal SaaS - Plugins\Modulo.PlanVentaTiendas\`).
>
> Convención de este documento: **[Decidido]** = lo definió el dueño del
> requerimiento. **[Propuesta]** = decisión del redactor, revisable.
> **[Abierto]** = falta respuesta del negocio (ver §14).

---

## 1. Intención — el porqué

Hoy cada jefe de tienda llena a mano una planilla de Google Sheets por semana:
meta, venta, unidades, boletas y horas por vendedor. La planilla calcula KPI,
detecta la "oportunidad" de cada vendedor y genera un **plan de acción**
(KPI a trabajar, comportamiento a mejorar, estrategia, supervisor).

El problema no es la lógica (funciona y el negocio la valida), sino que:

- casi todo lo que se digita **ya existe en SAP B1** (venta, unidades y boletas
  por vendedor — cada boleta trae el código de vendedor) [Decidido];
- la información queda dispersa en ~70 planillas, sin consolidación ni
  historia comparable entre tiendas, semanas o trimestres;
- el tráfico (entradas) lo mide un proveedor externo y no se cruza
  sistemáticamente con la venta para obtener **conversión**;
- la meta llega **mensual por tienda** y su bajada a día y a vendedor es manual.

**Objetivo de negocio:** mejorar y fortalecer **venta, conversión y tráfico**
en las 70 tiendas, con un informe **diario**, un análisis **semanal** (con plan
de acción) y una lectura **trimestral** de resultados.

## 2. Objetivos, no-objetivos y anti-objetivos

### Objetivos
1. Cargar automáticamente desde SAP B1 (Depor = HANA, Block = SQL Server) la
   venta, unidades y boletas por tienda, vendedor y día.
2. Cargar automáticamente el tráfico (entradas) por tienda desde el proveedor de
   conteo.
3. Que el jefe de tienda **solo digite lo que no existe en ningún sistema**:
   programación de turnos, horas reales/novedades y el plan de acción.
4. Bajar la meta mensual de tienda a **meta diaria** (peso histórico por día de
   semana + calendario de eventos) y luego a **meta diaria por vendedor** (según
   turnos programados).
5. Reproducir los KPI, el "escenario óptimo" y el "indicador prioritario" de la
   planilla actual, y generar el **plan de acción semanal** con seguimiento medible.
6. Informes en pantalla (diario / semanal / trimestral) y datos expuestos para
   **Power BI** u otras herramientas.
7. Multi-tenant (Depor y Block, y cualquier compañía futura) y **control de
   acceso por sucursal**.
8. Dejar la información capturada lista para **otros reportes y KPI** futuros.

### No-objetivos (por ahora)
- No escribe nada en SAP B1 (solo lectura).
- No reemplaza el sistema de RR.HH./remuneraciones ni el control de asistencia
  legal; los turnos son de **planificación comercial**.
- No calcula comisiones ni incentivos (podría consumir estos datos después).
- No gestiona la meta mensual en sí (quién la define y cómo); solo la recibe.

### Anti-objetivos (sería un fracaso aunque "funcione")
- Que el jefe de tienda termine digitando **más** o lo mismo que hoy.
- Que los números del módulo no cuadren con SAP y la tienda vuelva a la planilla.
- Que un jefe de tienda pueda ver datos de vendedores de otra tienda.
- Que la meta diaria por vendedor cambie "sola" hacia atrás sin trazabilidad.

## 3. Lo que existe hoy (planilla de referencia)

Planilla Google Sheets (una por tienda/semana). Hojas: resultados por
colaborador + plan de acción por vendedor.

### 3.1 Datos de entrada (hoy manuales)
| Dato | Nivel | Origen futuro |
|---|---|---|
| Presupuesto (meta) del local | tienda / semana | Meta mensual → bajada diaria (§7) |
| Venta neta | vendedor / día | SAP B1 |
| Boletas (documentos) | vendedor / día | SAP B1 |
| Unidades | vendedor / día | SAP B1 |
| Horas trabajadas | vendedor / día | Turnos programados + ajuste real (§8) |
| Registros de clientes | vendedor | **Fuera de alcance** [Decidido 2026-10-23] |
| Entradas (tráfico) | tienda | API de FollowUP |
| Cargo (Jefe, Sub jefe, Vendedor, PT20, PT30) | vendedor | Maestro de vendedores |

### 3.2 KPI (fórmulas verificadas contra la planilla)
| KPI | Fórmula |
|---|---|
| Logro | venta / presupuesto |
| Diferencia vs presupuesto | venta − presupuesto |
| Boleta promedio (BP) | venta / boletas |
| Unidades por boleta (UPB) | unidades / boletas |
| Precio promedio producto (PPP) | venta / unidades |
| Venta por hora (VPH) | venta / horas |
| Transacciones por hora (TPH) | boletas / horas |
| Conversión (tienda) | transacciones / entradas |

El KPI "Registro de clientes" de la planilla queda **fuera de alcance**
[Decidido].

Nota: VPH = TPH × UPB × PPP, y BP = UPB × PPP; por eso los KPI se leen como
árbol de causas de la venta.

### 3.3 Escenario óptimo (oportunidad) — fórmula inferida y verificada
Para cada vendedor y cada KPI, la oportunidad es cuánto más vendería si llegara
al **promedio de la tienda** en ese KPI, manteniendo su volumen:

| KPI | Oportunidad (solo si vendedor < tienda; si no, 0) |
|---|---|
| BP | (BP_tienda − BP_vend) × boletas_vend |
| UPB | (UPB_tienda − UPB_vend) × boletas_vend × PPP_vend |
| PPP | (PPP_tienda − PPP_vend) × unidades_vend |
| VPH | (VPH_tienda − VPH_vend) × horas_vend |
| TPH | (TPH_tienda − TPH_vend) × horas_vend × BP_vend |

Verificado con la semana 42, usando los KPI sin redondear (ej. Minerva: BP
(71.602−60.494)×17 ≈ 188.832; UPB (2,036−2,000)×17×30.247 ≈ 18.530;
PPP (35.167−30.247)×34 ≈ 167.288; Alvaro: TPH (0,7255−0,6571)×35×66.213 ≈
158.393). **Indicador prioritario** = KPI con mayor oportunidad; si todas son
0 → "Ninguno esta semana".
Nota: en la planilla, el cuadro de conversión usa 123 boletas mientras el total
del local es 111. En el módulo la conversión se calcula siempre con las mismas
transacciones del período que el resto de los KPI. [Propuesta]

### 3.4 Plan de acción (hoja actual)
Semana, vendedor, meta, venta, cumplimiento, cumplimiento tienda, KPI a trabajar
(promedio tienda / valor actual / valor a lograr), lista de *comportamientos a
mejorar* (ej. saludo, indagación, objeciones) con *estrategia a utilizar*
(ej. "Saludar. Invitar a conversar"), supervisor, fecha inicio/término.

## 4. Decisión de plataforma

**[Decidido]** Plugin **externo** del Portal SaaS, repo propio en
`Portal SaaS - Plugins\Modulo.PlanVentaTiendas\`, mismo patrón que
`Modulo.Rendiciones` / `Modulo.SellOut` / `Modulo.AuditoriaInventario`.
Motivo: el portal ya resuelve multi-tenant (Organization → Instance → Company),
usuarios, perfiles, permisos por menú, conector dual SAP HANA/SQL Server y
publicación en `portal.grupodepor.cl`.

Reglas del portal que aplican sin excepción (ver
`Portal SaaS - Core/docs/09-GUIA-DESARROLLO-PLUGINS.md` y `CLAUDE.md` del Core):

- Solo referencia `PortalSaas.Abstractions`; implementa `IModuloPortal`.
- `ModuleCode` estable: **`PlanVentaTiendas`** [Propuesta — nombre revisable
  antes de publicar; después no se puede cambiar].
- Todo se personaliza y persiste **por `CompanyId`**, sin fallback a Organization.
- Base propia del plugin vía `IExternalDatabaseConnectionService`, **motor dual**
  (PostgreSQL y SQL Server, EF Core con migraciones para los dos motores,
  `HasPrecision` en lugar de tipos específicos).
- Convención de nombres BD: inglés, plural, `snake_case`, `id` surrogate,
  `company_id` en toda tabla de negocio, `_at`, `is_`, `status`.
- Páginas bajo `Pages/PlanVentaTiendas/...`; parciales con sufijo del módulo.
- URLs relativas al PathBase (`~/...`), nunca absolutas.
- Responsive a 375 px (los jefes de tienda usarán celular/tablet).
- Lectura de SAP con `IHanaService` (SQL en dialecto HANA, traducido a SQL
  Server para Block) y **solo lectura**.

## 5. Arquitectura del módulo

```
SAP B1 Depor (HANA) ──┐                 Proveedor de tráfico (API/archivo)
SAP B1 Block (SQL)  ──┤                              │
                      ▼                              ▼
   SalesSyncBackgroundService            TrafficSyncBackgroundService
   (por compañía, ICurrentCompanyOverride + IHanaService, solo lectura)
                      │                              │
                      ▼                              ▼
          Base propia del plugin (Postgres o SQL Server)
          staging → hechos diarios → metas → KPI → planes de acción
                      │                              │
                      ▼                              ▼
     Páginas del portal (Razor)            Vistas rpt_* para Power BI
     captura + reportes                    (usuario de BD solo lectura)
```

- Los jobs en segundo plano siguen el patrón de
  `RendicionesReminderBackgroundService`: recorren
  `ListActiveCompanyIdsAsync(ModuleCode)`, abren un scope por compañía, fijan
  `ICurrentCompanyOverride` y aíslan errores por compañía.
- Las páginas **nunca consultan SAP en vivo** para reportes; leen la base propia
  (mismo principio que `Block_ValidacionRecepcion`).
- **[Propuesta]** Depor y Block apuntan a **la misma base física del plugin**
  (filas separadas por `company_id`), para que Power BI y el reporte
  consolidado lean un solo lugar. Si se exige separación física, cada compañía
  apunta a su base y la consolidación se hace en Power BI.

## 6. Fuentes de datos e ingesta

### 6.1 Venta desde SAP B1
- Granularidad almacenada: **cabecera de documento** (fecha, hora, tienda,
  vendedor `SlpCode`, tipo de documento, venta neta, unidades). Permite
  agregados diarios y cruce horario con tráfico.
  Volumen estimado: 70 tiendas × ~300 docs/día ≈ 8 M filas/año, manejable.
- **[Decidido]** Cada transacción es una **boleta, factura o nota de crédito**.
  Boletas y facturas suman y las notas de crédito restan venta y unidades.
  **[Propuesta]** Una nota de crédito no cuenta como transacción, es decir, no
  resta boletas. Se confirma en fase 0 contra la planilla.
- Se confirma en fase 0, en una sesión técnica: tablas y filtros exactos por
  compañía (OINV/ORIN, series de boleta y factura, `DocSubType`) y si se excluyen
  líneas no comerciales (bolsas, gift cards, despachos).
- Venta neta = sin IVA, descuentos aplicados. **[Abierto]** confirmar que la
  planilla usa neto y no bruto.
- Ruteo a tienda: **[Abierto]** por `BPLId` (sucursal), `WhsCode` o serie de
  documento — se configura por compañía en el maestro de tiendas (§9.1), como
  lección aprendida de `Block_ValidacionRecepcion` (OPOR rutea distinto que OWTR).
- Frecuencia: incremental cada N minutos durante el día (default 15) +
  **cierre nocturno** que re-lee el día completo (y los últimos 7 días, para
  capturar anulaciones y notas de crédito tardías). Upsert idempotente por
  clave de documento SAP.
- El sync incremental más el cierre nocturno sirve tanto si la venta llega a SAP
  en línea como si llega en lote. El informe diario siempre muestra la hora de
  la última sincronización. En fase 0 se mide la latencia real.

### 6.2 Tráfico (entradas)
- **[Decidido]** El proveedor es **FollowUP** y la integración es por **API**,
  cuya documentación y credenciales están pendientes de entrega.
- Conector `FollowUpTrafficProvider` detrás de la interfaz `ITrafficProvider`,
  para poder cambiar de proveedor sin tocar el resto. Las credenciales se guardan
  cifradas por compañía con el mecanismo del portal (`ISecretoCifradoService`),
  nunca en `appsettings`.
- Granularidad deseable: por tienda y **por hora**; mínima aceptable: por día.
  Se define al recibir la API.
- Mapeo tienda de FollowUP ↔ tienda del módulo en el maestro de tiendas.
- Si la API se atrasa, el módulo permite **carga por Excel** del tráfico diario
  como respaldo.

### 6.3 Datos digitados (lo que no existe en sistemas)
| Dato | Quién | Cuándo |
|---|---|---|
| Programación de turnos | Jefe de tienda | Anticipado (semana siguiente) |
| Horas reales / ausencias / cambios de turno | Jefe de tienda | Diario |
| Plan de acción (comportamientos, estrategia) | Jefe tienda + supervisor | Semanal |
| Calendario de eventos | Comercial | Antes del mes |

### 6.4 Meta mensual
- **[Decidido]** La meta mensual por tienda **se obtiene por query SQL**, así que
  no se digita.
- Job `GoalSyncBackgroundService`, que la lee a diario y al inicio de cada mes y
  versiona cada cambio. Si la meta de un mes en curso cambia, se recalcula el
  reparto solo para los días no transcurridos (§7.1, punto 5).
- **Pendiente de entrega:** la query, en qué base vive (¿la misma base SAP de
  cada compañía u otra base?) y su llave de tienda, para mapearla al maestro de
  tiendas. Si vive fuera de SAP, se resuelve como conexión externa por compañía
  con `IExternalDatabaseConnectionService`.
- Respaldo: carga por Excel solo para un admin, por si la fuente falla.

## 7. Módulo de metas — de mensual a diaria a vendedor

### 7.1 Meta diaria por tienda
Entrada: meta mensual `M` de la tienda `s` para el mes `m`.

1. **Perfil por día de semana** de la tienda: para cada día de semana
   (lun..dom), su participación promedio en la venta semanal, calculada sobre
   las últimas **N semanas** (default 12) de venta real, **excluyendo** días
   marcados como evento/feriado/cierre y semanas incompletas.
   **[Propuesta]** combinar con el mismo período del año anterior cuando exista
   (estacionalidad), configurable.
2. **Peso base** de cada fecha `d` del mes = participación de su día de semana.
3. **Calendario de eventos** (tabla propia, por compañía y opcionalmente por
   tienda/marca/zona):
   - *Cierre* (feriado irrenunciable, tienda cerrada) → peso 0.
   - *Feriado normal* → factor histórico de feriados de esa tienda (ventas de
     feriado ÷ mismo día de semana normal) o factor manual.
   - *Evento comercial* (Cyber, Black Friday, Navidad, Día del Padre, inicio de
     temporada escolar…) → factor histórico del mismo evento el año anterior
     (si está marcado en el calendario histórico) o **factor manual**.
4. **Normalización:** `meta_d = M × peso_d / Σ peso` del mes, de modo que la
   suma de días = meta mensual exacta (redondeo al peso; el residuo va al día de
   mayor peso).
5. **Ajuste manual** de un día por un usuario autorizado: el resto de los días
   **no transcurridos** se re-normaliza; los días ya transcurridos quedan
   congelados.
6. **Tienda sin historia suficiente** (nueva o < N semanas): usa el perfil
   promedio de su marca/cluster. **[Propuesta]**
7. Cada cálculo guarda **versión, parámetros y autor** (trazabilidad).

### 7.2 Meta diaria por vendedor
- `meta_v,d = meta_d × peso_v,d / Σ peso` de los vendedores programados ese día.
- **[Propuesta]** `peso_v,d = horas programadas × factor de cargo`
  (factor default 1,0 para todos; configurable por cargo, por si el jefe de
  tienda dedica parte del turno a tareas administrativas).
- **[Decidido]** Si alguien falta o cambia turno, la meta del día **se
  redistribuye** entre los que sí trabajaron, en proporción a sus horas reales,
  al registrar la novedad. La meta de la tienda no cambia. El cambio queda
  registrado: meta original, meta ajustada, quién y cuándo.
- Meta semanal/mensual por vendedor = suma de sus metas diarias.

## 8. Turnos y horas

- **Programación anticipada** por tienda y semana: vendedor, fecha, hora
  inicio, hora término, colación → horas programadas. Plantillas reutilizables
  ("semana tipo") y copiar semana anterior para minimizar digitación.
- **Horas reales:** por defecto = programadas; el jefe solo registra
  **excepciones** (ausencia, atraso, cambio, horas extra). Es la clave del
  anti-objetivo "no digitar más que hoy".
- Las horas reales alimentan VPH y TPH; las programadas alimentan la meta.
- Cierre de semana: la programación de una semana pasada queda bloqueada
  (editable solo por rol supervisor/admin, con auditoría).
- **[Abierto]** ¿existe hoy un reloj control/sistema de asistencia cuyos datos
  se puedan integrar a futuro (ej. GeoVictoria, Buk, Talana)?

## 9. Funcionalidades (pantallas)

### 9.1 Maestros (admin del módulo)
- **Tiendas:** código, nombre, marca, zona/supervisor, compañía, mapeo SAP
  (`BPLId`/bodega/series), id en proveedor de tráfico, activo. Importación
  inicial desde `IBranchCatalogService`/`IWarehouseCatalogService`.
- **Vendedores:** mapeo a `SlpCode` (OSLP, vía `ISalesEmployeeCatalogService`),
  tienda(s), cargo, jornada (PT20/PT30/full), vigencia. Soporta traslado de
  vendedor entre tiendas con fecha.
- **Cargos** y factor de meta.
- **Calendario de eventos** (§7.1).
- **Catálogo de comportamientos y estrategias** para el plan de acción
  (ej. Saludo → "Saludar. Invitar a conversar"; Indagación; Manejo de
  objeciones; Venta complementaria; Cierre…), asociable a cada KPI.
- **Asignación usuario ↔ tiendas** (control de acceso, §11).
- **Parámetros:** N semanas de historia, intervalos de sync, KPI benchmark
  (promedio tienda por defecto), umbrales de semáforo (ej. <80 % rojo,
  80–99 % amarillo, ≥100 % verde — **[Abierto]** confirmar los de la planilla).

### 9.2 Metas
Carga/edición de meta mensual, visualización del reparto diario (calendario con
pesos y eventos), ajuste manual de días, reparto por vendedor, historial de
versiones.

### 9.3 Turnos
Grilla semanal por tienda (vendedores × días), plantillas, copia de semana,
registro de novedades del día.

### 9.4 Informe diario (por tienda, con drill-down a vendedor)
Venta del día vs meta del día; acumulado semana y mes vs meta (logro y
proyección al cierre); boletas, unidades, BP, UPB, PPP, VPH, TPH; tráfico y
conversión (por hora si el proveedor lo entrega); estado de sincronización
("datos SAP al hh:mm").
Vista consolidada para supervisor/gerencia: ranking de tiendas por logro,
conversión y variación vs semana anterior/año anterior.

### 9.5 Análisis semanal + plan de acción
- Reproduce la planilla: resultados por colaborador, indicadores, escenario
  óptimo, indicador prioritario.
- **Plan de acción** auto-propuesto por vendedor con oportunidad > 0:
  KPI prioritario, valor actual, promedio tienda, **valor a lograr** (default =
  promedio tienda, editable), fechas de vigencia (semana siguiente).
  El jefe/supervisor elige comportamientos y estrategias del catálogo (o texto
  libre) y lo confirma; supervisor queda registrado.
- **Seguimiento:** al cerrar la semana de vigencia, el sistema mide el KPI real
  del vendedor y marca el plan como *logrado / parcial / no logrado* (medición
  automática, nada que digitar).
- Exportación del plan a PDF para revisión con el vendedor. **[Propuesta]**

### 9.6 Resultados trimestrales
Evolución semanal de logro y KPI por tienda/vendedor/marca/zona; efectividad
de los planes de acción (% logrados por KPI y por comportamiento trabajado);
tendencia de tráfico y conversión; comparativo vs trimestre anterior y año
anterior. **[Propuesta]** trimestres calendario (Q1 = ene–mar) salvo que el
negocio use otro año fiscal.

### 9.7 Monitoreo de integración
Última ejecución por compañía y fuente, filas leídas, errores, botón de
reproceso de un rango de fechas (rol admin).

## 10. Modelo de datos (borrador)

Todas las tablas llevan `company_id` (uuid) y, cuando corresponde,
`created_at`/`updated_at`/`created_by`. Nombres finales según
`docs/01-CONVENCION-NOMBRES-BD.md`.

**Maestros**
- `stores` (id, company_id, code, name, brand, zone, supervisor_user_id,
  sap_branch_id, sap_warehouse_code, traffic_provider_store_id, is_active)
- `store_sap_series` (id, company_id, store_id, series_code, document_type)
- `sales_people` (id, company_id, sap_slp_code, name, job_role_id, contract_type,
  is_active)
- `sales_person_store_assignments` (id, company_id, sales_person_id, store_id,
  valid_from, valid_to)
- `job_roles` (id, company_id, code, name, goal_weight_factor)
- `calendar_events` (id, company_id, date, event_type, name, scope_type,
  scope_id, manual_factor, is_store_closed)
- `behaviors` (id, company_id, name, kpi_code, default_strategy, is_active)
- `user_store_access` (id, company_id, user_id, store_id | zone, access_role)
- `module_settings` (id, company_id, key, value)

**Hechos importados**
- `sales_documents` (id, company_id, store_id, sales_person_id, sap_doc_entry,
  sap_object_type, doc_date, doc_time, net_amount, units, sign, synced_at)
  — UK (company_id, sap_object_type, sap_doc_entry)
- `daily_sales` (agregado: company_id, store_id, sales_person_id, date,
  net_amount, documents, units) — se recalcula desde `sales_documents`
- `traffic_counts` (company_id, store_id, date, hour, entries, source, synced_at)
- `sync_runs` (id, company_id, source, started_at, finished_at, status,
  rows_read, error_message, date_from, date_to)

**Metas y turnos**
- `monthly_goals` (id, company_id, store_id, year, month, amount, version,
  source ('sql' | 'excel'), status, synced_at)
- `daily_store_goals` (id, company_id, store_id, date, weight, amount,
  is_manual_override, goal_version_id)
- `daily_sales_person_goals` (id, company_id, sales_person_id, store_id, date,
  weight, amount, goal_version_id)
- `goal_calculation_versions` (id, company_id, store_id, year, month,
  parameters_json, created_by, created_at, reason)
- `shift_schedules` (id, company_id, store_id, sales_person_id, date,
  start_time, end_time, break_minutes, scheduled_hours, status)
- `shift_actuals` (id, company_id, shift_schedule_id | sales_person_id, date,
  actual_hours, novelty_type, comment, recorded_by)
- `sales_person_goal_adjustments` (id, company_id, sales_person_id, date,
  original_amount, adjusted_amount, reason, shift_actual_id, created_by,
  created_at) — trazabilidad de la redistribución por ausencia (§7.2)

**Plan de acción**
- `weekly_kpi_snapshots` (company_id, store_id, sales_person_id | null=tienda,
  iso_year, iso_week, venta, presupuesto, boletas, unidades, horas, bp, upb,
  ppp, vph, tph, oportunidades por KPI, kpi_prioritario) — foto congelada al
  cerrar la semana, base del histórico trimestral.
- `action_plans` (id, company_id, store_id, sales_person_id, iso_year,
  iso_week, kpi_code, store_avg_value, current_value, target_value, start_date,
  end_date, supervisor_user_id, status, result_value, result_status)
- `action_plan_items` (id, company_id, action_plan_id, behavior_id,
  behavior_text, strategy_text, order)

**Reporting (Power BI)** — vistas `rpt_*` estables (contrato con BI):
`rpt_daily_store`, `rpt_daily_sales_person`, `rpt_weekly_kpi`,
`rpt_action_plans`, `rpt_traffic_hourly`, `rpt_dim_store`,
`rpt_dim_sales_person`, `rpt_dim_calendar` (con semana ISO, mes, trimestre,
eventos). Power BI se conecta con un usuario de BD **solo lectura** limitado a
estas vistas. Base de partida para los visuales: `C:\PROYECTOS\Power_BI\Block\RepVenta`.

## 11. Roles y control de acceso

Permisos de menú/acción con el mecanismo del portal (perfiles +
`PortalActions`); **alcance por tienda** con `user_store_access` del plugin,
aplicado en todas las consultas (filtro obligatorio en el repositorio, no solo
en la UI).

| Rol | Ve | Hace |
|---|---|---|
| Jefe de tienda | Su(s) tienda(s) | Turnos, novedades, plan de acción, informes |
| Supervisor / zonal | Tiendas de su zona | Valida planes, ajusta metas diarias de su zona, informes |
| Gerencia comercial | Todas las tiendas de la compañía | Informes, consolidado |
| Control de gestión | Todas | Calendario de eventos, respaldo de meta por Excel |
| Admin del módulo | Todas | Maestros, parámetros, monitoreo, reprocesos |

**[Decidido]** Los vendedores **no** tienen acceso. El módulo es la herramienta
del jefe de tienda para generar el plan de acción y revisarlo con cada vendedor.
El PDF del plan (§9.5) es lo que el vendedor recibe.

## 12. Definiciones de tiempo
- **Semana ISO** (lunes a domingo) — coincide con la planilla (12-10-2026 =
  semana 42). **[Abierto]** confirmar que la semana comercial es lunes–domingo.
- Zona horaria: America/Santiago.
- Trimestre: calendario (§9.6).

## 13. Fases propuestas

| Fase | Alcance | Resultado para la tienda |
|---|---|---|
| 0 — Descubrimiento | Insumos de §14; queries de venta (boleta/factura/NC) y de meta validadas contra una semana real de la planilla (Depor y Block) | Números que cuadran |
| 1 — Datos base | Esqueleto del plugin, maestros, sync de venta SAP (ambas compañías), monitoreo | Venta diaria por tienda/vendedor sin digitar |
| 2 — Metas y turnos | Sync de meta mensual por query, calendario, reparto diario y por vendedor, turnos, novedades y redistribución | Meta diaria por vendedor |
| 3 — KPI y plan de acción | Informe diario, análisis semanal, escenario óptimo, plan de acción y su medición | Reemplaza la planilla (piloto en 3–5 tiendas) |
| 4 — Tráfico | Conector FollowUP, conversión diaria/horaria (puede adelantarse si la API llega antes) | Conversión automática |
| 5 — Trimestral y BI | Vistas `rpt_*`, modelo Power BI, reporte trimestral | Gestión consolidada |
| 6 — Despliegue | 70 tiendas, capacitación, retiro de planillas | — |

Criterio de salida del piloto: durante 2 semanas, KPI del módulo = planilla
(diferencia ≤ 0,5 %) y tiempo de digitación del jefe de tienda menor al actual.

## 14. Preguntas abiertas para el negocio

Ordenadas por impacto en lo que se construye:

### Respondidas (2026-10-23)
- Tráfico → FollowUP, por API (documentación pendiente de entrega).
- Venta → cada transacción es boleta, factura o nota de crédito.
- Meta mensual → se obtiene por query SQL.
- Ausencias → la meta se redistribuye entre los presentes.
- Registro de clientes → fuera de alcance.
- Vendedores → sin acceso; el reporte es del jefe de tienda.

### Insumos pendientes de entrega (bloquean fase 0)
1. Documentación y credenciales de test de la **API de FollowUP**.
2. **Query de la meta mensual**: base donde vive y llave de tienda.

### Aún abiertas (no bloquean el inicio)
3. Umbrales del semáforo (hoy rojo/amarillo/verde en la planilla).
4. Estructura comercial: ¿marcas y zonas/supervisores por tienda? Sirve para los
   consolidados y para el perfil de tiendas nuevas.
5. ¿Existe reloj control o sistema de asistencia integrable a futuro?
6. ¿Una sola base del plugin para Depor y Block (recomendado) o bases separadas?

## 15. Decisiones tomadas por el redactor (revisar)

- Nombre del módulo `PlanVentaTiendas`.
- Venta almacenada a nivel cabecera de documento (no solo agregado diario).
- Historia de 12 semanas para el perfil por día de semana, excluyendo eventos.
- Reparto a vendedor por horas programadas × factor de cargo (factor 1,0).
- La nota de crédito resta venta y unidades, pero no resta transacciones.
- Horas reales = programadas salvo novedad (registro por excepción).
- Benchmark de oportunidad = promedio de la tienda (igual que la planilla).
- Plan de acción medido automáticamente al cierre de su semana de vigencia.
- Semana ISO, trimestre calendario, zona horaria America/Santiago.
- Una base física del plugin compartida por Depor y Block, separada por
  `company_id`.
- Power BI consume vistas `rpt_*` con usuario solo lectura.

## 16. Riesgos

| Riesgo | Mitigación |
|---|---|
| Reglas de "qué es venta de tienda" distintas entre SAP Depor y Block | Fase 0: cuadratura contra planilla real por compañía antes de construir reportes |
| Traductor HANA→SQL Server de `IHanaService` no soporta alguna query | Usar `GetPlatformEngineTypeAsync` y SQL por motor en las queries de ingesta |
| Vendedores sin `SlpCode` correcto en boletas (ej. código genérico) | Reporte de "venta sin vendedor asignado" por tienda en el monitoreo |
| API de FollowUP atrasada o con datos solo diarios | Carga por Excel como respaldo; la conversión horaria queda opcional |
| Resistencia del jefe de tienda | Registro por excepción, plantillas de turno, piloto con tiendas referentes |
| Volumen (70 tiendas × documentos) | Índices por (company_id, store_id, date); agregados diarios precalculados |
