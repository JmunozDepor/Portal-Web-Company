# Gestión de contraseñas desde Administración de Usuarios

**Fecha:** 2026-09-04
**Estado:** Aprobado

## Contexto

El módulo `Modulo.Administracion` (`Pages/Usuarios/Editar.cshtml(.cs)`) permite editar los datos de un usuario (usuario, correo, admin/activo/bloqueado) y sus permisos, pero **no ofrece ninguna acción sobre la contraseña**: no se puede generar una nueva, ni resetearla desde el panel de administración.

Ya existe infraestructura reusable:
- `IPasswordResetService` / `PasswordResetService` — tokens de reset hasheados (SHA-256), expiración 1h, tabla `password_reset_tokens`. Hoy solo se usa en el flujo de "olvidé mi contraseña" (`Pages/Account/ForgotPassword.cshtml`, `ResetPassword.cshtml`) y en la invitación de usuario nuevo.
- `IEmailSenderService` — despacho de correo dual (Google Workspace / Microsoft 365) por organización, ya usado para invitaciones (`Editar.cshtml.cs`).
- `PasswordHasher` — PBKDF2-SHA256, sin política de complejidad asociada.

No existe hoy ninguna política de complejidad de contraseña (`PasswordPolicy`) en la solución: `ResetPassword.cshtml.cs` acepta cualquier valor no vacío. Tampoco existe 2FA/MFA — queda explícitamente fuera de este spec (evaluación pendiente para otra conversación).

## Objetivo

1. Definir una política de complejidad de contraseña estándar y aplicarla tanto a la generación aleatoria como a la definición manual (usuario final vía `ResetPassword`).
2. Permitir al administrador, desde `Editar.cshtml`, generar una contraseña nueva para un usuario con dos modos de entrega: por correo, o mostrada una única vez en pantalla.

Fuera de alcance: 2FA/MFA, políticas de expiración/histórico de contraseñas, contraseñas prohibidas por diccionario.

## Diseño

### Política de contraseña — `PasswordPolicy`

Nueva clase en `src/PortalSaas.Core/Seguridad/PasswordPolicy.cs`:

- Regla estándar: mínimo 10 caracteres, al menos 1 mayúscula, 1 minúscula, 1 dígito, 1 símbolo.
- `IReadOnlyList<string> Validate(string password)` — devuelve la lista de reglas incumplidas (vacía si es válida). Se usa tanto para validar entrada de usuario como para verificar que el generador produce contraseñas válidas.

### Generador — `RandomPasswordGenerator`

Nueva clase en `src/PortalSaas.Core/Seguridad/RandomPasswordGenerator.cs`:

- `string Generate(int length = 14)`.
- Usa `System.Security.Cryptography.RandomNumberGenerator` (no `System.Random`).
- Construye garantizando al menos un carácter de cada categoría exigida por `PasswordPolicy`, rellena el resto desde el conjunto combinado, mezcla el resultado.
- Excluye caracteres ambiguos visualmente (`0/O`, `1/l/I`) para reducir errores al copiar manualmente.

### Validación en `ResetPassword.cshtml.cs`

`OnPostAsync` aplica `PasswordPolicy.Validate(Input.NewPassword)` antes de aceptar la contraseña elegida por el usuario; errores se agregan a `ModelState` por campo, igual que la validación existente de "las contraseñas no coinciden".

### Flujo de administración — `Editar.cshtml` / `Editar.cshtml.cs`

En el tab "Datos", nueva sección "Contraseña" con dos botones:

- **"Enviar contraseña nueva por correo"** — POST a `OnPostGenerarPasswordAsync(enviarPorCorreo: true)`.
- **"Generar y mostrar contraseña"** — POST a `OnPostGenerarPasswordAsync(enviarPorCorreo: false)`.

Handler `OnPostGenerarPasswordAsync(bool enviarPorCorreo)`:

1. Carga el usuario por id (mismo patrón que `OnPostGuardarAsync`).
2. `var password = RandomPasswordGenerator.Generate();`
3. Hashea con `PasswordHasher` y persiste en `User` (mismo campo que usa el login).
4. Si `enviarPorCorreo`: arma el correo (asunto/cuerpo similar a la plantilla de invitación existente) y llama a `_emailSenderService.SendAsync(organizationId, mensaje)`. Si el envío falla, el cambio de contraseña **igual queda aplicado** (falla hacia adelante, igual que hoy en la invitación) y se muestra al admin un mensaje de advertencia distinto ("contraseña generada pero no se pudo enviar el correo").
5. Si no `enviarPorCorreo`: guarda el password en `TempData["NuevaPasswordGenerada"]` (lectura única) y redirige (Post-Redirect-Get) a la misma página; el `OnGet` lee `TempData` y, si existe, la página muestra un modal con la contraseña y un botón "copiar al portapapeles". Al cerrarse el modal o recargar la página, el valor ya no está disponible (TempData se consume en la primera lectura).

No se agrega auditoría/log adicional a lo que ya exista para cambios de usuario — fuera de alcance.

## Testing

- **`PasswordPolicyTests`**: casos válidos e inválidos por cada regla (longitud, mayúscula, minúscula, dígito, símbolo).
- **`RandomPasswordGeneratorTests`**: N iteraciones generadas siempre pasan `PasswordPolicy.Validate`; dos generaciones consecutivas no son iguales (chequeo básico de aleatoriedad); no contiene caracteres ambiguos excluidos.
- **`EditarModelTests`** (o el proyecto de test existente del plugin, si lo hay): handler `OnPostGenerarPasswordAsync` con `enviarPorCorreo:false` persiste el hash correcto y no llama al email sender; con `enviarPorCorreo:true` llama a `IEmailSenderService.SendAsync` con el destinatario correcto.
- **`ResetPasswordModelTests`**: contraseña que no cumple la política es rechazada con el mensaje de error correspondiente; contraseña válida es aceptada.

## Notas

- 2FA queda explícitamente fuera de este spec por decisión del usuario (2026-09-04): se evaluará en una conversación separada dado que hoy no existe ninguna base de MFA/TOTP en el código y es una pieza independiente y de mayor alcance.
