# Instrucciones de trabajo para Copilot en Proyecto Saas Portal

Este repositorio es la evolución de PortalSAP_v2 hacia un producto vendible (SaaS + on-premise) para empresas que usan SAP Business One. Por lo tanto, cualquier cambio debe respetar tanto la arquitectura original heredada de PortalSAP_v2 como las decisiones de diseño nuevas del proyecto actual.

## 1. Propósito del repositorio

- Mantener una arquitectura modular monolítica con plugins reales cargados en runtime.
- Reutilizar el modelo conceptual de PortalSAP_v2, no reconstruirlo desde cero.
- Separar claramente:
  - la base propia de la plataforma (PostgreSQL/SQL Server),
  - la conexión al SAP del cliente (HANA o SQL Server),
  - y la capa comercial (planes, licencias, límites, módulos y permisos).

## 2. Fuente de verdad y precedentes

- La referencia principal es la arquitectura y el código heredados desde la carpeta de referencia original:
  - [referencia-original/PortalSAP_v2](../referencia-original/PortalSAP_v2)
- Nunca se debe tratar a la referencia como un repositorio editable ni como un código nuevo a apuntar.
- Cualquier cambio que implique portar funcionalidad desde PortalSAP_v2 debe:
  1. revisar primero la implementación original en la referencia,
  2. adaptar el patrón al modelo del proyecto actual,
  3. preservar las reglas de negocio y seguridad, y
  4. evitar copiar lógica sin entenderla.

## 3. Reglas de arquitectura obligatorias

### 3.1 Dependencias

- Mantener la dirección de dependencias estable:
  - Abstractions → todos los proyectos.
  - Core → solo Host.
  - Ningún plugin debe referenciar Host, Core o otro plugin.
- No introducir acoplamientos entre plugins.
- Si una nueva pieza necesita compartirse, debe exponerse por Abstractions o por un servicio central del Core.

### 3.2 Modularidad

- Preferir el patrón de plugins reales con AssemblyLoadContext aislado.
- Los plugins deben implementar IModuloPortal y registrar su menú, páginas y servicios de forma explícita.
- No introducir lógica de negocio específica de un plugin en Host.

### 3.3 Separación de responsabilidades

- La capa de datos debe permanecer agnóstica del proveedor.
- La lógica de negocio debe vivir en Core.
- El Host solo debe orquestar UI, autenticación y composición de servicios.
- Los DTOs y contratos compartidos deben ir en Abstractions.

## 4. Reglas de diseño y código

### 4.1 Estilo de código

- Mantener el código claro, explícito y consistente.
- Usar nombres en español para UI, mensajes y comentarios, salvo cuando el dominio técnico o el modelo de SAP exija inglés (por ejemplo: SalesOrder, Customer, Item, DocumentType, etc.).
- Para entidades y tablas de base de datos, seguir la convención formal:
  - inglés,
  - plural,
  - snake_case en minúsculas,
  - id surrogate siempre,
  - FK como <entidad>_id,
  - timestamps como _at,
  - booleanos como is_ / has_,
  - estados en columna status.

### 4.2 Seguridad

- Nunca guardar secretos en texto plano en appsettings.json.
- Usar user-secrets en desarrollo y vault/secret manager en producción.
- Mantener TLS obligatorio.
- No exponer credenciales SAP ni secretos técnicos en la UI.
- Cualquier operación que involucre secretos debe usar cifrado fuerte (por ejemplo AES-256-GCM) y seguir el patrón ya usado en el proyecto.

### 4.3 Tests

- Todo cambio de lógica comercial debe venir con tests.
- Los módulos de licenciamiento, límites, permisos, autenticación, accesos y reglas de negocio deben cubrirse con pruebas unitarias o de integración.
- Si un cambio afecta a SAP, se debe validar con pruebas de integración o con un escenario real cuando sea posible.
- No declarar un módulo como completo si no tiene cobertura mínima apropiada.

### 4.4 Compatibilidad multi-motor

- El proyecto debe funcionar tanto con PostgreSQL como con SQL Server para la base propia de la plataforma.
- No introducir código dependiente de un proveedor específico en PortalSaas.Data.
- No usar HasDefaultValueSql para valores por defecto de negocio si la lógica ya se resuelve en C#.
- Evitar código específico de PostgreSQL/SQL Server salvo en los proyectos de migraciones o adaptadores explícitos.

## 5. Reglas del dominio comercial

### 5.1 Nivel organizacional

- Toda tabla de negocio nueva debe tener un nivel de organización o un vínculo que resuelva a organization_id.
- El nivel de negocio principal es organization, no company.
- Los límites de contrato deben cumplirse en código, no solo documentarse.
- El comportamiento por defecto debe ser estricto: si un límite no se puede verificar, la operación debe bloquearse.

### 5.2 Modo SaaS vs on-premise

- SaaS: validar subscriptions y plan vigente.
- On-premise: validar on_premise_licenses activas y vigentes.
- Nunca mezclar la lógica de SaaS con la lógica de on-premise.

### 5.3 Módulos y permisos

- Todo módulo nuevo debe declararse explícitamente como núcleo de plataforma o extensión específica de una organización.
- Los permisos de navegación deben respetar los grupos de menú, perfiles y acciones.
- Un administrador de plataforma y un administrador de tenant no deben compartirse como un mismo actor de negocio.

## 6. Reglas para SAP y mapeos de negocio

### 6.1 Principio general

- La conexión al SAP de cada organización es un motor aparte de la base propia de la plataforma.
- El acceso a SAP debe ser resiliente, seguro y aislado por compañía/instancia.
- No mezclar el modelo propio de PortalSaas con el modelo de SAP.

### 6.2 Mapeo de conexión por organización

- Cada company debe resolver su instance y su conexión SAP desde la organización correspondiente.
- No asumir que una instancia o conexión vale para todas las organizaciones.
- Nunca depender de una sesión global activa sin una compañía explícita.

### 6.3 Traductor HANA ↔ SQL Server

- Si se modifica la integración SAP, respetar el patrón ya existente de traducción de SQL HANA a SQL Server.
- No convertir esto en un traductor genérico de SQL. Debe cubrir solo los patrones ya validados y documentados.
- No ejecutar SQL no fijado o dinámico en la rama SQL Server sin una evaluación explícita de riesgo.

### 6.4 Motor de documentos genéricos

- Los motores genéricos de documentos (venta, compra, inventario) deben seguir el patrón de catálogo estático + PageModel base + vistas compartidas.
- No duplicar lógica de listado, detalle, validación y creación entre tipos de documento.
- Si un cambio aplica a un motor, debe considerarse también para los otros dos cuando la regla de paridad lo justifique.
- No introducir un nuevo tipo de documento sin agregarlo al catálogo correspondiente.

### 6.5 Reglas de negocio de documentos SAP

- Respetar la semántica real de SAP Business One y no asumir que todos los documentos se comportan igual.
- Para órdenes, facturas, devoluciones, ofertas y transferencias, mantener el mapeo de campos y tablas ya definido en la referencia original y en la arquitectura del repositorio.
- No introducir campos de usuario o UDF sin evaluar si SAP los necesita y cómo se deben nombrar.
- Cuando exista una diferencia entre PortalSAP_v2 y este proyecto, priorizar la implementación del repositorio actual y documentar la diferencia.

### 6.6 Catálogos y servicios compartidos

- Los catálogos de clientes, artículos, almacenes, vendedores y otros master data deben estar expuestos como servicios reutilizables en Core.
- Evitar consultas masivas sin filtro cuando el volumen real del cliente lo justifique.
- Nunca implementar un catálogo de SAP en Host o en plugins.

## 7. Reglas de UI y estilo visual

- Mantener el estilo visual basado en Bootstrap 5 y variables de Bootstrap.
- Respetar la estética del shell de tenant y el diseño de la interfaz de administración.
- No introducir un sistema de temas complejo si el proyecto no lo necesita.
- Mantener consistencia visual en:
  - sidebar,
  - topbar,
  - tablas de documentos,
  - botones primarios,
  - estados de carga/error/éxito.
- Priorizar claridad, jerarquía y legibilidad sobre efectos decorativos.

## 8. Reglas operativas para trabajar en este repo

- Antes de hacer cambios grandes, revisar ARCHITECTURE.md, CLAUDE.md y docs/.
- Antes de portar funcionalidad desde PortalSAP_v2, revisar primero la implementación en referencia-original/PortalSAP_v2.
- Si un cambio afecta a la base de datos, considerar migración y compatibilidad con PostgreSQL y SQL Server.
- Si un cambio afecta a la seguridad, revisar el impacto antes de implementarlo.
- Si un cambio afecta a permisos, contratos o módulos comerciales, validar con tests reales.
- Si un cambio afecta al flujo de plugins, verificar que el plugin siga cargando en runtime sin dependencias no deseadas.

## 9. Resultado esperado

Toda contribución en este repositorio debe ser:
- coherente con PortalSAP_v2,
- segura,
- modular,
- compatible con la arquitectura dual de plataforma/SAP,
- testeable,
- y alineada con el modelo comercial y los motores genéricos del proyecto.
