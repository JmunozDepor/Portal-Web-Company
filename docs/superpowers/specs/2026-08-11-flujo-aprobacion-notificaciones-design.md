# Flujo completo gasto→informe→aprobación con notificaciones — Entrega 1 (sin SAP)

Estado: **aprobado por el dueño del proyecto (11 ago 2026)**, pendiente de convertir en plan de implementación.

## Contexto

El módulo tiene el flujo de negocio (`ExpenseService`, `ExpenseReportService`,
`ExpenseApprovalGroupService`) y las páginas (`Gastos/`, `Informes/`, `Aprobaciones/`)
portadas y compilando desde la Fase 3, pero **nunca se probó de punta a punta con un
usuario real ni se avisa al aprobador** de que tiene algo pendiente — hoy tiene que
entrar a `Aprobaciones/Index` por su cuenta a revisar. Ver `PENDIENTE.md` para el
estado completo del módulo.

Pedido explícito del dueño del proyecto (11 ago 2026): antes de tocar SAP, dejar
andando y probado el ciclo completo (registrar gasto → armar informe → enviar →
aprobar/rechazar) con aviso al aprobador **en pantalla y por correo**, más un
**recordatorio diario** de pendientes con horario configurable. Los módulos de
administración existentes (Tipos de Gasto, Grupos de Aprobación, Políticas, etc.) se
dan por válidos tal cual están — no se rediseñan en esta entrega.

## Alcance de Entrega 1

**Incluido:**
- Flujo gasto → informe → enviar → aprobar/rechazar, verificado con clic real en
  navegador con usuario tenant autenticado (retoma el bloqueo pendiente en
  `PENDIENTE.md` §"Orden de prioridad de los pendientes", punto 3 — crear el usuario
  de prueba en Comercial Depor).
- Correo al aprobador cuando le toca actuar (envío inicial y cada vez que sube de
  nivel), y correo al dueño del informe cuando se aprueba/rechaza definitivamente.
- Contador de pendientes en la propia página `Aprobaciones/Index` (no badge de menú).
- Recordatorio diario por correo, un resumen por aprobador agrupando todos sus
  informes pendientes, con hora configurable desde una pantalla admin nueva del
  propio plugin.
- Fondo por Rendir: se mantiene 100% manual, como hoy — no se toca en esta entrega.

**Fuera de alcance de Entrega 1** (quedan para después o para Entrega 2):
- Integración SAP (Fase 7 completa de `PENDIENTE.md`): amarrar `ExpenseFund` a su
  documento SAP real, caja chica de tienda compartida, posteo de rendiciones
  aprobadas de vuelta a SAP.
- Centro de notificaciones con historial/marcar leído.
- Badge dinámico en el menú lateral (requeriría tocar infraestructura compartida del
  Core, `MenuNavigationService`/`SidebarMenuViewComponent` — evaluado y descartado
  para esta entrega por alcance/riesgo).
- Canales de notificación distintos a correo (SMS, push).
- Validación/rediseño de las pantallas de administración existentes del plugin.

## Decisiones de diseño

### 1. Notificación por correo — reusa infraestructura existente, sin eventos nuevos
El portal ya tiene `IEmailSenderService`/`EmailMessage`
(`PortalSaas.Abstractions.Contratos.IEmailSenderService`), usado hoy por
`ForgotPasswordModel` (`Portal SaaS - Core/.../Account/ForgotPassword.cshtml.cs`) como
precedente de patrón (armar el HTML inline, invocar `SendAsync(organizationId,
message)`). `ExpenseReportService.cs` no tiene eventos ni hooks — las transiciones son
llamadas directas terminando en `SaveChangesAsync`. Se inyecta `IEmailSenderService`
directo en `ExpenseReportService` y se invoca justo después de cada
`SaveChangesAsync` relevante:

- `SubmitAsync` (hoy calcula el nivel 1 vía `ResolveNextLevel`) → correo al aprobador
  de `ExpenseApprovalGroupLevel.UserId` del nivel 1.
- `ApproveAsync` → si queda otro nivel, correo al aprobador del siguiente nivel; si
  fue el último nivel (`Status` pasa a `Approved`), correo al dueño del informe.
- `RejectAsync` → correo al dueño del informe con el motivo de rechazo.

Se respeta `UserPreference.EmailNotificationsEnabled` (ya existe, default `true`)
antes de enviar cualquier correo de este flujo. **No se notifica en paralelo a todos
los niveles** — solo al aprobador del nivel que corresponde actuar ahora, reflejando
el flujo secuencial real (decisión ya validada con el dueño del proyecto).

**Manejo de error**: si `SendAsync` falla (ej. organización sin proveedor de correo
configurado en `EmailSettings`), se loguea y el flujo de negocio (aprobar/rechazar/
enviar) **continúa igual** — un correo caído nunca debe bloquear una transición de
estado real.

### 2. Aviso en pantalla — contador en la propia página, no badge de menú
`MenuItemDefinition`/`Menu` del Core son estáticos (se sincronizan una sola vez al
levantar el Host, compartidos entre organizaciones, sin campo de contador) y ningún
plugin del portal tiene hoy un badge dinámico ahí — agregarlo requeriría modificar
`MenuNavigationService`/`SidebarMenuViewComponent`, infraestructura compartida por
todos los plugins. Evaluado con el dueño del proyecto y descartado por alcance/riesgo
para esta entrega.

En su lugar: `Aprobaciones/Index.cshtml.cs` (que ya lista los pendientes del
aprobador actual) agrega un texto/alerta con el conteo al cargar la página. Cero
cambios al Core, cero tablas nuevas para esto.

### 3. Recordatorio diario — `BackgroundService` propio + config en tabla del plugin
Mismo patrón que `LicenseActivatorBackgroundService`
(`Portal SaaS - Core/.../Comercial/Licenciamiento/LicenseActivatorBackgroundService.cs`):
`BackgroundService` con `IServiceScopeFactory` para crear scope por ciclo y
`Task.Delay` entre iteraciones. Nuevo `RendicionesReminderBackgroundService`,
registrado con `AddHostedService` en `RegisterServices` del plugin.

- Cada ciclo (cada ~15 min) compara la hora actual contra la hora configurada; si
  coincide y no se envió ya hoy, agrupa **por aprobador** todos los informes con
  `Status=Pending` donde ese aprobador es el dueño del `CurrentLevel`, y manda **un
  solo correo resumen** por aprobador (no uno por informe).
- Si una organización no tiene proveedor de correo configurado, se salta esa
  organización y sigue con las demás — no aborta el job completo por un tenant mal
  configurado.

**Configuración**: no existe en el portal ningún mecanismo de "settings globales"
reusable (`PlatformAdmin`/`PlatformModule` son de propósito específico, `EmailSettings`
es por-organización, no hay tabla clave-valor genérica). Se descartó crear un concepto
nuevo de settings global en el Core (mayor alcance, afecta a toda la plataforma) a
favor de una tabla propia del plugin — mismo criterio ya aplicado en el módulo con
`ExternalServiceUsage`: "si un segundo plugin necesita lo mismo, se promueve a
`PortalSaas.Abstractions`"; hoy es un caso único de Rendiciones.

## Modelo de datos nuevo

Dos tablas en `RendicionesDbContext`, mismo criterio de nombres que las 13 existentes
(`snake_case`, `bigint identity`, columnas `_at`/`is_`), migraciones EF Core
generadas y aplicadas contra Postgres y SQL Server como el resto del plugin:

- **`rendiciones_settings`** — fila única (singleton): `ReminderHour` (`TimeOnly`),
  `ReminderEnabled` (`bool`, default `true`).
- **`rendiciones_reminder_log`** — dedupe de envíos: `SentDate` (`DateOnly`, único) —
  evita reenviar el mismo día si el servicio reinicia.

Pantalla admin nueva mínima: `Configuracion/Notificaciones/Index` (hora + on/off),
agregada al grupo de menú "Administrador" del plugin.

## Manejo de errores (resumen)

- Falla de envío de correo → log, no revierte ni bloquea la transacción de negocio.
- Sin `ExpenseApprovalGroup` asignado al usuario → se mantiene el comportamiento
  actual (auto-aprobación), sin intento de notificación.
- Reminder con organización sin `EmailSettings` configurado → se salta esa
  organización, sigue con las demás.

## Pruebas

- Unit tests de `ExpenseReportService` verificando que se invoca
  `IEmailSenderService.SendAsync` con el destinatario correcto en Submit/Approve/
  Reject (mock del servicio).
- Unit test de la lógica de agrupación del reminder (dado un set de informes
  pendientes, arma correctamente el diccionario aprobador→lista).
- Prueba manual end-to-end en navegador con usuario tenant real — retoma el punto ya
  bloqueado en `PENDIENTE.md` (crear usuario de prueba en Comercial Depor).

## Entrega 2 (fuera de este spec)

Retoma la Fase 7 ya descrita en `PENDIENTE.md`: amarrar `ExpenseFund` a su documento
SAP real, soportar caja chica de tienda (fondo compartido), y postear rendiciones
aprobadas de vuelta a SAP en "algún estado por revisar". Requiere antes una sesión de
negocio con Comercial Depor para las tres decisiones pendientes descritas ahí — se
diseñará en su propio spec cuando esas decisiones existan.
