---
name: proyecto-menoja
description: "Iniciativa de negocio del usuario para convertir PortalSAP_v2 + WMS_Suite en producto vendible (SaaS + on-premise) — estado de madurez, veredicto y plan de acción"
metadata: 
  node_type: memory
  type: project
  originSessionId: 141aef22-b219-46e5-8e9c-7f6dec63eadf
  modified: 2026-07-24T13:36:41.846Z
---

**Proyecto Menoja** es el nombre que el usuario le dio a la iniciativa de negocio de evaluar y convertir dos repos internos en un producto comercial vendible a otras empresas en Chile/LatAm que usen SAP Business One:

- **`PortalSAP_v2`** (`C:\PROYECTOS\PortalSAP_v2`) — portal web ASP.NET Core 8 delante de SAP B1/HANA, modular monolith con plugins reales cargados en runtime (`AssemblyLoadContext` aislado). Multi-empresa ya construido (`INSTANCIA`/`EMPRESA`, secretos cifrados AES-256-GCM), motores de documento genéricos (`GenericoVenta`/`GenericoCompra`/`GenericoInventario`), motor de aprobación N-niveles configurable, traductor HANA↔SQL Server. Hoy corre en producción para un solo cliente real (Comercial Depor / Grupo Depor / JK Logística).
- **`WMS_Suite`** (`C:\PROYECTOS\WMS_Suite`) — integrador Oracle WMS (Logfire) ↔ SAP B1, 3 apps independientes (`WmsApiRest.webservices`, `WmsSapIntegration.Service`, `WmsPortal.Web`) que comparten solo el esquema de staging, no código.
- El plugin `GestionDistribucionGastos` (`C:\PROYECTOS\GestionDistribucionGastos`) es un módulo vertical concreto ya integrado como plugin de `PortalSAP_v2` — ejemplo de negocio a medida (distribución de gastos SAP B1, chileno) que NO es funcionalidad de plataforma reutilizable.

**Análisis completo publicado como Artifact:** https://claude.ai/code/artifact/37c3592a-2950-4a49-b44d-f6a6cf5f5096 (24 jul 2026) — fuente completa con tablas de madurez por dimensión; este memo resume solo las conclusiones que deben sobrevivir a futuras sesiones.

## Veredicto central (24 jul 2026)

**Evolucionar `PortalSAP_v2`, no reconstruir.** El aislamiento de plugins, el modelo multi-empresa y los motores genéricos están verificados contra el código real (grafo de dependencias, no solo documentación) — son decisiones de arquitectura correctas ya pagadas. Rebuild desde cero sería el error de descartar eso.

**`WMS_Suite` sí necesita una pieza nueva de fondo**: los adaptadores formales (`IWmsAdapter`/`IErpAdapter`) que harían el sistema genuinamente "cualquier WMS + cualquier ERP" están explícitamente en backlog, descartados "hasta tener un segundo caso real" — hoy es una integración de un cliente, no un producto, aunque el contrato de entrada (`INT_WMS_STAGE`) ya está diseñado para ser agnóstico.

**Por qué:** el usuario preguntó explícitamente si valía la pena reconstruir todo desde cero con las bases del objetivo SaaS/on-premise definidas de antemano, o evolucionar lo existente. La arquitectura ya construida (plugin isolation real, multi-tenant con secretos cifrados, motor de documento genérico, motor de aprobación configurable) es del nivel que muchos equipos financiados no logran — descartarla sería desperdiciar la ventaja competitiva real.

**Cómo aplicar:** en cualquier conversación futura sobre "¿deberíamos reescribir X en otro stack/arquitectura?" dentro de estos repos, el punto de partida es NO — la brecha real no es técnica, es la capa comercial faltante (ver abajo). Mantener .NET 8 — es la pieza que ya resolvió el problema difícil (cliente nativo HANA x64, aislamiento de plugins), no una limitación.

## Los 4 huecos reales para ser vendible

1. **Capa comercial: 0% construida.** Sin plan/tier, límite de usuarios por empresa, medición de transacciones, alta de tenant autoservicio, ni licencia on-premise con expiración. Es, en volumen de trabajo, más grande que cualquier ajuste de arquitectura pendiente.
2. **Núcleo vs. vertical sin frontera declarada.** `Core`/`Abstractions`/`Host`/los 3 motores genéricos/motor de aprobación = producto vendible. `GestionDistribucionGastos`/`SellOut` = desarrollo a medida de Comercial Depor. Hoy conviven en el mismo repo sin que esa decisión esté escrita.
3. **Tests/CI casi inexistentes.** 2 archivos de test reales en todo `PortalSAP.Core.Tests` para ~400 nodos de código en Core. Sin CI configurado. Despliegue de plugins es copiado manual de `.dll` a `artifacts/plugins/` — no sobrevive a 10 clientes.
4. **Higiene de seguridad operativa con hallazgos activos** (ver abajo) — deben cerrarse antes de cualquier conversación comercial, no son teóricos.

## Hallazgos de seguridad activos (bloquean venta hoy)

- **Credenciales reales expuestas en git** de `WmsSapIntegration.Service`: OAuth2 `SigningKey` y `client_secret` real de Logfire, ya pusheados a `origin/master` (commit `6cde05a`). Sanitizado hacia adelante en la rama `desarrollo`, pero **rotación de credenciales sigue pendiente** a la fecha de este memo.
- **`WmsApiRest.webservices` sirve HTTP plano en producción** desde 2026-07-03 (falta certificado TLS para `181.42.3.47`) — `client_secret` y token Bearer viajan sin cifrar mientras dure. Bloqueado externamente (esperando certificado), no es un fix de código.

**Cómo aplicar:** si en una sesión futura se habla de mostrarle este código a un cliente, inversionista, o hacer un security review externo, recordar que estos dos puntos deben estar resueltos primero — no son "nice to have", son bloqueadores duros.

## Modelo de datos

El esquema núcleo `PORTALWEB` (HANA, `db/hana/002_crear_tablas.sql`) ya está bien normalizado: claves subrogadas, FKs, `UNIQUE`, y disciplina real de `EMPRESA_CODIGO` obligatorio en toda tabla dependiente de compañía. Es una buena base para formalizar como modelo multi-tenant.

Los módulos verticales (`GestionDistribucionGastos` → base `CLDEPORFIN`, `SellOut` → base `CLSELLOUT`) NO tienen esa misma disciplina: sin columna de tenant, reglas de negocio hardcodeadas a un cliente, y al menos un caso confirmado de tablas sin `PRIMARY KEY` declarada. Antes de replicar a un segundo cliente, todo módulo vertical nuevo debe llevar `EMPRESA_CODIGO`/tenant desde el primer `CREATE TABLE`.

## Arquitectura de conectividad SAP — riesgos específicos para escalar a SaaS multiempresa (revisión 24 jul 2026, código `PortalSAP.Core/Sap/`)

El modelo `INSTANCIA`/`EMPRESA` y `HanaService` (Scoped, resuelve la empresa activa por request, sin el bug de singleton que tuvo el proyecto anterior) están bien resueltos para "1 host, pocas empresas". Al mirar `SapConnectionProvider`/`SapSessionCache`/`HanaService` en detalle aparecen 4 límites concretos que no son bugs hoy, pero son el primer techo real al escalar a una SaaS con muchos clientes:

1. **Sesión de Service Layer cacheada en memoria del proceso** (`ConcurrentDictionary` en `SapSessionCache`, clave `empresa:database`) — no distribuida. Escalar el Host a 2+ instancias (normal para alta disponibilidad en SaaS) multiplica las sesiones SL concurrentes contra el mismo SAP B1 del cliente, y Service Layer tiene límite real de sesiones concurrentes.
2. **Sin aislamiento de "vecino ruidoso" entre empresas** — conexión nueva por request, sin pool/circuit breaker por tenant. Un HANA lento/caído de un cliente puede degradar el mismo proceso Host que atiende a otros clientes.
3. **El modelo asume conectividad de red directa** del Host hacia el HANA/Service Layer de cada cliente. Onboardear un cliente nuevo no es solo un INSERT en `EMPRESA` — es trabajo de networking (VPN/túnel/whitelist) cada vez, salvo que el SAP del cliente ya esté en la nube. Es un costo operativo real que ninguna tabla resuelve.
4. **`TrustServerCertificate = true` fijo** para SQL Server (deshabilita validación de certificado para todos los clientes) y el connection string de HANA armado por interpolación de texto simple (no un builder seguro, a diferencia de SQL Server) — funciona hoy con certificados autofirmados, pero es el tipo de hallazgo que una revisión de seguridad de un comprador enterprise marca.

**Cómo aplicar:** estos 4 puntos son la base técnica real para responder "¿puede escalar horizontalmente el Host?" o "¿cuánto cuesta onboardear un cliente nuevo?" en conversaciones futuras — no son motivo para reconstruir, son inversión pendiente específica antes de escalar más allá de 1 host / pocos clientes con red ya resuelta.

## Plan de acción (orden de riesgo, ver artifact para detalle completo)

0. Antes de vender nada: rotar credenciales expuestas + forzar TLS en cuanto haya certificado.
1. Declarar por escrito la frontera núcleo/vertical.
2. Tests + CI mínimo viable, reemplazar copiado manual de plugins.
3. Extender `EMPRESA`/`INSTANCIA` con plan, límite de usuarios, vencimiento de licencia — capa comercial mínima.
4. Construir adaptadores formales en `WMS_Suite`, probados contra un segundo WMS real (no solo Logfire).
5. Validar todo con un segundo cliente real distinto de Comercial Depor — es la única prueba real de que "multiempresa" funciona más allá del diseño.
