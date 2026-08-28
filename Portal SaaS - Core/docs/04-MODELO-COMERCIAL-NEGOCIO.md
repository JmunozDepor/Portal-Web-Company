# Modelo comercial de negocio

Complementa `03-MODELO-CORE-COMERCIAL.md` (el modelo técnico/esquema) con el
planteamiento de negocio que debe llenar esas tablas: a quién se le vende, qué se
vende, cómo se cobra y en qué tiers. Es un **punto de partida a validar**, no una
tarifa cerrada — los tiers/precios de §4 son propuesta, no definición final.

## 1. A quién se le vende

**Mercado objetivo**: empresas medianas en Chile/LatAm que usan SAP Business One
(HANA o SQL Server) — típicamente entre USD 5M y USD 200M de facturación, el segmento
donde vive SAP B1. Dolor real que resuelve cada producto:

- **PortalSAP (núcleo + módulos genéricos + aprobación)**: SAP B1 nativo no tiene
  workflows de aprobación jerárquica configurables ni una capa de digitación
  simplificada para usuarios sin licencia SAP — hoy se resuelve con desarrollo a
  medida caro o add-ons de terceros.
- **WMS_Suite**: integrar un WMS con SAP B1 es normalmente un proyecto de meses
  tercerizado — ser "el integrador ya probado" es una propuesta de valor fuerte una
  vez generalizado (ver `00-HISTORIAL-DECISIONES.md`, plan de acción ítem 4:
  adaptadores formales).

**Canal de venta — decisión pendiente de tomar, no excluyentes entre sí:**

- **Venta directa** a empresas usuarias de SAP B1.
- **White-label / reventa vía partners SAP B1 (VARs)** — el ecosistema SAP B1 en
  Chile/LatAm se vende mayoritariamente vía partners implementadores, no directo del
  fabricante. Un partner lo instala/soporta para su propia cartera de clientes, con
  margen para él — baja el costo de adquisición porque el partner ya tiene la
  confianza del cliente y conoce su SAP.

## 2. Qué se vende — módulos como unidades comerciales

Usa `platform_modules`/`plan_modules` (ver `03-MODELO-CORE-COMERCIAL.md`):

| Módulo | Tipo |
|---|---|
| Núcleo (login, menú, multi-empresa, permisos) | Incluido en todo plan — no se vende suelto |
| Motor de aprobación (workflow configurable) | Módulo pagado |
| `GenericoVenta`/`GenericoCompra`/`GenericoInventario` (digitación simplificada) | Módulo pagado, posiblemente uno por familia de documento |
| Importador masivo | Add-on sobre los genéricos |
| Integración WMS (una vez generalizada con adaptadores formales) | Módulo premium — mayor valor diferencial, candidato al tier más caro |

## 3. Cómo se cobra

El mercado SAP ya educó al cliente en **licenciamiento por usuario nombrado** (así
vende SAP mismo) — no conviene pelear contra esa expectativa con un modelo 100% por
consumo, más difícil de vender y de presupuestar para el cliente. Propuesta híbrida,
calzando con lo ya modelado (`plans.user_limit`, `plans.company_limit`,
`plans.monthly_transaction_limit`):

- **Eje 1 — usuarios activos**: rango por tier (ej. hasta 10 / hasta 50 / ilimitado).
- **Eje 2 — empresas SAP conectadas**: un holding (ej. Comercial Depor con DEPOR +
  DEPORQA) conecta varias compañías — cobrar por empresa adicional es natural y ya
  está en el modelo de datos (`companies` cuelga de `organizations`).
- **Eje 3 — módulos contratados**: cada módulo de §2 se prende/apaga por plan o como
  add-on (`organization_modules`).
- **`usage_metrics` se usa para alertar y limitar, no para facturar por transacción
  desde el día 1** — más simple de vender. Deja la puerta abierta a un modelo por
  consumo más adelante para el módulo de mayor volumen (WMS) si el mercado lo pide.

## 4. Tiers propuestos (punto de partida, a validar)

| | Starter | Growth | Enterprise |
|---|---|---|---|
| Empresas SAP | 1 | hasta 5 | ilimitadas |
| Usuarios | hasta 10 | hasta 50 | ilimitados |
| Módulos incluidos | Núcleo + Aprobación | + Genéricos + Importador | + Inventario + WMS |
| Soporte | estándar | estándar | prioritario/SLA |

## 5. SaaS vs. on-premise — términos distintos, mismo producto

- **SaaS**: suscripción mensual/anual. `subscriptions.status` gobierna el acceso
  (activa/vencida/cancelada), pago recurrente vía pasarela (Stripe/Flow/Transbank —
  ya previsto en `subscriptions.payment_provider`).
- **On-premise**: **licencia anual, no perpetua** — evita el problema clásico de
  licencia perpetua sin incentivo de renovar/actualizar. `on_premise_licenses.
  expires_at` obliga renovación, igual que SAP B1 cobra mantenimiento anual sobre su
  propia licencia.

## 6. Trial / onboarding

Conectar HANA/Service Layer de un cliente real requiere trabajo técnico (no es un
signup de un clic) — **no conviene un modelo freemium autoservicio**. El trial es
acompañado por ventas/onboarding, con `organizations.status = 'trial'` limitando
**tiempo** (ej. 30 días), no features — el cliente debe ver el producto completo
funcionando contra su propio SAP para decidir comprar.

## Pendiente de validar con el usuario

- Precio real por tier (no definido acá a propósito — depende de benchmark de
  mercado que no se hizo todavía).
- Si el canal de venta es directo, vía partners, o ambos desde el lanzamiento.
- Si el módulo WMS se vende junto con el resto o como producto/marca separada (dado
  que hoy son dos repos distintos con público potencialmente distinto).
