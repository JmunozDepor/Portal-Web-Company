# 08 — Brecha funcional vs. `PortalSAP_v2`

Auditoría pedida explícitamente por el dueño del proyecto (25/26 jul 2026): antes de
seguir agregando módulos nuevos, revisar en detalle qué existe en el sistema anterior
(`referencia-original/PortalSAP_v2/`, copia de solo lectura) que este proyecto **debe
heredar con toda su lógica funcional**, dejando de lado la capa de negocio específica
de Comercial Depor que ya se decidió no portar (`GestionDistribucionGastos`/`SellOut`,
ver `CLAUDE.md` § Decisiones ya tomadas).

Alcance de esta auditoría, tal como se pidió:
1. **Motores genéricos de documento** (Venta/Compra/Inventario) — ¿qué funcionalidad
   del original todavía no tiene equivalente acá?
2. **Módulo Importador Genérico** — hoy no existe nada portado.
3. **Módulo Rendiciones** ("RindeGastos") — no estaba ni siquiera documentado en el
   `CLAUDE.md` del original, investigación desde cero.
4. **Requerimiento nuevo, sin precedente en el original**: importador de plugins para
   el modelo SaaS.

Este documento es un inventario de brechas para priorizar trabajo futuro — **no** es
un plan de implementación con fases y fechas (eso se arma aparte, por módulo, cuando
se decida arrancar cada uno). Las líneas de código citadas son del código real de
`referencia-original/PortalSAP_v2` (relevado por agentes de exploración, jul 2026) y
del estado real de este proyecto a la misma fecha — van a desactualizarse, no confiar
en los números sin volver a verificar contra el código si pasa mucho tiempo.

## Regla de gobierno ya vigente (contexto para todo lo que sigue)

Ver `CLAUDE.md` § "Todo nace del documento padre" — los motores genéricos ya
implementados acá (`SalesDocumentService`/`PurchaseDocumentService`/
`InventoryDocumentService`) siguen el mismo patrón que el original: catálogo estático
por tipo + PageModel base sin métodos `virtual` + tabs compartidas. Cualquier cosa que
se agregue de lo que sigue en este documento **debe respetar ese mismo patrón**, no
introducir una arquitectura paralela.

---

## 1. Motores genéricos de documento (Venta/Compra/Inventario)

Los 3 tipos de documento ya están portados (7 de Venta, 2 de Compra, 2 de Inventario,
ver `CLAUDE.md` § Estado actual). Esto es lo que el original tiene y este proyecto
todavía no:

### 1.0 Tres importadores distintos — no confundirlos

El original tiene **tres** mecanismos de importación completamente separados, y los
3 motores genéricos usan los 3. Vale la pena dejarlo explícito acá porque en la
primera versión de este documento el tercero quedó mencionado solo al pasar (y
directamente ausente en la tabla de Compra):

| # | Mecanismo | Nivel | Crea | Dónde vive en el original |
|---|---|---|---|---|
| 1 | **Importador de líneas CSV** (`IImportadorLineasDocumentoService`) | Dentro del formulario de creación de **un** documento, tab Contenido | Llena la grilla de líneas de ese único documento que se está digitando | `PortalSAP.Core.Documentos` — **un solo servicio compartido por Venta y Compra** |
| 2 | **Importador de líneas CSV de Inventario** (`IImportadorLineasInventarioService`) | Igual que #1 pero para Traslados/Solicitudes | Idem, un documento | `PortalSAP.Core.Inventario` — copia deliberada de #1, columnas distintas (bodega dual, sin precio/cuenta/dimensiones) |
| 3 | **Módulo Importador Genérico** (`Modulo.ImportacionGenerica`, ver §2) | Plugin aparte, wizard propio | **Muchos documentos de una vez**, uno por fila/grupo de un Excel | Plugin independiente |

Los tres están **0% portados** en este proyecto. El más caro de los tres es el #3
(ver §2); los dos primeros son mucho más chicos y comparativamente baratos de portar
(ver 1.4 más abajo) — no conviene evaluarlos como si fueran una sola pieza de trabajo.

### 1.1 Motor de Venta (`GenericoVenta` → `SalesDocumentService`)

| Funcionalidad del original | Estado acá |
|---|---|
| 7 tipos de documento | ✅ Portado completo |
| Tabs General + Contenido | ✅ Portado |
| **Tabs Logística + Finanzas** (`VistaLogistica`/`VistaContabilidad`) | ❌ Falta — `DocumentFormViewModel.LogisticsView`/`AccountingView` ya soportan esto, solo falta poblar los campos y catálogos |
| Catálogos Cliente/Vendedor/Artículo/Almacén | ✅ Portado |
| **Catálogos Cuenta contable/Dimensión/Empleado(Responsable)/Forma de envío/Condición de pago** | ❌ Faltan los 5 — bloquean tabs Logística/Finanzas y líneas de Servicio |
| **Líneas de tipo Servicio** | ❌ Ya diferido explícitamente (ver `CLAUDE.md`, diseño completo documentado ahí) |
| **`CamposAdicionales`/UDF dinámicos** (`SapCamposAdicionalesHelper.Aplanar`) | ❌ Falta — sin esto no hay forma de mandar un UDF custom sin tocar código |
| **Campo condicional por tipo** (ej. `NumeroNotaCreditoOrigen` solo para `NotaCredito`) | ❌ Falta el mecanismo (no hay ningún campo específico de subtipo implementado todavía) |
| **Importador CSV de líneas del formulario** (`IImportadorLineasDocumentoService`, ver §1.0 #1) | ❌ Diferido a propósito (ver plan de la Fase 1) |
| **`returnUrl` preservando filtros al volver del Detalle al listado** | ❌ Falta — hoy "Volver" siempre va a la raíz del listado sin filtros |
| **Desglose de totales** (`TotalAntesDescuento`/`Descuento`/`Impuesto`/`TotalDocumento`) | ❌ Solo se muestra `DocTotal` crudo de SAP |

### 1.2 Motor de Compra (`GenericoCompra` → `PurchaseDocumentService`)

| Funcionalidad del original | Estado acá |
|---|---|
| 2 tipos de documento | ✅ Portado |
| Oferta permite crear, Pedido nace de Copy-From al aprobar | ⚠️ **Desviación deliberada**: acá ambos permiten creación directa (documentado en `CLAUDE.md`) |
| **Motor de aprobación** (`IAprobacionService`/`Modulo.Aprobacion`, N niveles configurables, condiciones SQL por etapa) | ❌ Completamente ausente — es la brecha más grande del proyecto en general, ver §6 más abajo (se relaciona con Rendiciones) |
| **`Comprador`/`GrupoAprobacion`** (admin del equipo de aprobadores) | ❌ Falta (depende del motor de aprobación) |
| Catálogos Cuenta contable/Dimensión (líneas de Servicio) | ❌ Mismo gap que Venta |
| **Importador CSV de líneas del formulario** (`IImportadorLineasDocumentoService`, ver §1.0 #1) | ❌ Falta — **es el mismo servicio compartido que usa Venta**, `GenericoVentaLineaDto`/`GenericoCompraLineaDto` son estructuralmente idénticos así que en el original vive una sola vez en `Core` y ambos módulos lo consumen pasándole sus propios catálogos ya cargados (`CatalogosImportacionLineas`); portarlo para Venta lo deja prácticamente listo para Compra también |

### 1.3 Motor de Inventario (`GenericoInventario` → `InventoryDocumentService`)

| Funcionalidad del original | Estado acá |
|---|---|
| 2 tipos, `StockTransferLines` (no `DocumentLines`), sin `DocDueDate`, almacén por línea | ✅ Corregido en esta sesión tras confirmar contra el original |
| Solicitud permite crear, Traslado nace de Copy-From | ⚠️ Misma desviación deliberada que Compra |
| **Socio de negocio opcional** (cliente asociado a un traslado tipo despacho e-commerce) | ❌ Falta — el original lo soporta como campo opcional, acá no existe ningún vínculo con cliente |
| **Importador CSV de líneas del formulario, versión Inventario** (`IImportadorLineasInventarioService`, ver §1.0 #2) | ❌ Falta — **no** reutiliza el servicio de Venta/Compra (columnas distintas: bodega origen/destino, sin precio/cuenta/dimensiones), es una copia deliberada aparte |

### 1.4 Detalle del importador de líneas CSV del formulario (§1.0, #1 y #2)

Para dimensionar bien esta pieza, que es chica comparada con el Módulo Importador
Genérico (§2) pero tiene su propio valor real (agiliza digitar un documento con
muchas líneas a mano):

- **`IImportadorLineasDocumentoService`** (`PortalSAP.Core.Documentos`, 423 líneas +
  interfaz de 42) — 3 métodos: `GenerarPlantillaCsv(tipo)` (CSV de solo encabezado,
  columnas distintas según `"Articulo"`/`"Servicio"`), `ExtraerCodigosArticulo(tipo,
  csv)` (primera pasada, para pedirle a SAP solo los artículos que aparecen en el
  archivo en vez de cargar el catálogo completo — mismo criterio anti-N+1 que el resto
  del proyecto), y `ProcesarCsv(tipo, csv, catalogos)` (cruza cada fila contra los
  catálogos ya cargados por el llamador, sin resolver nada por su cuenta contra SAP).
  Vive **una sola vez** en `Core` y lo consumen los 9 subtipos de Venta + los 2 de
  Compra (confirmado por grep sobre el código real — aparece en las 11 páginas
  `Detalle.cshtml.cs`, incluidos los subtipos de solo lectura, que igual lo usan para
  la plantilla).
- **`LineaImportadaCsvDto`**/`ResultadoImportacionCsvDto`/`CatalogosImportacionLineas`
  (`Abstractions/Modelos/LineaImportadaCsvDto.cs`, 67 líneas) — el DTO de fila tiene
  el mismo shape que `LineaInput` de los PageModel base a propósito, es el punto de
  encuentro entre el CSV y el formulario. `PlantillaValida = false` si el encabezado
  del CSV no corresponde al Tipo del documento (ej. subieron la plantilla de Servicio
  con el documento en modo Artículo) — en ese caso no inserta nada, solo muestra error.
- **`IImportadorLineasInventarioService`** (`PortalSAP.Core.Inventario`, interfaz de
  35 líneas + implementación de 296) — mecánica de parseo CSV duplicada a propósito
  respecto al de Venta/Compra (mismo criterio "copia deliberada" que rige los 3
  motores, ver `CLAUDE.md`), columnas: `CodigoArticulo`, `Cantidad`,
  `AlmacenOrigen`, `AlmacenDestino`.
- **Tamaño total estimado de portar los dos**: ~460 líneas de C# puro (sin UI propia —
  la UI es "Descargar plantilla CSV"/"Importar CSV" ya integrada en la tab Contenido
  existente de cada motor, no una pantalla aparte) + los catálogos que ya haga falta
  tener portados para Cuenta Mayor/Dimensión (mismos que bloquean líneas de Servicio,
  ver §1.1/§1.2 — no es un costo adicional si ya se portan para eso).

### 1.5 Transversal a los 3 motores

- **`SapCamposAdicionalesHelper`** (UDF dinámicos + `EsNombreReservado`) — ausente en
  los 3, y es un prerrequisito duro para portar el Importador Genérico (§2) con campos
  de usuario.
- **Buscador de catálogo estandarizado** (`data-catalogo-buscador`, atributo HTML
  genérico) — acá existe una versión más simple (`catalog-search.js`, parámetros
  explícitos por invocación), funcionalmente similar pero no data-attribute driven.
- **`Modulo.Base`** (plantilla de referencia para módulos nuevos) — no existe un
  plugin plantilla en este proyecto; cada plugin nuevo se arma copiando el patrón de
  uno existente a mano.

---

## 2. Módulo Importador Genérico

**No confundir con el importador de líneas del formulario (§1.0, #1 y #2)** — este es
el mecanismo #3 de la tabla de §1.0: un plugin aparte que crea **muchos documentos
completos** de una vez desde un Excel, no una ayuda para cargar las líneas de un
documento que ya se está digitando a mano.

**Hoy: 0% portado.** Grep sobre todo el proyecto nuevo (excluyendo
`referencia-original/`): cero resultados de "ImportacionGenerica"/"ClosedXML"/
"ExcelDataReader".

### 2.1 Qué es

Importador masivo de documentos vía Excel para **los 3 motores genéricos** (Venta,
Compra **e Inventario** — corrección sobre el resumen previo, que solo mencionaba 2).
Mapeo de columnas **por letra** (A, B, C…, nunca por nombre de header), con
configuración estándar por Empresa+Módulo+TipoDocumento+TipoLínea más excepciones por
socio de negocio puntual, campos de usuario configurables (UDF), agrupación de un
mismo archivo en varios documentos vía una columna de agrupación, y un wizard de
1 sola página (parámetros+archivo → previsualizar con errores por fila → confirmar
con polling).

**Nunca postea directo a Service Layer** — arma el DTO del documento y llama
`ISalesDocumentService`/`IPurchaseDocumentService`/`IInventoryDocumentService.CreateAsync`
(ya renombrados a este proyecto), heredando gratis el UDF de trazabilidad y
(a futuro) el enganche de aprobación sin duplicar esa lógica — mismo motivo por el que
vale la pena portarlo como capa por encima de los motores ya existentes, no como
servicio aislado.

### 2.2 Tamaño real (código dedicado del original)

| Bloque | Archivos | Líneas |
|---|---:|---:|
| Plugin `Modulo.ImportacionGenerica` (wizard + 2 CRUD admin) | 13 | 1.620 |
| `Abstractions` (4 contratos, 17 métodos) | 4 | 122 |
| `Abstractions` (6 DTOs + 4 enums) | 10 | 256 |
| `Core/ImportacionGenerica` (`ImportacionGenericaService` solo = 853) | 6 | 1.357 |
| SQL HANA (3 tablas + 2 `ALTER`) | 2 | 102 |
| Registro DI | — | 5 |
| **Subtotal código dedicado** | **35** | **≈ 3.460** |

### 2.3 Lo que hace este porte más caro de lo que parece: 6 catálogos ausentes

El motor en sí (853 líneas, autocontenido, bien comentado) no es lo caro — lo caro son
las dependencias que hoy **no existen en este proyecto**:

| Servicio que consume el importador | Estado acá |
|---|---|
| `IParidadCatalogoService` (SKU cliente ↔ `ItemCode`) | ❌ No existe |
| `ICuentaContableCatalogoService` | ❌ No existe |
| `IDimensionCatalogoService` (`CostingCode` 1/2/3, DimCode) | ❌ No existe |
| `IListaPrecioService.ObtenerPreciosAsync` | ❌ No existe |
| `ISocioNegocioDefaultsService` (vendedor/lista de precio default del socio) | ❌ No existe |
| `SapCamposAdicionalesHelper.Aplanar`/`EsNombreReservado` | ❌ No existe |
| `AdditionalFields` (campos dinámicos) en los DTOs de documento | ❌ No existe en ningún DTO nuevo |

Sin estos 6-7 servicios, "toda su lógica" se degrada a un importador que solo maneja
líneas de Artículo con almacén — se pierde Servicio (cuenta mayor + 3 dimensiones),
paridad SKU, precio de lista de sistema, y **campos de usuario enteros** (sin
`SapCamposAdicionalesHelper` el CRUD de `CamposUsuario` no tiene ningún efecto real).

### 2.4 Cambio de arquitectura obligatorio: HANA → EF Core

El original persiste la configuración con `HanaCommand`/`HanaParameter` crudo contra
`PORTALWEB` en HANA. Este proyecto usa EF Core dual (Postgres/SQL Server, ver
`docs/02-ARQUITECTURA-BASE-DE-DATOS.md`) — **no es una traducción de SQL**, hay que:
- Modelar 3 entidades EF nuevas + migraciones para los 2 motores de base propios.
- Decidir si `EMPRESA_CODIGO` (scope original) se convierte en `organization_id`/
  `company_id` según la convención de este proyecto (ver
  `docs/01-CONVENCION-NOMBRES-BD.md`) y rehacer el `UNIQUE` compuesto acorde.
- Reescribir `ConfiguracionImportacionGenericaService`/
  `CampoUsuarioImportacionGenericaService` completos (~420 líneas) contra
  `PortalSaasDbContext` en vez de ADO.NET directo.

### 2.5 Librerías nuevas a agregar

`ExcelDataReader` + `ExcelDataReader.DataSet` (lectura) y `ClosedXML` (generar
plantilla de descarga) — ninguna de las 3 está en `PortalSaas.Core.csproj` hoy.

### 2.6 Otras brechas de infraestructura a resolver al portar

- `IImportacionGenericaProgresoStore` original es un singleton in-memory sin
  expiración — en un SaaS multi-instancia esto necesita repensarse (scope por
  organización como mínimo, backend distribuido si hay más de una instancia del Host).
- Cero tests en el original — portar con cobertura real implica escribirlos de cero
  (regla dura de este proyecto para módulos comerciales, ver `CLAUDE.md` — evaluar si
  aplica el mismo estándar acá).

### 2.7 Dimensionamiento total estimado

| | Líneas |
|---|---:|
| Código dedicado portable (adaptado, no copy-paste) | ≈ 3.460 |
| Soporte compartido faltante (6-7 catálogos + helper de UDF) | ≈ 870 |
| Trabajo nuevo impuesto por el SaaS (EF dual, multi-tenant, tests) | ≈ 1.430 |
| **Total estimado** | **≈ 5.760 líneas** |

---

## 3. Módulo Rendiciones ("RindeGastos")

**Hallazgo central: es funcionalidad de plataforma genuina, no desarrollo a medida.**
A diferencia de `GestionDistribucionGastos`/`SellOut` (ya excluidos por decisión
explícita), este módulo fue diseñado **desde el día uno con la intención declarada de
poder independizarse como producto SaaS aparte** — el propio doc-comment de
`ModuloRendiciones.cs` en el original lo dice textualmente, y usa una base de datos
propia (SQL Server vía `ISqlServerService`) separada de `PORTALWEB`/HANA
específicamente para esa frontera. Coincide exactamente con el objetivo de este
proyecto.

**No estaba documentado ni en `CLAUDE.md` ni en `ARCHITECTURE.md` del original** — toda
su especificación vive solo en `docs/tareas/rendiciones-gastos.md` (818 líneas de
bitácora), que además admite que hay contexto de producto (levantamiento de negocio
completo, benchmark contra RindeGastos/Buk) que ni siquiera está versionado en ese
repo.

### 3.1 Qué resuelve

Clon funcional de RindeGastos: captura de gastos de bolsillo por colaborador (boletas,
viajes, transporte, viáticos) → agrupación en informes → aprobación jerárquica →
conciliación contra un fondo por rendir (anticipo) → cierre exportable. Tres roles de
menú: **Rendidor** (Mis Gastos, Informes), **Aprobador** (bandeja de
aprobación/rechazo con visor de comprobante), **Administrador** (Tipos de Gasto/
Documento, Grupos de Aprobación, Centros de Costo por Usuario, Políticas de Gasto,
Cierre y Reportes).

Dos features que van más allá de un expense report básico:
- **OCR real** (Azure Document Intelligence, modelo `prebuilt-invoice`) para extraer
  datos de la boleta/factura fotografiada — probado en vivo contra Azure real.
- **Kilometraje con ruta real** (Azure Maps: geocoding + Route Directions), tarifa
  CLP/km configurable por tipo de gasto.

Motor de políticas propio: tope por categoría (bloqueante o solo advertencia) +
detección de duplicados por folio+RUT.

### 3.2 Arquitectura — completamente independiente de los 3 motores genéricos

No usa `GenericoVenta`/`GenericoCompra`/`GenericoInventario` (cero referencias). No
reusa `IAprobacionService`/`Modulo.Aprobacion` — tiene su **propio motor de aprobación
autocontenido** (2 niveles fijos hoy, algoritmo estático simple:
`ResolverProximoNivel`), con la decisión de diseño documentada explícitamente: el
`IAprobacionService` del original está acoplado al modelo documental de Compras, no es
un motor genérico inyectable, así que no valía la pena forzar el acoplamiento.

Casi no habla con SAP: la única lectura real es `IDimensionCatalogoService` (para el
catálogo de centros de costo, con degradación segura si falla). El gasto en sí es una
fila propia sin documento SAP asociado — la integración contable de vuelta a SAP
(Factura de compra sobre proveedor-empleado) es **Fase 2, nunca implementada ni
siquiera como interfaz** (`IPublicadorContableSapService` no existe). Hoy el cierre es
un CSV manual que Finanzas carga a mano.

### 3.3 Modelo de datos

11 tablas propias en SQL Server (`dbo.*`, no `PORTALWEB`), todas scoped por
`EMPRESA_CODIGO` (consistente con el resto del proyecto original — acá sería
`organization_id`/`company_id` según la convención vigente). Sin EF Migrations en el
original — 12 scripts SQL numerados manuales con guardas `IF OBJECT_ID` para ser
re-ejecutables.

Tablas: `FONDO_POR_RENDIR`, `TIPO_GASTO`, `RENDICION_GASTO` (Informe),
`RENDICION_GASTO_DETALLE` (Gasto, la más grande — pasó por un rediseño real de "gasto
solo existe dentro de un informe" a "gasto suelto + informe que lo agrupa" para imitar
el flujo real de RindeGastos), `GRUPO_APROBACION_RENDICION` +
`..._MIEMBRO`/`..._NIVEL`, `RENDICION_GASTO_ACCION` (historial, fuente de verdad),
`TIPO_DOCUMENTO`, `CENTRO_COSTO_USUARIO`, `COMPROBANTE_ADJUNTO`, `POLITICA_GASTO`.

Dos tablas **decididas pero nunca creadas**: `RENDICION_GASTO_EXPORTACION` (log de
trazabilidad) y `COLABORADOR_RENDICIONES` (cargo/jefe directo/moneda base) — sin la
segunda, las políticas no pueden ser "por cargo".

### 3.4 Tamaño real

| Área | Archivos | Líneas |
|---|---:|---:|
| `Pages/` (Razor) | 37 | 3.232 |
| `Servicios/` (13 interfaces + 13 impl., dominio puro) | 29 | 1.634 |
| `Sql/` | 12 | 447 |
| CSS propio | 1 | 329 |
| `Models/` | 12 | 318 |
| `RendicionesDbContext` | 1 | 192 |
| Entrypoint + csproj | 3 | 287 |
| **Total** | **~95** | **~6.440** |

Dependencia oculta a portar también: `IConsumoServicioExternoService` (vive fuera del
plugin, en `Abstractions`/`Core.Infraestructura` del original) — gobierna límites de
cuota de Azure Maps/Document Intelligence, sin ella no compila la pantalla de Consumo
de Servicios Externos.

### 3.5 Estado de completitud

**MVP+ terminado, nunca verificado en producción real.** Cero `TODO`/`FIXME`/
`NotImplementedException` en todo el módulo, `dotnet build` limpio, 10 slices de
desarrollo documentados como "completado", con bugs reales encontrados y corregidos en
pruebas en vivo (colisión de rutas Razor, falta de atomicidad en `CrearInformeAsync`,
bug de dominio en `AdjuntarGastosAsync`). Pero:
- **Cero tests automatizados.**
- **Sin dar de alta la instancia externa** (`MODULO_INSTANCIA_EXTERNA` para
  `CLDEPOR_GASTO`) — sin eso, `ResolverConnectionStringAsync` explota al primer
  acceso.
- Credenciales Azure (OCR/Maps) no configuradas en el ambiente de referencia — sin
  ellas esas dos features estrella no corren out-of-the-box (degradan con mensaje de
  error, no rompen la app).
- Sin conversión de moneda, sin automatización de `FONDO_POR_RENDIR.ESTADO='Vencido'`.

### 3.6 Sesgos localistas a generalizar para multi-tenant (todos triviales)

- `PORCENTAJE_IVA DEFAULT 19.00` — ya es editable por empresa, solo hay que confirmar
  que el default no quede hardcodeado a Chile en el DDL nuevo.
  <br>_(el resto de las columnas ya son parametrizables por diseño)_
- `RUT_PROVEEDOR` + regex de RUT chileno en el extractor OCR — específico de Chile,
  habría que generalizar o dejar como campo de texto libre sin validación de formato
  para otros países.
- `countrySet=CL` fijo en el geocoding de Azure Maps — hay que parametrizarlo por país
  de la organización.
- **El más importante**: `DimCode 1 = Centro de Costo` está hardcodeado — en un SaaS
  multi-cliente cada organización puede tener sus dimensiones SAP configuradas
  distinto, esto tiene que ser configurable por organización, no una constante.

### 3.7 Recomendación de la investigación

Trabajo real de portado, en orden de valor:
1. **Alto valor, portable casi directo**: los 12 scripts SQL (adaptar a EF dual) y las
   ~1.634 líneas de `Servicios/` (dominio puro, mínimo acoplamiento a SAP).
2. **A rehacer**: las ~3.200 líneas de Razor Pages (mismo motivo que Importación
   Genérica — cambia el patrón de UI/persistencia).
3. **A completar de cero**: integración contable SAP (nunca escrita en el original),
   tabla de exportación/trazabilidad, tabla de colaborador con cargo, conversión de
   moneda, y toda la suite de tests.

---

## 4. Requerimiento nuevo: importador de plugins (sin precedente en el original)

A diferencia de los 3 puntos anteriores (heredar lo que ya existe), esto es
**funcionalidad nueva que el original nunca necesitó** — `PortalSAP_v2` es mono-tenant,
todos los plugins se compilan/despliegan junto con ese único cliente. En un modelo
SaaS multi-organización, instalar un plugin nuevo (o uno de un tercero) sin
recompilar/redesplegar todo el Host es un requerimiento real de plataforma.

**No hay nada de esto documentado todavía en `ARCHITECTURE.md` ni en ningún `docs/`
existente** — grep confirmado, cero menciones previas a "importador de plugins" o
"marketplace" en este repo. Esto es punto de partida, no una brecha respecto a algo ya
diseñado.

### 4.1 Lo que ya existe y sobre lo que esto se construye

`PluginManager`/`PluginLoadContext` (ya portado, `AssemblyLoadContext` aislado) hoy
**solo** descubre plugins que ya están físicamente en `artifacts/plugins/{Nombre}/
{Version}/` al arrancar el Host (`Program.cs`, antes de `builder.Build()`). No hay
ningún mecanismo para:
- Subir/instalar un plugin nuevo sin acceso al filesystem del servidor.
- Habilitar/deshabilitar un plugin **por organización** (hoy todos los plugins
  cargados están disponibles para todas las organizaciones por igual — brecha ya
  anotada en `CLAUDE.md`: "el filtrado del árbol de menú por módulos contratados
  (`organization_modules`) tampoco existe").
- Versionar/actualizar un plugin en caliente.

### 4.2 Relación directa con una brecha ya conocida

Este requerimiento converge con algo que `CLAUDE.md` ya venía señalando como pendiente
en cada entrega reciente: **`organization_modules`** — qué módulos tiene contratados
cada organización. Un importador de plugins sin ese modelo de "quién tiene acceso a
qué" solo resuelve la mitad del problema (cómo entra el código al sistema), no la
mitad comercial (quién puede usarlo). Cualquier diseño de esto debería resolver los
dos ejes juntos, no por separado.

### 4.3 Alcance a definir (no diseñado todavía, para discutir antes de planificar)

Preguntas abiertas que hay que resolver con el dueño del proyecto antes de armar un
plan de implementación real:
- ¿Quién instala un plugin — solo el administrador de plataforma (`/Admin/*`), o
  también un admin de organización puede traer un plugin propio/de un partner?
- ¿El "paquete" de plugin es un `.zip` subido por UI, o se resuelve contra un
  repositorio/feed externo (tipo NuGet privado)?
- ¿Instalar exige reiniciar el Host (aceptable en esta etapa) o tiene que ser
  verdaderamente en caliente (`PluginLoadContext` ya soporta descarga de contexto,
  pero recargar Razor Pages compiladas en caliente es harina de otro costal)?
- Validación/sandboxing de un plugin de un tercero — ¿hay algún nivel de firma o
  verificación antes de cargar código arbitrario en el mismo proceso del Host?

Este documento deja constancia del requerimiento y su punto de partida técnico; el
diseño concreto (contratos, modelo de datos, UI) se arma en un documento aparte cuando
se decida priorizarlo, siguiendo el mismo proceso de este proyecto (research →
`AskUserQuestion` para las decisiones abiertas → plan → implementación por fases
verificadas).

---

## 5. Menú de administración — árbol personalizable

**Pregunta directa del dueño del proyecto** (26 jul 2026): el menú de administración
cambió bastante respecto al original, no estaba claro si lo que hay acá es hardcodeado
o dinámico, y faltaba la pantalla de configuración. Investigado con precisión contra
el código real de los dos proyectos — sí es dinámico, pero **falta por completo el
módulo de personalización** que sí tenía el original.

### 5.1 Qué hay hoy en este proyecto (confirmado)

El árbol de `menus` **es dinámico**, no hardcodeado: cada plugin declara su menú en
código (`IModuloPortal.GetMenu()`) y `MenuSyncService.SyncAsync` (`PortalSaas.Core.
Infraestructura`) hace upsert automático contra la tabla `menus` en cada arranque del
Host — portado tal cual del original, incluida la fase que desactiva nodos de un
`OriginModule` que ya no corresponde a ningún plugin cargado.

Lo único que existe para "administrar" el menú es `/Admin/MenuGroups` — **asigna**
nodos ya existentes a un `MenuGroup` (checkboxes indentados por `Level`), pero **no
edita el árbol en sí**: no se puede renombrar un nodo, reordenarlo, ocultarlo, ni crear
una carpeta o página manual que no venga de un plugin. Confirmado por inspección
directa: `src/PortalSaas.Host/Pages/Admin/` solo tiene `MenuGroups/`, no hay ningún
`Menus/`; la entidad `Menu` (`src/PortalSaas.Data/Entities/Menu.cs`) no tiene ninguna
columna de override (nada equivalente a `NombrePersonalizado`/`OcultoPorAdmin`); y
`MenuSyncService.SyncAsync` pisa `menu.Name`/`menu.Icon`/`menu.PagePath`/`menu.Order`
**incondicionalmente** en cada nodo de módulo, en cada arranque — cualquier edición
manual directa sobre esas columnas se revertiría sola en el próximo restart del Host.

### 5.2 Qué tenía el original — módulo completo, no solo dos columnas

`IMenuAdminService`/`MenuAdminService` (`PortalSAP.Core.Administracion`, 445 líneas) +
`Pages/Menus/Index.cshtml(.cs)` (`plugins/Modulo.Administracion`, 786 líneas) — **~1.255
líneas**, pantalla real "Árbol de menús" en el backoffice. Capacidades reales
(`IMenuAdminService`, contrato completo):

- **`ListarArbolAsync`** — árbol completo (activos, inactivos, ocultos), DFS con nivel
  para indentar, nombre ya resuelto con el override si existe.
- **Nodos manuales** (`OriginModule = "Manual"`, no vienen de ningún plugin) —
  `CrearCarpetaAsync`/`CrearPaginaAsync` (alta), `EditarCarpetaAsync`/
  `EditarPaginaAsync` (edición real), `EliminarNodoAsync` hace un `DELETE` físico de
  verdad (falla si tiene hijos o está asignado a algún `MenuGroup`/`UserMenuProfile`).
  Sirven para agrupar/enlazar cosas que no son un módulo (ej. un link externo, una
  carpeta puramente organizativa).
- **Nodos de módulo** (`OriginModule != "Manual"`) — **nunca** se editan `Nombre`/
  `Activo` directamente (el sync los pisaría en el próximo restart, exactamente el
  problema que confirmé en §5.1). En cambio:
  - `EditarNodoDeModuloAsync(id, nombrePersonalizado, ordenPersonalizado)` — guarda el
    override en columnas nuevas que `MenuSyncService` nunca toca
    (`NOMBRE_PERSONALIZADO`/`ORDEN_PERSONALIZADO`, ver `db/hana/012_menu_override_admin.sql`,
    24 líneas). `ordenPersonalizado = null` = sin override, vuelve a usarse el que
    declara el plugin.
  - `EliminarNodoAsync` sobre un nodo de módulo **no borra** — marca
    `OCULTO_POR_ADMIN = true` (persistente, un `DELETE` físico no serviría porque el
    plugin lo va a volver a declarar en el próximo sync). Excluido del sidebar real
    pero visible en el árbol de Administración para poder revertirlo.
  - `RestaurarNodoDeModuloAsync(id)` — deshace el ocultamiento.

### 5.3 Brecha concreta

| Capacidad del original | Estado acá |
|---|---|
| Ver el árbol completo (activos/inactivos/ocultos) en una pantalla | ❌ No existe — solo se ve indirectamente vía `/Admin/MenuGroups` (checkboxes planos, sin jerarquía visual completa) |
| Renombrar un nodo de módulo sin que el próximo sync lo revierta | ❌ No existe el mecanismo de override (`NombrePersonalizado`) |
| Reordenar un nodo de módulo | ❌ No existe (`OrdenPersonalizado`) |
| Ocultar un nodo de módulo puntual (sin tocar el plugin) | ❌ No existe (`OcultoPorAdmin`) |
| Crear una carpeta/página manual (no ligada a ningún plugin) | ❌ No existe el concepto de nodo `Manual` en el modelo (`Menu.OriginModule` siempre es un `ModuleCode` real) |

### 5.4 Nota de diseño para el port: `organization_id`

En el original el árbol de menú es global (una sola instalación, un solo cliente). En
este proyecto `menus` también es GLOBAL a la plataforma (mismo criterio que
`MenuGroup`/`Profile`, ver `docs/03-MODELO-CORE-COMERCIAL.md` §3) — un override
(`NombrePersonalizado`/`OcultoPorAdmin`) hecho por el administrador de plataforma
afecta a **todas** las organizaciones por igual, no hay (todavía) un concepto de
"personalización de menú por organización". Si al portar esto se decide que cada
organización necesita poder ocultar/renombrar nodos para sí misma sin afectar a las
demás, es un modelo de datos distinto (scoped por `organization_id`), no una
extensión directa de las dos columnas del original — decisión a confirmar con el
dueño del proyecto antes de portar, no asumir cuál de las dos quiere.

### 5.5 Tamaño estimado de portar

≈ 1.255 líneas del original (445 servicio + 786 UI + 24 SQL) más las migraciones EF
dual (Postgres/SQL Server) para las 2 columnas nuevas — comparativamente barato, sin
dependencias de catálogos SAP ni de otros módulos pendientes (a diferencia de
Importación Genérica o Rendiciones).

---

## 6. Resumen ejecutivo — priorización sugerida

No es una decisión tomada, es un punto de partida para que el dueño del proyecto
priorice:

| Bloque | Tamaño estimado | Depende de | Nota |
|---|---:|---|---|
| Completar tabs Logística/Finanzas + catálogos faltantes en Venta/Compra | ~mediano | nada nuevo | Cierra la brecha #1 más barata, catálogos ya tienen patrón conocido (`CustomerCatalogService`, etc.) |
| **Importador de líneas CSV del formulario** (§1.0 #1 y #2) | ≈ 460 líneas | los mismos catálogos de Cuenta Mayor/Dimensión de arriba | El más barato de los 4 bloques — quick win real, sin UI nueva (se integra en la tab Contenido que ya existe) |
| Motor de aprobación genérico (`IAprobacionService`) | ~grande | nada nuevo | Único bloqueante real para que Compras funcione "como el original" (Oferta→Pedido vía Copy-From al aprobar) |
| Módulo Importador Genérico (masivo, §2) | ≈ 5.760 líneas | motor de aprobación NO es prerrequisito, pero si se quiere Servicio+campos de usuario, sí depende de los 6-7 catálogos faltantes | El más caro de los 5, pero el de mayor apalancamiento (sirve a los 3 motores a la vez) |
| Módulo Rendiciones | ≈ 6.440 líneas original (bajará al reescribir Razor) | nada de lo anterior — es independiente | El único con visión de producto SaaS aparte desde el diseño original; el que más se acerca a "vender esto a cualquier organización" tal cual |
| **Árbol de menús personalizable** (§5) | ≈ 1.255 líneas | nada nuevo | Barato y sin dependencias — candidato real a hacerse temprano, antes de que se acumulen más plugins que necesiten reordenarse/ocultarse |
| Importador de plugins | sin dimensionar (requiere definir alcance primero) | modelo `organization_modules` (brecha ya conocida, sin resolver) | Es infraestructura de plataforma, no un módulo de negocio — bloquea la venta real a un segundo cliente si no existe, pero no bloquea seguir construyendo módulos de negocio mientras tanto |

**Ninguno de estos 6 bloques está iniciado hoy** salvo Venta/Compra/Inventario, que
están parcialmente completos (ver §1). El resto (importador de líneas CSV, Importador
Genérico masivo, Rendiciones, árbol de menús personalizable, Importador de plugins)
está en **0% de avance de código** en este proyecto.
