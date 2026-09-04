# Gestión de contraseñas desde Administración de Usuarios — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir al administrador de una organización generar/restablecer la contraseña de un usuario desde `Editar.cshtml` (enviada por correo o mostrada una vez en pantalla), y exigir una política de complejidad estándar tanto ahí como cuando un usuario define su propia contraseña vía `ResetPassword`.

**Architecture:** Dos clases nuevas y puras en `PortalSaas.Core/Seguridad` (`PasswordPolicy`, `RandomPasswordGenerator`), un método nuevo en el servicio ya existente `ITenantUserAdminService`/`TenantUserAdminService` (`SetPasswordAsync`), un `OnPostAsync` de `ResetPasswordModel` (Host) que valida contra `PasswordPolicy`, y un handler nuevo `OnPostGenerarPasswordAsync` en `EditarModel` (plugin `Modulo.Administracion`) que reusa `SetPasswordAsync` + `IEmailSenderService` ya inyectado.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 8), EF Core (InMemory para tests), xUnit (sin librería de mocks — el proyecto usa fakes escritos a mano, ver `CurrentUserContextFijo` en `TenantUserAdminServiceTests.cs`).

## Global Constraints

- Política de contraseña estándar (spec 2026-09-04): mínimo 10 caracteres, al menos 1 mayúscula, 1 minúscula, 1 dígito, 1 símbolo.
- El generador usa `System.Security.Cryptography.RandomNumberGenerator`, nunca `System.Random`.
- El generador excluye caracteres visualmente ambiguos: `0`, `O`, `1`, `l`, `I`.
- Un fallo de envío de correo nunca revierte el cambio de contraseña ya aplicado (falla hacia adelante, mismo criterio que `EnviarInvitacionAsync` existente).
- 2FA/MFA está fuera de alcance de este plan.

---

### Task 1: `PasswordPolicy`

**Files:**
- Create: `src/PortalSaas.Core/Seguridad/PasswordPolicy.cs`
- Test: `tests/PortalSaas.Core.Tests/Seguridad/PasswordPolicyTests.cs`

**Interfaces:**
- Produces: `PortalSaas.Core.Seguridad.PasswordPolicy.Validate(string password) : IReadOnlyList<string>` — lista vacía si la contraseña cumple la política; si no, un mensaje por cada regla incumplida.

- [ ] **Step 1: Write the failing test**

```csharp
using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class PasswordPolicyTests
{
    [Fact]
    public void Validate_ContrasenaQueCumpleTodasLasReglas_NoDevuelveErrores()
    {
        var errores = PasswordPolicy.Validate("Abcdef12!$");

        Assert.Empty(errores);
    }

    [Fact]
    public void Validate_MenosDeDiezCaracteres_DevuelveErrorDeLongitud()
    {
        var errores = PasswordPolicy.Validate("Ab1!Ab1!");

        Assert.Contains(errores, e => e.Contains("10 caracteres"));
    }

    [Fact]
    public void Validate_SinMayuscula_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("abcdefg12!$");

        Assert.Contains(errores, e => e.Contains("mayúscula"));
    }

    [Fact]
    public void Validate_SinMinuscula_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("ABCDEFG12!$");

        Assert.Contains(errores, e => e.Contains("minúscula"));
    }

    [Fact]
    public void Validate_SinDigito_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("Abcdefghi!$");

        Assert.Contains(errores, e => e.Contains("número"));
    }

    [Fact]
    public void Validate_SinSimbolo_DevuelveErrorCorrespondiente()
    {
        var errores = PasswordPolicy.Validate("Abcdefghi12");

        Assert.Contains(errores, e => e.Contains("símbolo"));
    }

    [Fact]
    public void Validate_ContrasenaVacia_DevuelveTodosLosErroresAplicables()
    {
        var errores = PasswordPolicy.Validate("");

        Assert.Equal(5, errores.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter FullyQualifiedName~PasswordPolicyTests`
Expected: FAIL (compilación falla, `PasswordPolicy` no existe)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Text.RegularExpressions;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Política de complejidad estándar (spec 2026-09-04): mínimo 10 caracteres, al menos
/// 1 mayúscula, 1 minúscula, 1 dígito, 1 símbolo. Se aplica tanto a la contraseña que
/// elige un usuario (ResetPassword.cshtml.cs) como a la que genera RandomPasswordGenerator.
/// </summary>
public static class PasswordPolicy
{
    private const int MinLength = 10;
    private static readonly Regex SymbolPattern = new(@"[^a-zA-Z0-9]", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(string password)
    {
        var errores = new List<string>();

        if (password.Length < MinLength)
        {
            errores.Add($"La contraseña debe tener al menos {MinLength} caracteres.");
        }

        if (!password.Any(char.IsUpper))
        {
            errores.Add("La contraseña debe incluir al menos una mayúscula.");
        }

        if (!password.Any(char.IsLower))
        {
            errores.Add("La contraseña debe incluir al menos una minúscula.");
        }

        if (!password.Any(char.IsDigit))
        {
            errores.Add("La contraseña debe incluir al menos un número.");
        }

        if (!SymbolPattern.IsMatch(password))
        {
            errores.Add("La contraseña debe incluir al menos un símbolo.");
        }

        return errores;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter FullyQualifiedName~PasswordPolicyTests`
Expected: PASS (7 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Core/Seguridad/PasswordPolicy.cs tests/PortalSaas.Core.Tests/Seguridad/PasswordPolicyTests.cs
git commit -m "feat(seguridad): agregar PasswordPolicy con regla estándar de complejidad

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 2: `RandomPasswordGenerator`

**Files:**
- Create: `src/PortalSaas.Core/Seguridad/RandomPasswordGenerator.cs`
- Test: `tests/PortalSaas.Core.Tests/Seguridad/RandomPasswordGeneratorTests.cs`

**Interfaces:**
- Consumes: `PasswordPolicy.Validate(string) : IReadOnlyList<string>` (Task 1).
- Produces: `PortalSaas.Core.Seguridad.RandomPasswordGenerator.Generate(int length = 14) : string`.

- [ ] **Step 1: Write the failing test**

```csharp
using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class RandomPasswordGeneratorTests
{
    [Fact]
    public void Generate_SiempreCumpleLaPoliticaDeContrasena()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = RandomPasswordGenerator.Generate();
            var errores = PasswordPolicy.Validate(password);

            Assert.Empty(errores);
        }
    }

    [Fact]
    public void Generate_DosLlamadasConsecutivas_ProducenValoresDistintos()
    {
        var primera = RandomPasswordGenerator.Generate();
        var segunda = RandomPasswordGenerator.Generate();

        Assert.NotEqual(primera, segunda);
    }

    [Fact]
    public void Generate_RespetaLaLongitudSolicitada()
    {
        var password = RandomPasswordGenerator.Generate(length: 20);

        Assert.Equal(20, password.Length);
    }

    [Fact]
    public void Generate_NoContieneCaracteresAmbiguos()
    {
        for (var i = 0; i < 200; i++)
        {
            var password = RandomPasswordGenerator.Generate();

            Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter FullyQualifiedName~RandomPasswordGeneratorTests`
Expected: FAIL (compilación falla, `RandomPasswordGenerator` no existe)

- [ ] **Step 3: Write minimal implementation**

```csharp
using System.Security.Cryptography;

namespace PortalSaas.Core.Seguridad;

/// <summary>
/// Genera contraseñas aleatorias que siempre cumplen PasswordPolicy. Usa
/// RandomNumberGenerator (no System.Random) -- mismo criterio criptográfico que
/// PasswordHasher y PasswordResetService. Excluye caracteres visualmente ambiguos
/// (0/O, 1/l/I) para que un admin pueda transcribirla o leerla en voz alta sin errores.
/// </summary>
public static class RandomPasswordGenerator
{
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*-_=+?";
    private const string AllChars = Uppercase + Lowercase + Digits + Symbols;

    public static string Generate(int length = 14)
    {
        var chars = new char[length];

        // Garantiza al menos un carácter de cada categoría exigida por PasswordPolicy.
        chars[0] = PickFrom(Uppercase);
        chars[1] = PickFrom(Lowercase);
        chars[2] = PickFrom(Digits);
        chars[3] = PickFrom(Symbols);

        for (var i = 4; i < length; i++)
        {
            chars[i] = PickFrom(AllChars);
        }

        Shuffle(chars);

        return new string(chars);
    }

    private static char PickFrom(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    private static void Shuffle(char[] chars)
    {
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter FullyQualifiedName~RandomPasswordGeneratorTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Core/Seguridad/RandomPasswordGenerator.cs tests/PortalSaas.Core.Tests/Seguridad/RandomPasswordGeneratorTests.cs
git commit -m "feat(seguridad): agregar RandomPasswordGenerator que cumple PasswordPolicy

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 3: `ITenantUserAdminService.SetPasswordAsync`

**Files:**
- Modify: `src/PortalSaas.Abstractions/Contratos/ITenantUserAdminService.cs` (agregar método a la interfaz)
- Modify: `src/PortalSaas.Core/Administracion/TenantUserAdminService.cs:293-316` (implementación, junto a `SetDefaultCompanyAsync`)
- Modify: `tests/PortalSaas.Core.Tests/TenantUserAdminServiceTests.cs` (agregar tests al final de la clase existente)

**Interfaces:**
- Consumes: `PasswordHasher.Hash(string) : (string Hash, string Salt)` (ya existe), `FindOwnUserAsync(Guid, CancellationToken) : Task<User?>` (método privado ya existente en `TenantUserAdminService`).
- Produces: `ITenantUserAdminService.SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default) : Task<TenantUserOperationResult>`.

- [ ] **Step 1: Write the failing test**

Agregar al final de la clase `TenantUserAdminServiceTests` (antes del `}` de cierre, reusa `CrearOrganizacionConPlanAsync` y `CrearServicio` ya definidos en el archivo):

```csharp
    [Fact]
    public async Task SetPasswordAsync_UsuarioPropio_CambiaElHashYReseteaBloqueo()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id);
        var alta = await servicio.CreateAsync("jperez", "jperez@test.cl", isAdmin: false);
        var usuario = await db.Users.FindAsync(alta.UserId);
        usuario!.FailedLoginAttempts = 3;
        usuario.IsLocked = true;
        await db.SaveChangesAsync();

        var resultado = await servicio.SetPasswordAsync(alta.UserId!.Value, "Nueva.Clave12!");

        Assert.True(resultado.IsSuccess);
        var actualizado = await db.Users.FindAsync(alta.UserId);
        Assert.True(PasswordHasher.Verify("Nueva.Clave12!", actualizado!.PasswordHash, actualizado.PasswordSalt));
        Assert.Equal(0, actualizado.FailedLoginAttempts);
        Assert.False(actualizado.IsLocked);
    }

    [Fact]
    public async Task SetPasswordAsync_UsuarioDeOtraOrganizacion_Rechaza()
    {
        var (db, org, _) = await CrearOrganizacionConPlanAsync();
        var servicio = CrearServicio(db, org.Id);
        var alta = await servicio.CreateAsync("jperez", "jperez@test.cl", isAdmin: false);

        var servicioDeOtraOrg = CrearServicio(db, Guid.NewGuid());
        var resultado = await servicioDeOtraOrg.SetPasswordAsync(alta.UserId!.Value, "Nueva.Clave12!");

        Assert.False(resultado.IsSuccess);
    }
```

Agregar el using necesario al inicio del archivo si no está: `using PortalSaas.Core.Seguridad;` (ya está, se usa indirectamente vía `TenantUserAdminService`; si `PasswordHasher.Verify` no resuelve, agregarlo).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter FullyQualifiedName~SetPasswordAsync`
Expected: FAIL (compilación falla, `SetPasswordAsync` no existe en `ITenantUserAdminService`)

- [ ] **Step 3: Write minimal implementation**

En `ITenantUserAdminService.cs`, agregar antes del cierre de la interfaz (después de `SetDefaultCompanyAsync`):

```csharp
    /// <summary>
    /// Fija una contraseña ya generada por el llamador (admin, vía RandomPasswordGenerator)
    /// -- a diferencia de CreateAsync, acá el hash SÍ se comunica (por correo o en pantalla),
    /// por eso el llamador es responsable de que newPassword cumpla PasswordPolicy antes de
    /// invocar este método. Resetea intentos fallidos y desbloquea, mismo criterio que
    /// PasswordResetService.ResetPasswordAsync.
    /// </summary>
    Task<TenantUserOperationResult> SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default);
```

En `TenantUserAdminService.cs`, agregar el método después de `SetDefaultCompanyAsync` (línea ~316, antes de los métodos privados):

```csharp
    public async Task<TenantUserOperationResult> SetPasswordAsync(Guid userId, string newPassword, CancellationToken ct = default)
    {
        var user = await FindOwnUserAsync(userId, ct);
        if (user is null)
        {
            return TenantUserOperationResult.Failure("Usuario no encontrado.");
        }

        var (hash, salt) = PasswordHasher.Hash(newPassword);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        user.FailedLoginAttempts = 0;
        user.IsLocked = false;

        await _db.SaveChangesAsync(ct);

        return TenantUserOperationResult.Success(user.Id);
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/PortalSaas.Core.Tests --filter FullyQualifiedName~SetPasswordAsync`
Expected: PASS (2 tests)

Run también la suite completa para confirmar que no rompiste nada: `dotnet test tests/PortalSaas.Core.Tests`
Expected: PASS (todos los tests existentes siguen en verde)

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Abstractions/Contratos/ITenantUserAdminService.cs src/PortalSaas.Core/Administracion/TenantUserAdminService.cs tests/PortalSaas.Core.Tests/TenantUserAdminServiceTests.cs
git commit -m "feat(administracion): agregar SetPasswordAsync a ITenantUserAdminService

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 4: Validar `PasswordPolicy` en `ResetPassword.cshtml.cs`

**Files:**
- Modify: `src/PortalSaas.Host/Pages/Account/ResetPassword.cshtml.cs:27-42`

**Interfaces:**
- Consumes: `PasswordPolicy.Validate(string) : IReadOnlyList<string>` (Task 1).

No hay proyecto de tests para `PortalSaas.Host` en la solución (solo existe `tests/PortalSaas.Core.Tests`) — este task se verifica manualmente en el paso 3, no con un test automatizado.

- [ ] **Step 1: Modificar `OnPostAsync` para validar la política antes de aplicar la contraseña**

Reemplazar el cuerpo de `OnPostAsync` en `ResetPassword.cshtml.cs`:

```csharp
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ConfirmPassword)}", "Las contraseñas no coinciden.");
            return Page();
        }

        foreach (var error in PasswordPolicy.Validate(Input.NewPassword))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.NewPassword)}", error);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        Succeeded = await _passwordResetService.ResetPasswordAsync(Input.Token, Input.NewPassword);
        return Page();
    }
```

Agregar el using al inicio del archivo:

```csharp
using PortalSaas.Core.Seguridad;
```

Y quitar el atributo `[MinLength(8, ...)]` de `InputModel.NewPassword` (línea 51) ya que `PasswordPolicy` reemplaza esa validación con las reglas completas:

```csharp
        [Required(ErrorMessage = "Ingresa tu nueva contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Contraseña nueva")]
        public string NewPassword { get; set; } = string.Empty;
```

- [ ] **Step 2: Compilar**

Run: `dotnet build "Portal SaaS - Core"` (o el `.sln` de la solución)
Expected: build exitoso, sin errores

- [ ] **Step 3: Verificación manual**

Levantar el Host, ir a `/Account/ResetPassword?token=<token-válido>` (generarlo probando el flujo de "olvidé mi contraseña" o creando un usuario nuevo), e intentar:
- Una contraseña corta o sin símbolo → debe mostrar los mensajes de error de `PasswordPolicy` bajo el campo.
- Una contraseña válida (ej. `Abcdef12!$`) → debe aplicarse correctamente (`Succeeded = true`).

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Pages/Account/ResetPassword.cshtml.cs"
git commit -m "feat(account): validar PasswordPolicy al elegir contraseña en ResetPassword

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

### Task 5: Generar/enviar contraseña desde `Editar.cshtml` (Administración)

**Files:**
- Modify: `plugins/Modulo.Administracion/Pages/Usuarios/Editar.cshtml.cs:11-241`
- Modify: `plugins/Modulo.Administracion/Pages/Usuarios/Editar.cshtml:66-103` (tab "Datos")

**Interfaces:**
- Consumes: `RandomPasswordGenerator.Generate() : string` (Task 2), `ITenantUserAdminService.SetPasswordAsync(Guid, string, CancellationToken) : Task<TenantUserOperationResult>` (Task 3), `IEmailSenderService.SendAsync(Guid, EmailMessage, CancellationToken)` (ya inyectado en `EditarModel`).

No hay proyecto de tests para `Modulo.Administracion` (mismo caso que Task 4) — este task se verifica manualmente en el paso 3.

- [ ] **Step 1: Agregar el handler y el TempData de una sola lectura en `Editar.cshtml.cs`**

Agregar la propiedad TempData junto a `Input`/`Detalle` (después de la línea 39, `public Guid SelectedCompanyId { get; set; }`):

```csharp
    [TempData]
    public string? NuevaPasswordGenerada { get; set; }
```

Agregar el handler nuevo después de `OnPostGuardarAsync` (después de la línea 149, antes de `OnPostGuardarPermisosAsync`):

```csharp
    public async Task<IActionResult> OnPostGenerarPasswordAsync(Guid id, bool enviarPorCorreo)
    {
        var detalle = await _usuarios.GetAsync(id);
        if (detalle is null)
        {
            return NotFound();
        }

        var password = RandomPasswordGenerator.Generate();
        var resultado = await _usuarios.SetPasswordAsync(id, password);
        if (!resultado.IsSuccess)
        {
            MensajeError = resultado.Reason;
            return RedirectToPage(new { id });
        }

        if (!enviarPorCorreo)
        {
            NuevaPasswordGenerada = password;
            MensajeExito = "Se generó una contraseña nueva. Cópiala ahora: no se volverá a mostrar.";
            return RedirectToPage(new { id });
        }

        var organizationId = CurrentUser.OrganizationId;
        try
        {
            await _emailSenderService.SendAsync(organizationId, new EmailMessage(
                detalle.Email,
                "Tu contraseña fue actualizada — Portal SaaS",
                $"""
                <p>Un administrador generó una contraseña nueva para tu cuenta en el Portal SaaS.</p>
                <p>Tu nueva contraseña es: <strong>{password}</strong></p>
                <p>Te recomendamos cambiarla la próxima vez que inicies sesión.</p>
                """));
            MensajeExito = "Se generó una contraseña nueva y se envió por correo al usuario.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falló el envío del correo de contraseña nueva para el usuario {UserId} de la organización {OrganizationId}", id, organizationId);
            MensajeError = "Se generó la contraseña nueva, pero no se pudo enviar el correo (revisa la configuración de correo de la organización).";
        }

        return RedirectToPage(new { id });
    }
```

Agregar el `using` necesario al inicio del archivo (junto a los existentes):

```csharp
using PortalSaas.Core.Seguridad;
```

- [ ] **Step 2: Agregar la sección "Contraseña" en `Editar.cshtml`**

En `Editar.cshtml`, dentro del panel `data-tab-panel="datos"`, agregar después del `</form>` de la línea 102 y antes del `</div>` de cierre del panel (línea 103), solo cuando `!Model.EsNuevo` (un usuario nuevo todavía no tiene contraseña que resetear — la recibe por la invitación existente):

```razor
    @if (!Model.EsNuevo)
    {
        <hr class="my-4" />
        <h2 class="h5 mb-1">Contraseña</h2>
        <p class="text-muted mb-3">Genera una contraseña nueva que cumple la política de seguridad (mínimo 10 caracteres, mayúscula, minúscula, número y símbolo).</p>
        <div class="d-flex flex-wrap gap-2">
            <form method="post" asp-page-handler="GenerarPassword" asp-route-id="@Model.Detalle!.Id">
                <input type="hidden" name="enviarPorCorreo" value="true" />
                <button type="submit" class="btn-secondary">Enviar contraseña nueva por correo</button>
            </form>
            <form method="post" asp-page-handler="GenerarPassword" asp-route-id="@Model.Detalle!.Id">
                <input type="hidden" name="enviarPorCorreo" value="false" />
                <button type="submit" class="btn-secondary">Generar y mostrar contraseña</button>
            </form>
        </div>
    }
```

Agregar el modal al final del archivo, antes de `@section Scripts {` (línea 218), solo se renderiza si hay una contraseña recién generada para mostrar:

```razor
@if (Model.NuevaPasswordGenerada is not null)
{
    <div class="modal-backdrop-custom" id="modal-nueva-password" style="position:fixed;inset:0;background:rgba(0,0,0,.5);display:flex;align-items:center;justify-content:center;z-index:1050;">
        <div class="card-surface" style="max-width:420px;width:90%;">
            <h2 class="h5 mb-2">Contraseña generada</h2>
            <p class="text-muted mb-3">Cópiala ahora: por seguridad, no se volverá a mostrar.</p>
            <div class="d-flex align-items-center gap-2 mb-3">
                <input id="input-nueva-password" type="text" class="form-control" value="@Model.NuevaPasswordGenerada" readonly />
                <button type="button" class="btn-secondary" onclick="copiarPassword()">Copiar</button>
            </div>
            <button type="button" class="btn-primary" onclick="document.getElementById('modal-nueva-password').remove()">Cerrar</button>
        </div>
    </div>
}
```

Dentro del bloque `@section Scripts { ... }` existente (línea 218-239), agregar la función `copiarPassword` antes del cierre `}` de la sección (después del `})();` de la línea 237):

```javascript
        function copiarPassword() {
            var input = document.getElementById('input-nueva-password');
            input.select();
            navigator.clipboard.writeText(input.value);
        }
```

- [ ] **Step 3: Compilar y verificar manualmente**

Run: `dotnet build "Portal SaaS - Core"`
Expected: build exitoso.

Levantar el Host, ir a `/organizacion/usuarios/editar/<id-de-un-usuario-existente>`, y probar:
- Botón "Generar y mostrar contraseña" → aparece el modal con una contraseña que cumple la política visualmente (10+ caracteres, mezcla de mayúsculas/minúsculas/números/símbolos); recargar la página confirma que el modal ya no aparece (TempData de una sola lectura).
- Botón "Enviar contraseña nueva por correo" → llega el correo al buzón configurado con la contraseña en texto plano; mensaje de éxito en pantalla.
- Con la organización sin `email_settings` configurada (o forzando un error), confirmar que el mensaje cambia a la variante de "se generó pero no se pudo enviar" y que el login del usuario ya acepta la contraseña nueva (el cambio se aplicó igual).

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Usuarios/Editar.cshtml.cs" "Portal SaaS - Core/plugins/Modulo.Administracion/Pages/Usuarios/Editar.cshtml"
git commit -m "feat(administracion): generar y enviar/mostrar contraseña nueva desde Editar usuario

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Self-Review Notes

- **Cobertura del spec:** política de complejidad → Task 1; generador aleatorio → Task 2; aplicación de la política al elegir contraseña propia → Task 4; generación/entrega desde Administración (correo + mostrar una vez) → Task 5; persistencia del nuevo hash → Task 3. 2FA queda fuera, según el spec.
- **Sin placeholders:** cada step trae el código completo a escribir.
- **Consistencia de tipos:** `RandomPasswordGenerator.Generate()`, `PasswordPolicy.Validate(string)` y `ITenantUserAdminService.SetPasswordAsync(Guid, string, CancellationToken)` se usan con la misma firma en todos los tasks que los consumen.
- **Sin test project para Host/plugins:** Tasks 4 y 5 no tienen test automatizado porque la solución no tiene un proyecto de test para `PortalSaas.Host` ni para `Modulo.Administracion` (confirmado — solo existe `tests/PortalSaas.Core.Tests`); se verifican manualmente como indica cada task.
