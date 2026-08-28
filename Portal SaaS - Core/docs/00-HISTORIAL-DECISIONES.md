# Historial de decisiones — previo a este repo

Este documento existe para que este proyecto sea **autocontenido**: cualquiera que abra
esta carpeta (con o sin acceso a la memoria de Claude Code del autor original) tiene acá
el razonamiento completo detrás de cada decisión de `CLAUDE.md`/`ARCHITECTURE.md`.
Corresponde al análisis hecho entre el 24 de julio de 2026 y la creación de este repo
(nombre de trabajo interno del análisis: "Proyecto Menoja"). No se edita salvo para
corregir un error — las decisiones nuevas van en `ARCHITECTURE.md`/`CLAUDE.md`, no acá.

**Informe visual completo del análisis original:**
https://claude.ai/code/artifact/37c3592a-2950-4a49-b44d-f6a6cf5f5096

## Contexto

Iniciativa de negocio para evaluar y convertir dos repos internos en un producto
comercial vendible a otras empresas en Chile/LatAm que usen SAP Business One:

- **`PortalSAP_v2`** — portal web ASP.NET Core 8 delante de SAP B1/HANA, modular
  monolith con plugins reales cargados en runtime (`AssemblyLoadContext` aislado).
  Multi-empresa ya construido (`INSTANCIA`/`EMPRESA`, secretos cifrados AES-256-GCM),
  motores de documento genéricos (`GenericoVenta`/`GenericoCompra`/`GenericoInventario`),
  motor de aprobación N-niveles configurable, traductor HANA↔SQL Server. Corre en
  producción para un solo cliente real (Comercial Depor / Grupo Depor / JK Logística).
- **`WMS_Suite`** — integrador Oracle WMS (Logfire) ↔ SAP B1, 3 apps independientes
  (`WmsApiRest.webservices`, `WmsSapIntegration.Service`, `WmsPortal.Web`) que
  comparten solo el esquema de staging, no código.
- El plugin `GestionDistribucionGastos` es un módulo vertical concreto ya integrado
  como plugin de `PortalSAP_v2` — desarrollo a medida para Comercial Depor (distribución
  de gastos SAP B1, reglas contables chilenas), no funcionalidad de plataforma.

Ambos repos originales quedaron copiados de solo lectura en `referencia-original/` de
este proyecto (ver `ARCHITECTURE.md` §-1).

## Veredicto central

**Evolucionar `PortalSAP_v2`, no reconstruir.** El aislamiento de plugins, el modelo
multi-empresa y los motores genéricos están verificados contra el código real (grafo de
dependencias, no solo documentación) — son decisiones de arquitectura correctas ya
pagadas. Reconstruir desde cero sería el error de descartar eso.

**`WMS_Suite` sí necesita una pieza nueva de fondo**: los adaptadores formales
(`IWmsAdapter`/`IErpAdapter`) que harían el sistema genuinamente "cualquier WMS +
cualquier ERP" están explícitamente en backlog del propio repo, descartados "hasta
tener un segundo caso real" — hoy es una integración de un cliente, no un producto,
aunque el contrato de entrada (`INT_WMS_STAGE`) ya está diseñado para ser agnóstico.

**Por qué:** la pregunta original era si valía la pena reconstruir todo desde cero con
las bases del objetivo SaaS/on-premise definidas de antemano, o evolucionar lo
existente. La arquitectura ya construida (aislamiento real de plugins, multi-tenant con
secretos cifrados, motor de documento genérico, motor de aprobación configurable) es
del nivel que muchos equipos financiados no logran — descartarla desperdiciaría la
ventaja competitiva real. Mantener .NET 8 — es la pieza que ya resolvió el problema
difícil (cliente nativo HANA x64, aislamiento de plugins), no una limitación.

## Los 4 huecos reales para ser vendible

1. **Capa comercial: 0% construida.** Sin plan/tier, límite de usuarios por empresa,
   medición de transacciones, alta de tenant autoservicio, ni licencia on-premise con
   expiración. Es, en volumen de trabajo, más grande que cualquier ajuste de
   arquitectura pendiente. → Este proyecto la construye (`docs/03-MODELO-CORE-COMERCIAL.md`).
2. **Núcleo vs. vertical — confirmado por el dueño del proyecto.** `Core`/`Abstractions`/
   `Host`/los 3 motores genéricos/motor de aprobación = producto vendible.
   `GestionDistribucionGastos` y `SellOut` son **desarrollo a medida de Comercial
   Depor, diseñados para resolver algo puntual de ese cliente, que no aplica a otra
   empresa** — no son funcionalidad de plataforma. No se portan a este proyecto.
3. **Tests/CI casi inexistentes en `PortalSAP_v2`.** 2 archivos de test reales para
   ~400 nodos de código en `Core`. Sin CI configurado. Despliegue de plugins es
   copiado manual de `.dll` — no sobrevive a 10 clientes. → Este proyecto exige tests
   desde el primer módulo comercial (ver `CLAUDE.md`).
4. **Higiene de seguridad operativa con hallazgos activos** (ver abajo) — deben
   cerrarse antes de cualquier conversación comercial, no son teóricos.

## Hallazgos de seguridad activos en `WMS_Suite` (bloquean venta hoy)

- **Credenciales reales expuestas en git** de `WmsSapIntegration.Service`: OAuth2
  `SigningKey` y `client_secret` real de Logfire, ya pusheados a `origin/master`
  (commit `6cde05a`). Sanitizado hacia adelante en la rama `desarrollo`, pero
  **rotación de credenciales sigue pendiente** a la fecha de este análisis.
- **`WmsApiRest.webservices` sirve HTTP plano en producción** desde 2026-07-03 (falta
  certificado TLS) — `client_secret` y token Bearer viajan sin cifrar mientras dure.
  Bloqueado externamente (esperando certificado), no es un fix de código.

Si en algún momento se habla de mostrarle este código a un cliente, inversionista, o
hacer un security review externo, estos dos puntos deben estar resueltos primero — no
son "nice to have", son bloqueadores duros.

## Modelo de datos de origen

El esquema núcleo `PORTALWEB` (HANA, `PortalSAP_v2/db/hana/002_crear_tablas.sql`) está
bien normalizado: claves subrogadas, FKs, `UNIQUE`, y disciplina real de
`EMPRESA_CODIGO` obligatorio en toda tabla dependiente de compañía. Es la base directa
del modelo comercial de este proyecto (ver `docs/03-MODELO-CORE-COMERCIAL.md`).

Los módulos verticales (`GestionDistribucionGastos` → base `CLDEPORFIN`, `SellOut` →
base `CLSELLOUT`) NO tienen esa misma disciplina: sin columna de tenant, reglas de
negocio hardcodeadas a un cliente, y al menos un caso confirmado de tablas sin
`PRIMARY KEY` declarada. Refuerza por qué no se portan a este proyecto.

## Decisión de arquitectura — motor de base de datos

**Dos bases separadas, decisión solo sobre una de ellas:**

1. **Datos de SAP de cada cliente** (HANA o SQL Server) — no es una elección propia,
   la define el cliente según lo que tenga instalado. Sigue igual: `HanaService`/
   `TraductorSqlHanaASqlServer` se porta sin cambios.
2. **Base propia de la plataforma** (hoy `PORTALWEB` vive dentro del HANA de un
   cliente — correcto para "modo instalado, 1 cliente", equivocado para SaaS real):
   usuarios, empresas, instancias, menú, permisos, aprobaciones, auditoría, y
   licenciamiento/planes/medición de uso. **Decisión: PostgreSQL gestionado**, no
   HANA ni SQL Server.

**Por qué:** HANA es el motor equivocado para este workload (licenciamiento por
core/memoria pensado para OLTP+analytics in-memory, carísimo para tablas chicas de
usuarios/permisos/menú). PostgreSQL gestionado da costo de licencia cero, buen soporte
EF Core (proveedor Npgsql), JSONB nativo (útil para config de planes/plugins) y escala
horizontal barata vía réplicas de lectura. Se descartó Azure SQL Server como
alternativa (reusaría más código existente, pero pierde contra Postgres en costo puro
de licencia).

## Arquitectura de conectividad SAP — riesgos específicos al escalar a SaaS multiempresa

El modelo `INSTANCIA`/`EMPRESA` y `HanaService` (Scoped, resuelve la empresa activa por
request, sin el bug de singleton que tuvo el proyecto anterior de Comercial Depor)
están bien resueltos para "1 host, pocas empresas". Mirando `SapConnectionProvider`/
`SapSessionCache`/`HanaService` en detalle aparecen 4 límites concretos que no son
bugs hoy, pero son el primer techo real al escalar a una SaaS con muchos clientes:

1. **Sesión de Service Layer cacheada en memoria del proceso** (`ConcurrentDictionary`
   en `SapSessionCache`, clave `empresa:database`) — no distribuida. Escalar el Host a
   2+ instancias (normal para alta disponibilidad en SaaS) multiplica las sesiones SL
   concurrentes contra el mismo SAP B1 del cliente, y Service Layer tiene límite real
   de sesiones concurrentes.
2. **Sin aislamiento de "vecino ruidoso" entre empresas** — conexión nueva por
   request, sin pool/circuit breaker por tenant. Un HANA lento/caído de un cliente
   puede degradar el mismo proceso Host que atiende a otros clientes.
3. **El modelo asume conectividad de red directa** del Host hacia el HANA/Service
   Layer de cada cliente. Onboardear un cliente nuevo no es solo un INSERT en
   `EMPRESA` — es trabajo de networking (VPN/túnel/whitelist) cada vez, salvo que el
   SAP del cliente ya esté en la nube. Es un costo operativo real que ninguna tabla
   resuelve.
4. **`TrustServerCertificate = true` fijo** para SQL Server (deshabilita validación de
   certificado para todos los clientes) y el connection string de HANA armado por
   interpolación de texto simple (no un builder seguro, a diferencia de SQL Server) —
   funciona hoy con certificados autofirmados, pero es el tipo de hallazgo que una
   revisión de seguridad de un comprador enterprise marca.

Estos 4 puntos son la base técnica real para responder "¿puede escalar horizontalmente
el Host?" o "¿cuánto cuesta onboardear un cliente nuevo?" — no son motivo para
reconstruir, son inversión pendiente específica antes de escalar más allá de 1 host /
pocos clientes con red ya resuelta. Deben resolverse como parte de portar
`SapConnectionProvider`/`HanaService` a este proyecto, no después.

## Plan de acción original (orden de riesgo)

0. Antes de vender nada: rotar credenciales expuestas de `WMS_Suite` + forzar TLS en
   cuanto haya certificado.
1. Declarar por escrito la frontera núcleo/vertical. → Hecho al crear este repo.
2. Tests + CI mínimo viable, reemplazar copiado manual de plugins.
3. Extender `EMPRESA`/`INSTANCIA` con plan, límite de usuarios, vencimiento de
   licencia — capa comercial mínima. → Modelada en `docs/03-MODELO-CORE-COMERCIAL.md`.
4. Construir adaptadores formales en `WMS_Suite`, probados contra un segundo WMS real
   (no solo Logfire).
5. Validar todo con un segundo cliente real distinto de Comercial Depor — es la única
   prueba real de que "multiempresa" funciona más allá del diseño.
