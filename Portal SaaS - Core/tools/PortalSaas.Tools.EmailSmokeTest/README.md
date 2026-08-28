# Email Smoke Test

Prueba de humo **local**, contra un tenant/Workspace real, del envío de correo
(`IEmailSenderService` / `docs/06-AUTENTICACION-Y-PREFERENCIAS.md` §7). No se usa en
CI, no tiene ningún secreto hardcodeado — todo sale de variables de entorno que
pones tú mismo, en tu propia terminal, y que nunca se comparten con Claude ni se
versionan.

## Antes de correrlo

- **Microsoft 365**: una app registrada en Microsoft Entra ID con permiso de
  **aplicación** (no delegado) `Mail.Send` sobre Microsoft Graph, con consentimiento
  de administrador otorgado. Necesitas: Tenant ID, Client ID, Client Secret.
- **Google Workspace**: una cuenta de servicio de Google Cloud con **delegación de
  dominio completo** autorizada en el admin console del Workspace (scope
  `https://www.googleapis.com/auth/gmail.send`), y el archivo JSON de la clave
  descargado.

## Cómo crear la cuenta de servicio de Google Workspace (paso a paso)

Necesitas ser **super administrador** del Google Workspace del dominio que va a
enviar los correos (una cuenta de Gmail personal gratuita no alcanza).

1. **Google Cloud Console** (https://console.cloud.google.com) — crear un proyecto
   nuevo (o usar uno existente), ej. "portalsaas-email".
2. **Habilitar la Gmail API**: en ese proyecto, "APIs & Services" → "Library" →
   buscar "Gmail API" → "Enable".
3. **Crear la cuenta de servicio**: "IAM & Admin" → "Service Accounts" →
   "Create Service Account" — un nombre alcanza, no hace falta asignarle roles de
   IAM (la autorización real va por delegación de dominio, no por IAM).
4. **Descargar la clave JSON**: dentro de la cuenta de servicio recién creada →
   pestaña "Keys" → "Add Key" → "Create new key" → tipo **JSON**. Se descarga un
   archivo — esa es la ruta que va en `PORTALSAAS_TEST_GOOGLE_SERVICE_ACCOUNT_JSON_PATH`.
5. **Anotar el "Client ID" numérico** de la cuenta de servicio (distinto del
   `client_email` que trae el JSON) — está en los detalles de la cuenta de servicio,
   sección "Advanced settings"/"Domain-wide delegation".
6. **Autorizar la delegación de dominio en el Workspace**: entrar a
   https://admin.google.com (como super admin) → "Security" → "Access and data
   control" → "API controls" → "Domain-wide delegation" → "Add new":
   - **Client ID**: el numérico del paso 5 (no el `client_email`).
   - **OAuth scopes**: `https://www.googleapis.com/auth/gmail.send`
   - Guardar/autorizar.
7. **Elegir la casilla que va a enviar** (ej. `no-reply@comercialdepor.cl`) — tiene
   que ser una casilla real del Workspace (usuario o alias existente); la cuenta de
   servicio la "impersona" para enviar en su nombre. Esa es
   `PORTALSAAS_TEST_SENDER_EMAIL`.

Con eso ya se puede correr la herramienta (ver "Variables de entorno" abajo).

## Variables de entorno

Comunes:
```powershell
$env:PORTALSAAS_TEST_PROVIDER = "microsoft365"   # o "google_workspace"
$env:PORTALSAAS_TEST_SENDER_EMAIL = "no-reply@tudominio.cl"
$env:PORTALSAAS_TEST_SENDER_DISPLAY_NAME = "Portal SAP"   # opcional
$env:PORTALSAAS_TEST_RECIPIENT_EMAIL = "tu-correo-personal@tudominio.cl"
```

Si `PORTALSAAS_TEST_PROVIDER = "microsoft365"`:
```powershell
$env:PORTALSAAS_TEST_MS_TENANT_ID = "..."
$env:PORTALSAAS_TEST_MS_CLIENT_ID = "..."
$env:PORTALSAAS_TEST_MS_CLIENT_SECRET = "..."
```

Si `PORTALSAAS_TEST_PROVIDER = "google_workspace"`:
```powershell
$env:PORTALSAAS_TEST_GOOGLE_SERVICE_ACCOUNT_JSON_PATH = "C:\ruta\a\service-account.json"
```
(el archivo tal cual lo descarga Google Cloud Console — la herramienta lee
`client_email` y `private_key` de ahí, no hace falta partirlo a mano.)

## Correr

```powershell
dotnet run --project tools/PortalSaas.Tools.EmailSmokeTest/PortalSaas.Tools.EmailSmokeTest.csproj
```

Imprime `ÉXITO` si el proveedor aceptó el correo (revisa la bandeja de
`PORTALSAAS_TEST_RECIPIENT_EMAIL`), o `FALLÓ` con la excepción completa si algo salió
mal (credenciales, permisos, formato).

## Qué NO hace

- No persiste nada — usa una base EF Core InMemory descartable, no toca ningún
  Postgres/SQL Server real.
- No prueba `IAuthenticationService`/`IPasswordResetService`/`IUserPreferenceService`
  — solo el envío de correo.
- No reemplaza los tests automatizados (`tests/PortalSaas.Core.Tests`) — es un
  complemento manual para el único tramo que esos tests no pueden cubrir sin
  credenciales reales (la llamada HTTP real a Graph/Gmail API).
