# Autenticación, recuperación de contraseña y preferencias personales

Pedido explícito del usuario (24 jul 2026): la cuenta debe quedar **siempre asociada
a un correo real**, con lo que se requiera para autenticar de forma segura y para
recuperar la contraseña, más un segmento de **preferencias personales** configurable
por el propio usuario. Nada de esto existía implementado en `PortalSAP_v2` (tenía el
modelo de datos como "gancho" — `TOKEN_RECUPERACION` — pero nunca el flujo real, ver
`docs/00-HISTORIAL-DECISIONES.md`).

## 1. Correo obligatorio (cambio de modelo)

`users.email` pasó de nullable (como en `PortalSAP_v2`) a **obligatorio**, con índice
único `(organization_id, email)` —además del ya existente `(organization_id, username)`.
Razón: el correo es el único canal real de recuperación de contraseña y, a futuro, de
notificaciones — una cuenta sin correo no se puede recuperar ni notificar.

**Migración con aviso de EF Core** ("puede resultar en pérdida de datos") al pasar la
columna de nullable a `NOT NULL` — esperado y sin riesgo real hoy (no hay datos en
ninguna base todavía), pero si esto se aplicara sobre una base con usuarios ya
cargados sin correo, haría falta un backfill manual antes de aplicar la migración.

## 2. Autenticación (`IAuthenticationService`/`AuthenticationService`)

Valida usuario+contraseña **dentro de una organización** (login resuelve primero a
qué organización pertenece la cuenta, mismo criterio que `PortalSAP_v2` con la
empresa). Acepta **username o correo indistintamente** como identificador.

Política de bloqueo (nueva, no existía en `PortalSAP_v2`):
- Cuenta inactiva (`is_active = false`) o ya bloqueada (`is_locked = true`) se
  rechaza **sin evaluar la contraseña**.
- Cada intento fallido incrementa `failed_login_attempts`; al llegar a
  `AuthenticationService.MaxFailedAttempts` (5), la cuenta se bloquea sola.
- Un login exitoso resetea el contador y actualiza `last_login_at`.
- Mensaje de error **genérico** ("Usuario o contraseña incorrectos") para usuario
  inexistente y para contraseña incorrecta — no se distingue, para no permitir
  enumerar qué correos/usuarios existen (mismo criterio que `PortalSAP_v2` había
  planeado y nunca implementó).

Hash de contraseña: `PasswordHasher` (PBKDF2-SHA256, salt de 128 bits por usuario,
100k iteraciones) — portado tal cual de `PortalSAP_v2`.

## 3. Recuperación de contraseña (`IPasswordResetService`/`PasswordResetService`)

Flujo de dos pasos:
1. `RequestResetAsync(organizationId, email)` — si existe un usuario activo con ese
   correo, genera un token aleatorio de 256 bits, guarda **solo su hash SHA-256**
   (`password_reset_tokens.token_hash`) con expiración de 1 hora, y devuelve el token
   en texto plano (para que el Host futuro lo mande por correo). Si no existe,
   devuelve `null`.
2. `ResetPasswordAsync(rawToken, newPassword)` — valida que el token exista, no esté
   usado y no haya expirado; si es válido, aplica la nueva contraseña (nuevo hash +
   salt), desbloquea la cuenta si estaba bloqueada, y marca el token como usado
   (`is_used = true`, no se borra — trazabilidad).

**Anti-enumeración deliberado**: el contrato deja explícito en su doc-comment que el
llamador (futuro Host/API) debe mostrar el mismo mensaje genérico
("si el correo existe, se envió un link") sin importar si `RequestResetAsync` devolvió
un token real o `null` — la diferencia es información interna, nunca de cara al
usuario final.

**Por qué SHA-256 simple y no PBKDF2 acá**: a diferencia de una contraseña elegida
por una persona (baja entropía real, necesita una KDF lenta para encarecer fuerza
bruta), el token de recuperación ya es aleatorio de 256 bits — un hash rápido alcanza
y no tiene sentido pagar el costo de una KDF lenta para verificarlo.

## 4. Preferencias personales (`IUserPreferenceService`/`UserPreferenceService`)

Tabla nueva `user_preferences`, relación 1:1 con `users` (comparte PK, no tiene id
propio) — separada a propósito de la tabla de identidad/seguridad: un cambio de tema
o idioma no debería tocar nunca la fila crítica de credenciales.

Campos (punto de partida, no cerrado — agregar más ahí si se necesita, mismo patrón):
- `locale` (`"es-CL"` default) — idioma/región.
- `timezone` (`"America/Santiago"` default) — zona horaria IANA.
- `theme` (`"light"` | `"dark"` | `"system"`, default `"system"`).
- `email_notifications_enabled` (default `true`).

`GetOrCreateDefaultAsync` crea la fila con los defaults la primera vez que se
consulta (no hace falta un paso de alta explícito al crear el usuario).

## 5. Qué NO se construyó todavía (deliberado, no es un olvido)

- **Flujo de 2FA real**: los campos (`has_two_factor_enabled`, `two_factor_method`,
  `two_factor_secret`) siguen ahí (heredados del modelo de `PortalSAP_v2`), pero
  implementar TOTP/envío de código es un pedido aparte — mismo criterio que
  `PortalSAP_v2` ("no implementar el flujo todavía, solo cuando se pida
  explícitamente").
- **Verificación de correo al registrarse**: `is_email_confirmed` existe como
  columna, pero no hay todavía un `EmailVerificationToken`/servicio equivalente al de
  recuperación de contraseña — se puede construir con el mismo patrón el día que se
  pida.
- **Sesión/cookies**: la validación de credenciales existe, pero cómo se mantiene la
  sesión (cookie de autenticación, JWT, etc.) es responsabilidad del Host, que
  todavía no existe.
- **UI de administración para dar de alta `email_settings`**: hoy es una tabla sin
  pantalla — se carga a mano (INSERT) hasta que exista `Modulo.Administracion` (ver
  `ARCHITECTURE.md` §6 paso 6), mismo criterio que `PortalSAP_v2` con
  `REGLA_APROBACION` (sin UI, alta manual documentada como gap conocido).

## 7. Envío de correo (`IEmailSenderService`) — Google Workspace o Microsoft 365

Pedido explícito del usuario (24 jul 2026): los proveedores de correo hoy en día son
típicamente Gmail Workspace o Microsoft 365 — cada organización/instalación configura
**el suyo propio** (`email_settings`, 1:1 con `organizations`, mismo criterio que
`user_preferences`: separado de la fila de credenciales). `IEmailSenderService`
oculta al llamador cuál de los dos está activo — mismo patrón que `HanaService` de
`PortalSAP_v2` oculta si el motor SAP activo es HANA o SQL Server.

**Configuración por proveedor** (`email_settings.encrypted_provider_config`, JSON
cifrado con `ISecretoCifradoService`, forma según `Provider`):
- `microsoft365`: `{"tenantId", "clientId", "clientSecret"}` — app registrada en
  Microsoft Entra ID con permiso de **aplicación** `Mail.Send` (no permiso delegado,
  no hay usuario interactivo detrás).
- `google_workspace`: `{"clientEmail", "privateKeyPem"}` — cuenta de servicio de
  Google Cloud con **delegación de dominio completo** autorizada en el Workspace del
  cliente, para poder impersonar la casilla de envío (`sender_email`).

**Cómo se envía, en ambos casos:**
1. Se autentica la aplicación (no una persona) contra el proveedor: Microsoft con
   flujo `client_credentials` (`login.microsoftonline.com`), Google firmando un JWT
   assertion con la llave privada de la cuenta de servicio e intercambiándolo en
   `oauth2.googleapis.com` (`urn:ietf:params:oauth:grant-type:jwt-bearer`).
2. Con el token obtenido, se llama `POST /users/{sender}/sendMail` de Microsoft
   Graph, o `POST /users/{sender}/messages/send` de la Gmail API (mensaje MIME
   codificado en base64url).

**Separación deliberada entre lógica pura y llamada de red**: armar el JSON de Graph
(`MicrosoftGraphPayloadBuilder`), armar el MIME de Gmail (`GmailMessageBuilder`) y
firmar el JWT de Google (`GoogleServiceAccountJwtBuilder`) son métodos **puros**, sin
HTTP — testeados con datos de prueba (incluido un par de llaves RSA generado en el
test para Google, nunca una credencial real). `EmailSenderServiceTests` cubre el
despacho y las fallas cerradas (sin `email_settings`, inactiva, proveedor no
reconocido).

**Google Workspace: VERIFICADO contra un tenant real (24 jul 2026).** Envío real
exitoso desde `no-reply@comercialdepor.cl` hacia una casilla real del mismo Workspace,
usando `tools/PortalSaas.Tools.EmailSmokeTest` — el flujo completo (JWT firmado →
token de acceso → `POST /users/{sender}/messages/send`) funciona de punta a punta tal
como está implementado. Dos hallazgos reales en el camino (ninguno queda pendiente):
1. Bug de código (ver más abajo, corregido con `[JsonPropertyName]`).
2. No era código: la Gmail API no estaba habilitada en el proyecto de Google Cloud
   (`accessNotConfigured`/`SERVICE_DISABLED`) — se habilitó desde Cloud Console y
   quedó resuelto. Queda como recordatorio para el runbook de onboarding de un cliente
   nuevo: habilitar la Gmail API es un paso obligatorio, no implícito.

**Microsoft 365: sigue sin verificar contra un tenant real** — implementado
siguiendo la misma documentación oficial y el mismo patrón que Google Workspace (ya
probado), pero no hay un tenant de Microsoft 365 de prueba disponible todavía. Mismo
procedimiento (`tools/PortalSaas.Tools.EmailSmokeTest`) cuando exista uno.

**Cómo probarlo tú mismo, sin pasar credenciales por el chat**:
`tools/PortalSaas.Tools.EmailSmokeTest/` — herramienta de consola local, lee todo
desde variables de entorno que se setean en tu propia terminal (nunca versionadas, ni
compartidas con Claude), envía un correo real de prueba, e imprime ÉXITO/FALLÓ con el
detalle. Ver su `README.md` para las variables exactas por proveedor, incluida la
guía paso a paso para crear la cuenta de servicio de Google Workspace con delegación
de dominio.

**Bug real encontrado y corregido probando contra un Workspace real (24 jul 2026)**:
la primera corrida contra `no-reply@comercialdepor.cl` falló con
`RSA.ImportFromPem: "No supported key formats were found"`. Causa: `Microsoft365ProviderConfig`/
`GoogleWorkspaceProviderConfig` no tenían `[JsonPropertyName]`, y el JSON de
configuración se produce en camelCase (`clientEmail`, `privateKeyPem`, etc.) mientras
que `JsonSerializer.Deserialize` hace match de nombre **case-sensitive por defecto**
contra las propiedades PascalCase — las tres/dos propiedades de cada config
deserializaban a `null` **en silencio, sin excepción en ese punto**, y recién
explotaba varios pasos después al intentar parsear una PEM vacía. Corregido con
`[JsonPropertyName("...")]` explícito en ambos records, y agregado
`ProviderConfigDeserializationTests` (2 tests) que deserializan exactamente el mismo
JSON camelCase que produce el tool de prueba, sin opciones extra — para que una
regresión de esto se detecte en `dotnet test`, no contra un tenant real de nuevo.
Este bug afectaba a **los dos proveedores por igual** (Microsoft 365 también
deserializaba `TenantId`/`ClientId`/`ClientSecret` como `null`), aunque solo se
manifestó probando Google Workspace primero.

## 8. Tests

`tests/PortalSaas.Core.Tests` — **40 tests en total, todos en verde**:
`PasswordHasherTests` (3), `AuthenticationServiceTests` (6: login por username, login
por correo, credenciales incorrectas sin distinguir motivo, incremento/reset de
intentos, bloqueo al llegar al umbral, cuenta inactiva), `PasswordResetServiceTests`
(5: token generado/no generado, reset exitoso, token reusado falla, token expirado
falla, token inventado falla), `UserPreferenceServiceTests` (3: defaults al crear, no
duplica fila, actualización real), `MicrosoftGraphPayloadBuilderTests` (1: forma del
JSON), `GmailMessageBuilderTests` (2: encabezados/cuerpo MIME correctos, base64url sin
caracteres estándar), `GoogleServiceAccountJwtBuilderTests` (2: claims correctos,
firma verificable con la llave pública de un par generado en el test),
`EmailSenderServiceTests` (3: sin `email_settings` falla, inactiva falla, proveedor no
reconocido falla) — más los 8 de `ContractLimitServiceTests` de la sección anterior.
