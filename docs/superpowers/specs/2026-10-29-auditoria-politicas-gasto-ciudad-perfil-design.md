# Auditoría y políticas de gasto por ciudad y perfil de usuario

## Intención y por qué

Hoy la validación de gastos contra topes (viáticos) la hace una persona a mano:
revisa cada línea de una planilla Excel contra una tabla de topes por ciudad
(ej. Santiago: almuerzo máx $6.000, cena $0, refresco/café $1.400, carga Bip
$1.700/día) y corrige el monto a reembolsar escribiendo "(Ajustado a
Presupuesto)" en la glosa. Esa tabla de topes también varía según el tipo de
persona: un supervisor en terreno tiene además un tope **semanal acumulado**
de combustible ($25.000), distinto del control por boleta.

El objetivo es automatizar ese cálculo dentro de Modulo.Rendiciones: que el
sistema calcule el monto a reembolsar según la política vigente (tipo de
gasto + ciudad, y opcionalmente perfil del usuario) en el momento en que se
digita el gasto, mostrando la advertencia correspondiente, y que el auditor
vea ese cálculo ya hecho (con el detalle de qué política se aplicó) al
revisar el informe, en vez de recalcularlo a mano.

Principio central, explícito del pedido original: **el monto de la boleta
nunca se restringe** (la persona siempre puede cargar el monto real del
documento). Lo que la política ajusta es el **monto a reembolsar**, que
puede quedar por debajo del monto de la boleta cuando corresponde.

## Alcance actual (research, no se repite en el plan)

- `ExpensePolicy` hoy es 1 fila por (CompanyId, ExpenseTypeId), con un único
  `MaxAmount` y un flag `IsBlocking` que **impide guardar** (lanza excepción
  en `ExpenseService.CreateAsync/UpdateAsync`) si se supera. No hay ciudad.
- `ExpenseReportLine.Amount` es el único campo de monto. No existe "monto a
  reembolsar" separado.
- No existe ningún concepto de Ciudad en el modelo.
- No existe ningún tope agregado/periódico (semanal, mensual) ni nada
  asociado a un perfil de usuario distinto del rendidor genérico.
- Los roles de acceso del módulo (`Models/RendicionesRoles.cs`) son un
  conjunto fijo de 3: Rendidor, Aprobador, Administrador. No son
  extensibles y no tienen relación con "tipo de persona para efectos de
  política" (ej. "Supervisor en terreno").
- Los catálogos de mantención existentes (Tipos de Gasto, Tipos de
  Documento) siguen el mismo patrón: tabla por compañía, pantalla en
  `Pages/Configuracion/<Catalogo>/Index`, servicio `I<Catalogo>Service`.

## Goals

1. Ciudad como dato de cada **gasto** (no del informe completo) — confirmado
   explícitamente: un mismo informe puede tener gastos de distintas
   ciudades.
2. Separar "Tipos de Gasto" que hoy están mezclados (ej. una sola cuenta
   contable "Colación y Movilización" cubre almuerzo, cena, refresco, carga
   Bip) en tipos independientes, para poder ponerle tope a cada uno por
   separado. Esto es un pedido explícito del usuario ("mi propuesta es poder
   separar los tipos de gastos para poder controlar"), no solo un medio
   técnico.
3. Política de monto por (Tipo de Gasto × Ciudad): igual mecanismo que hoy
   (`ExpensePolicy`), pero con Ciudad como dimensión adicional, opcional.
   Esta política **también** solo afecta a usuarios con un perfil de
   política asignado que la tenga configurada — confirmado explícitamente:
   "la política de rango por ciudad también aplica solo a un perfil de
   usuario, ya que otros no manejan esa restricción." No es exclusivo del
   tope semanal del punto 7; el mismo mecanismo de perfil aplica a
   cualquier política, por ciudad o semanal.
4. Cada línea de gasto guarda **monto de boleta** (ya existe, `Amount`) y
   **monto a reembolsar** (nuevo, calculado), nunca editado directamente por
   el rendidor.
5. El recálculo se muestra como advertencia **no bloqueante** en el momento
   de digitar/editar el gasto: "Supera el tope para Almuerzo en Santiago
   ($6.000) — se reembolsará $6.000 de los $8.500 de la boleta."
6. Soporte para un tipo de gasto cuyo tope es **por día** pero se carga de
   una sola vez cubriendo varios días (caso concreto: carga de tarjeta Bip,
   que es una recarga única de saldo, no un gasto diario). La línea permite
   indicar cuántos días cubre esa carga, y el tope aplicado es
   `tope_diario × días`.
7. Tope **semanal acumulado**, aplicable solo a usuarios con un perfil de
   política específico (ej. "Supervisor en terreno" + Combustible + $25.000
   por semana), usando el mismo modelo de políticas (no un subsistema
   aparte) — pedido explícito: "esto debería ser parte de las mismas
   políticas... este perfil asignado a este tipo de grupo de usuario."
8. Catálogo de "Perfil de Política" editable por company (no una lista fija
   en código), asignable por usuario, **separado** de los 3 roles de acceso
   existentes (Rendidor/Aprobador/Administrador) — son dos dimensiones
   distintas (acceso vs. qué políticas le aplican a esa persona).
9. El auditor/aprobador ve, al revisar un informe, qué líneas fueron
   ajustadas por una política y por qué (boleta vs. reembolso, política
   aplicada), y ve la advertencia de sobregiro semanal agregada cuando
   corresponde, para decidir manualmente si hay que intervenir alguna
   línea puntual.

## Non-goals

- No se modela el flujo de firmas "Jefe Directo" / "Auditor Interno" de la
  planilla en papel — eso es un tema de niveles de aprobación ya resuelto
  por `ExpenseApprovalGroup` y queda fuera de este diseño (dicho
  explícitamente por el usuario).
- No se construye un motor de reglas genérico/configurable con
  expresiones arbitrarias. Las reglas soportadas son exactamente las que
  pidió el usuario: tope por (tipo, ciudad) por documento, con multiplicador
  de cantidad opcional, y tope acumulado por periodo (semanal) por (tipo,
  perfil). Cualquier otra forma de regla queda fuera de este alcance.
- No se decide automáticamente **qué línea recortar** cuando el tope
  semanal se supera entre varias boletas — eso lo decide el auditor a mano,
  a propósito (ver Decisión 5).
- No se migran datos históricos de la planilla Excel a este modelo.
- No se carga la data real de catálogos (Tipos de Gasto separados por
  Almuerzo/Cena/Refresco/Bip, montos de la tabla de viáticos, perfiles de
  usuario) como parte de esta entrega — el usuario la carga a mano desde
  las pantallas de administración una vez construidas. Este trabajo entrega
  el mecanismo, no los datos.
- No se cambia nada del flujo OCR/Gemini existente; el cálculo de política
  corre sobre el monto ya capturado (por OCR o manual), igual para ambos
  orígenes.

## Anti-goals (sería un fracaso aunque "funcione")

- Que alguna política llegue a bloquear el guardado de un gasto por monto.
  El pedido es expreso y repetido: la boleta nunca se restringe.
- Que el perfil de política quede acoplado a los roles de acceso (Rendidor/
  Aprobador/Administrador), de forma que cambiar el perfil de alguien
  afecte sin querer sus permisos, o viceversa.
- Que una política semanal "adivine" cuál boleta recortar y el auditor no
  tenga forma de ver el detalle de cómo se llegó al número agregado.
- Que separar los Tipos de Gasto rompa reportes/exportaciones que hoy
  agrupan por la cuenta contable SAP (`ExpenseType.SapGlAccount`) — varios
  tipos nuevos deben poder apuntar a la misma cuenta, igual que hoy.

## Constraints

- Las migraciones de este módulo van contra la base propia de Rendiciones
  (`ps_comdepor_rg` / `ps_comdepor_rg_qa`), **no** las 4 bases de
  plataforma — ver memoria `rendiciones-gemini-cuota-diaria.md`.
- Toda URL interna nueva usa `~/` (PathBase `/Depor/`) — regla dura del
  repo.
- Toda pantalla nueva es responsive a 375px y sigue el diseño/clases de una
  pantalla hermana existente (no estilos propios) — reglas duras del repo.
- `ExpenseType.SapGlAccount` sigue siendo de cardinalidad N:1 con la cuenta
  contable: separar tipos (Almuerzo/Cena/Refresco/Bip) no cambia a qué
  cuenta contable apuntan si hoy comparten una.

## Decisiones (cómo se construye)

### 1. Catálogo `City` (Ciudad)

Tabla nueva `expense_cities` (o nombre equivalente), mismo patrón que
`ExpenseType`/`DocumentType`: `Id`, `CompanyId`, `Name`, `IsActive`.
Pantalla de mantención en `Pages/Configuracion/Ciudades/Index`, copiando
estructura de `Pages/Configuracion/TiposDocumento/Index` (regla de diseño/
layout como módulos hermanos).

`ExpenseReportLine` suma `CityId` (nullable — no todos los tipos de gasto
necesitan ciudad, ej. Insumos de Oficina). Lo elige el rendidor en un combo
al digitar el gasto, visible solo si el `ExpenseType` elegido tiene alguna
política que varíe por ciudad (si no, no tiene sentido pedirlo).

### 2. `ExpenseType` gana un flag para habilitar cantidad

`ExpenseType` suma:
- `RequiresQuantity` (bool) — si es true, la línea pide un campo "Cantidad"
  (ej. "Días") además del monto.
- `QuantityLabel` (string?, ej. "Días") — rótulo del campo, para que sirva
  también a futuros tipos con otra unidad sin tocar código.

`ExpenseReportLine` suma `Quantity` (decimal?, default 1 cuando no aplica).
Caso concreto: el Tipo de Gasto "Movilización (Bip)" se marca
`RequiresQuantity=true`, `QuantityLabel="Días"`; una carga de $5.100 que
cubre 3 días se guarda con `Amount=5100`, `Quantity=3`.

### 3. `PolicyProfile` (Perfil de Política) — catálogo nuevo, separado de los roles de acceso

Tabla nueva `policy_profiles`: `Id`, `CompanyId`, `Name` (ej. "Supervisor en
terreno"), `IsActive`. Tabla de asignación `user_policy_profiles`: `UserId`,
`CompanyId`, `PolicyProfileId` (un usuario tiene como máximo un perfil
activo a la vez, igual de simple que hoy es "tope único por tipo").

Pantalla de mantención del catálogo en
`Pages/Configuracion/PerfilesPolitica/Index` (mismo patrón que Tipos de
Gasto). La asignación por usuario se agrega como un campo más en
`Pages/Configuracion/UsuariosRoles/Index` (donde ya se gestiona
Rendidor/Aprobador/Administrador por persona) — un combo "Perfil de
política" junto a los checkboxes de rol, dejando explícito en la UI que es
una cosa distinta del rol de acceso.

### 4. `ExpensePolicy` extendida

Se agregan tres columnas:
- `CityId` (nullable FK) — `null` = aplica a cualquier ciudad (o el tipo no
  distingue por ciudad, ej. Combustible, Alojamiento).
- `PolicyProfileId` (nullable FK) — `null` = aplica a cualquier usuario;
  con valor, solo aplica a usuarios con ese perfil asignado. En la práctica
  esto se usa tanto para las políticas por ciudad (viáticos: almuerzo,
  cena, refresco, Bip) como para la semanal de combustible — un usuario
  sin perfil asignado, o con un perfil que no tiene ninguna política
  configurada, no tiene ninguna restricción: `ReimbursementAmount = Amount`
  siempre, sin advertencia.
- `Period` (enum/string: `"PerDocument"` | `"Weekly"`, default
  `"PerDocument"`) — determina si el tope se evalúa gasto por gasto o
  acumulado en la semana (lunes a domingo, según `ExpenseReportLine.Date`).

Se **elimina** `IsBlocking` — ninguna política vuelve a impedir el guardado;
el único efecto de superar un tope es advertencia + recálculo de
`ReimbursementAmount`. El índice único pasa de
`(CompanyId, ExpenseTypeId)` a `(CompanyId, ExpenseTypeId, CityId,
PolicyProfileId)`, permitiendo varias filas por tipo (una por ciudad, y
opcionalmente una semanal por perfil). Postgres trata NULL como distinto
de NULL en un índice único plano, así que un índice único "a secas" no
evitaría dos filas "default" (`CityId` y `PolicyProfileId` ambos null)
para el mismo tipo, lo que rompería la resolución de "primera que
matchee" de más abajo — el builder debe usar un índice único parcial por
combinación de nulos (o una columna calculada tipo `COALESCE(CityId, 0)`/
`COALESCE(PolicyProfileId, 0)` dentro del índice) para que la unicidad
real se cumpla. Mi llamada, no requiere decisión del usuario.

Resolución al buscar la política aplicable a una línea (en este orden,
primera que matchee):
1. `(ExpenseTypeId, CityId = ciudad de la línea, PolicyProfileId = perfil
   del usuario)` si el usuario tiene perfil.
2. `(ExpenseTypeId, CityId = ciudad de la línea, PolicyProfileId = null)`.
3. `(ExpenseTypeId, CityId = null, PolicyProfileId = perfil del usuario)`
   si el usuario tiene perfil.
4. `(ExpenseTypeId, CityId = null, PolicyProfileId = null)`.

La pantalla `Pages/Configuracion/PoliticasGasto/Index` pasa de "una fila
por tipo" a una grilla que permite varias filas por tipo (agrupadas
visualmente), con selector de Ciudad, Perfil y Periodo por fila.

### 5. Cálculo del reembolso

`ExpenseReportLine` suma `ReimbursementAmount` (decimal, no nullable,
default = `Amount`). Se recalcula en `ExpenseService` cada vez que se crea
o edita una línea (mismo lugar donde hoy se llama a
`IExpensePolicyService.ValidateAsync`):

- Se busca la política `Period="PerDocument"` aplicable (resolución de
  arriba). Si existe y tiene `MaxAmount`:
  `tope = MaxAmount × (Quantity ?? 1)`;
  `ReimbursementAmount = Min(Amount, tope)`.
  Si `Amount > tope`, se agrega una advertencia (no bloqueante) con el
  detalle tipo/ciudad/tope/boleta/reembolso.
- Si no hay política aplicable, `ReimbursementAmount = Amount` (sin
  advertencia).
- Las políticas `Period="Weekly"` **no** tocan `ReimbursementAmount` de la
  línea individual. Se evalúan aparte (punto siguiente) porque recortar
  una boleta específica de varias de la semana es una decisión que le
  corresponde al auditor, no al sistema — ver anti-goals.

### 6. Advertencia semanal agregada (políticas `Period="Weekly"`)

Servicio nuevo (o método en `ExpensePolicyService`) que, dado un usuario y
una semana (lunes a domingo según `Date` de cada línea), para cada
`ExpenseType` con una política semanal aplicable a su perfil:
suma `ReimbursementAmount` de las líneas de ese tipo en esa semana
(incluyendo sueltas e in-report, **excluyendo** las que pertenecen a un
informe `Rejected` — un informe rechazado no llegó a reembolsarse, así que
sus líneas no deben sumar al acumulado semanal; decisión del builder-check,
mi llamada) y compara contra `MaxAmount`. Una política `Period="Weekly"`
siempre tiene `CityId = null` (no se admite tope semanal distinto por
ciudad en este alcance; la grilla de administración oculta el selector de
Ciudad cuando el periodo elegido es Semanal) — mi llamada, confirmada en
el builder-check como ambigüedad real del documento original. Si excede,
produce una advertencia agregada: "Combustible semana del 26 al 1-nov:
$30.000 gastados, tope semanal $25.000 (Supervisor en terreno) — excede en
$5.000."

Esta advertencia se muestra:
- Al rendidor, en Gastos/Detalle, cuando el tipo de gasto elegido tiene una
  política semanal activa para su perfil (igual que hoy se muestra el
  tope por documento).
- Al auditor/aprobador, en Informes/Detalle y Aprobaciones, junto al
  detalle de cada línea de ese tipo, para que decida si ajusta algo a mano
  antes de aprobar.

### 7. `ReimbursementAmount` es el monto real a pagar, no solo informativo

Confirmado explícitamente: "el monto a pagar debe ser el de la política... solo si el monto [de la política] es menor al de la boleta." Esto significa que `ReimbursementAmount` no queda solo como dato mostrado en pantalla — reemplaza a `Amount` en todo lugar donde hoy se suma el monto para un total que se paga, se liquida o se exporta:

- `ExpenseFundService.CalculatePendingBalanceAsync` (línea ~36): el saldo pendiente de un fondo por rendir hoy resta `SumAsync(d => d.Amount)` de las líneas de informes `Approved`. Pasa a restar `d.ReimbursementAmount`.
- `ClosingReportService.GetLinesAsync` (línea ~88, el cierre que se exporta/contabiliza): hoy arma `ClosingReportLine` con `line.Amount`. Pasa a usar `line.ReimbursementAmount` como el monto exportado/contabilizado. El `ClosingReportLine` debe seguir incluyendo también `Amount` (monto de boleta) como columna aparte, para que quien reciba el export pueda ver ambos valores y el ajuste no quede oculto.
- Cualquier total mostrado en Informes/Detalle y Aprobaciones como "monto a aprobar/pagar" del informe debe ser la suma de `ReimbursementAmount` de sus líneas, no de `Amount`. El total de `Amount` (monto de boletas) se mantiene visible como dato secundario, nunca se oculta.

No hay otros lugares del código que sumen `Amount` fuera de la UI (confirmado por grep sobre `Servicios/`); los únicos dos server-side son los de arriba.

### 8. Vista del auditor

En Informes/Detalle (y el resumen de cada gasto, ya rediseñado para
parecerse a Mis Gastos), cada línea con `ReimbursementAmount < Amount`
muestra un indicador visual (ej. badge "Ajustado a tope") y, al expandir,
el detalle: "Boleta: $8.500 · Reembolsable: $6.000 · Política: Almuerzo /
Santiago ($6.000)". Las advertencias semanales agregadas del punto 6
aparecen como una sección aparte dentro del detalle del informe (no por
línea, porque son por semana/tipo, no por boleta).

## Lo que queda a criterio del builder

- Nombres exactos de tablas/columnas en snake_case siguiendo la convención
  ya usada en `RendicionesDbContext` (ej. `expense_cities`,
  `policy_profiles`, `user_policy_profiles`, columnas nuevas en
  `expense_policies` y `expense_report_lines`).
- Si la resolución de política (orden de fallback del punto 4) se
  implementa con 4 queries o una sola query con `ORDER BY` por
  especificidad — el comportamiento debe ser el descrito, la
  implementación es libre.
- Mensajes de advertencia exactos (texto visible al usuario) — deben seguir
  el tono y formato ya usado en `ExpensePolicyService.ValidateAsync`
  (`"El monto (...) supera el tope ... (...)"`), en español, con montos en
  `N0`.
- Si `ExpenseValidationResult` (hoy `record(bool Blocked, List<string>
  Warnings)`) se extiende con el `ReimbursementAmount` calculado o ese
  cálculo vive en un método aparte — ambos son razonables, decide el que
  ensucie menos `ExpenseService`.
- Orden de migraciones (catálogos primero, luego columnas que los
  referencian) y si se hace en una sola migración o varias — deben quedar
  aplicadas en `ps_comdepor_rg` y `ps_comdepor_rg_qa` antes de dar el
  trabajo por terminado, por la regla de migraciones del módulo.
- Cobertura de tests: como mínimo, un test por cada regla de negocio nueva
  listada en Goals (resolución de política con los 4 niveles de fallback,
  multiplicador por cantidad, tope semanal no afecta líneas individuales,
  ninguna política bloquea guardado).
