# Autenticación Máquina-a-Máquina + Resolución de Compañía — Diseño

**Fecha:** 2026-08-15
**Estado:** Aprobado para plan de implementación
**Precede a:** proyecto de migración de `Modulo.Wms` al motor de integración genérico (ver `docs/12-MOTOR-INTEGRACION-ERP-PENDIENTES.md`, punto 3). Es la "Ronda 0" — capacidad de plataforma, no específica de Wms.

## Contexto

Al diseñar el piloto de migración de Wms (recibir XML de Oracle WMS y llevarlo, vía staging, al motor de integración genérico hacia SAP), se encontró que el Portal no tiene ningún mecanismo de autenticación para llamadas de sistema-a-sistema: solo existen dos esquemas (`CookieAuthenticationDefaults.AuthenticationScheme` para login de usuario, `"PlatformAdmin"` para el panel de super-admin), ambos dependientes de una sesión de navegador. Tampoco hay forma de resolver `CompanyId` para una llamada sin esa sesión — `ICurrentCompanyAccessor` lee claims de `HttpContext.User`, pero nada los pone ahí fuera del login normal.

Esta pieza es un prerrequisito genérico de plataforma, no solo del piloto de Wms — cualquier integración futura que reciba llamadas entrantes de un sistema externo (Oracle WMS, un Sorter, lo que sea) la va a necesitar.

## Decisión

**Reutilizar `ICurrentCompanyAccessor` sin modificarlo.** Es claims-based y agnóstico del esquema de autenticación que pobló esos claims — si el nuevo esquema arma un `ClaimsPrincipal` con los mismos tipos de claim que el login normal (`CompanyId`, `CompanyCode`, `CompanyDatabase`, `CompanyServiceLayerUrl`, `CompanyCountry`), todo el código existente que depende de la compañía activa (incluyendo `ISapConnectionProvider`, document services, etc.) funciona sin cambios para llamadas máquina-a-máquina.

### Modelo de datos

Nueva entidad `ApiClientCredential` en `PortalSaas.Data`:

```
ApiClientCredential
  Id            Guid PK
  CompanyId     FK -> companies (not null)
  Nombre        string           -- etiqueta legible, ej. "Oracle WMS - Ingesta XML"
  ApiKeyHash    string           -- hash de un solo sentido (SHA-256), NUNCA la key en texto plano ni cifrada
  Activo        bool
  CreatedAt     DateTimeOffset
  LastUsedAt    DateTimeOffset?  -- nullable, se actualiza en cada autenticación exitosa
```

Se guarda solo el hash porque nunca hace falta recuperar la key original — es análogo a una contraseña, no a un secreto que el sistema necesite leer de vuelta (a diferencia de `ConectorConfigCifrado`, que sí se descifra para usarlo). La key en sí se genera como un valor de alta entropía (32 bytes aleatorios, codificado base64/base64url) y se muestra al usuario administrador **una sola vez**, en el momento de creación — patrón estándar de "secret reveal once", igual que un token de API de cualquier plataforma conocida.

### Autenticación

`AuthenticationHandler` nuevo, esquema `"ExternalApiKey"`, registrado junto a los esquemas existentes en `Program.cs`:

1. Lee el header `X-Api-Key` de la request.
2. Hashea el valor recibido (mismo algoritmo que al crear la credencial) y busca un `ApiClientCredential` activo con ese hash.
3. Si no hay match o la credencial está inactiva: falla la autenticación (401), sin filtrar información sobre si la key existe o no.
4. Si hay match: carga la `Company` asociada (`CompanyId` de la credencial), arma un `ClaimsPrincipal` con los claims `CompanyId`, `CompanyCode`, `CompanyDatabase`, `CompanyServiceLayerUrl`, `CompanyCountry` — los mismos tipos que usa el login normal — y actualiza `LastUsedAt`.
5. Endpoints que acepten llamadas máquina-a-máquina se marcan `[Authorize(AuthenticationSchemes = "ExternalApiKey")]`, igual que las páginas admin usan `"PlatformAdmin"`.

### UI admin

Página nueva bajo `PortalSaas.Host/Pages/Admin/Organizations/Companies/ApiKeys/`, siguiendo el patrón exacto de `Admin/Organizations/Companies/ExternalConnections` (misma carpeta padre, mismo estilo de listado + crear/revocar). Permite:
- Listar credenciales activas/revocadas de una compañía (nombre, fecha de creación, último uso, estado).
- Crear una nueva (genera la key, la muestra una sola vez en un banner de confirmación, luego nunca más se puede recuperar).
- Revocar una existente (`Activo = false`, no se borra — se conserva el registro para auditoría).

## Fuera de alcance (explícito)

- No se construye ningún endpoint que consuma este esquema todavía (eso es la siguiente ronda: ingestión de XML de Oracle WMS). Esta ronda entrega la capacidad de autenticar y resolver compañía; el primer consumidor real llega después.
- No se implementa rotación automática de keys ni expiración por tiempo — revocación manual únicamente, consistente con el nivel de madurez del resto de la plataforma (ej. `ISecretoCifradoService` tampoco rota automáticamente).
- No se generaliza a un sistema de "scopes"/permisos granulares por API key — cada credencial da acceso completo a los endpoints marcados con el esquema, para la compañía a la que pertenece. Si en el futuro se necesita un control más fino, es una extensión posterior sobre esta base.

## Criterio de éxito

- Una request HTTP con header `X-Api-Key` válido, contra un endpoint de prueba marcado `[Authorize(AuthenticationSchemes = "ExternalApiKey")]`, resuelve `ICurrentCompanyAccessor.CompanyId` correctamente sin ningún cambio a `CurrentCompanyAccessor` ni a ningún consumidor existente de esa interfaz.
- Una request sin key, con key inválida, o con key revocada, recibe 401.
- La UI admin permite generar y revocar keys por compañía sin acceso directo a la base de datos.
