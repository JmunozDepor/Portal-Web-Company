# Autenticación Máquina-a-Máquina Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar al Portal una forma de autenticar llamadas HTTP de sistemas externos (sin sesión de usuario) y resolver automáticamente la compañía asociada, reutilizando `ICurrentCompanyAccessor` sin modificarlo.

**Architecture:** Entidad `ApiClientCredential` (hash de la key, nunca la key en texto plano) + un servicio testeable `ApiKeyAuthenticator` que valida la key y arma los datos de compañía + un `AuthenticationHandler` delgado de ASP.NET Core que solo traduce ese resultado a un `ClaimsPrincipal` con los mismos tipos de claim que usa el login normal. UI admin de generación/revocación bajo `Admin/Organizations/Companies/ApiKeys/`.

**Tech Stack:** .NET 8, ASP.NET Core Authentication, EF Core 8.0.8, xUnit + EF Core InMemory, `System.Security.Cryptography` (SHA-256, `RandomNumberGenerator`).

## Global Constraints

- `ICurrentCompanyAccessor` (`src/PortalSaas.Core/Seguridad/CurrentCompanyAccessor.cs`) NO se modifica — lee claims `CompanyId`, `CompanyCode`, `CompanyDatabase`, `CompanyServiceLayerUrl`, `CompanyCountry` de `HttpContext.User`, sin importar qué esquema los puso ahí.
- Nunca se persiste la API key en texto plano ni cifrada — solo su hash SHA-256 en base64, siguiendo el mismo criterio que `UserSessionService.cs` ya usa para tokens de sesión ("el token ya es aleatorio de 256 bits, un hash rápido alcanza — no hace falta una KDF lenta").
- Generación de la key: `RandomNumberGenerator.GetBytes(32)` codificado en base64 (256 bits de entropía), mismo patrón que `UserSessionService.cs`.
- Nombres de tabla/columna en inglés, snake_case: tabla `api_client_credentials`, columnas `company_id` (FK), `is_active`, `created_at`, `last_used_at` — ver `docs/01-CONVENCION-NOMBRES-BD.md` y la migración de referencia `20260726220745_AddModuleExternalConnections.cs`.
- Nunca `HasDefaultValueSql` en `OnModelCreating`; defaults como inicializador de propiedad en C#.
- Migraciones EF Core: generar con `--project src/PortalSaas.Data.Migrations.PostgreSql` (o `...SqlServer`) y `--startup-project` apuntando al MISMO proyecto de migraciones (no a `PortalSaas.Host` — el sandbox de desarrollo no tiene acceso a la base de datos de producción configurada en Host, ver `docs/12-MOTOR-INTEGRACION-ERP-PENDIENTES.md` punto 8).
- Páginas admin usan `[Authorize(AuthenticationSchemes = "PlatformAdmin")]`, inyectan `PortalSaasDbContext` por constructor, siguen el estilo de `Admin/Organizations/Companies/ExternalConnections/Index.cshtml.cs` (listar + `OnPostDeleteAsync` con `RedirectToPage(new { companyId })`).
- Tests: xUnit + `UseInMemoryDatabase(Guid.NewGuid().ToString())`, sin mocking framework, siguiendo el patrón de `tests/PortalSaas.Core.Tests/`.
- Esquemas de autenticación se registran encadenados en `Program.cs:232-271` (`AddAuthentication(...).AddCookie(...).AddCookie("PlatformAdmin", ...)`) — el esquema nuevo se agrega con `.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ExternalApiKey", ...)` en la misma cadena.

---

## File Structure

**Nuevos archivos:**
- `src/PortalSaas.Data/Entities/ApiClientCredential.cs`
- `src/PortalSaas.Core/Seguridad/ApiKeyGenerator.cs` — helpers estáticos: generar key cruda, hashear.
- `src/PortalSaas.Abstractions/Contratos/IApiKeyAuthenticator.cs`
- `src/PortalSaas.Core/Seguridad/ApiKeyAuthenticator.cs`
- `src/PortalSaas.Core/Seguridad/ApiKeyAuthenticationHandler.cs` (+ `ApiKeyAuthenticationSchemeOptions` en el mismo archivo, es una clase de opciones trivial)
- `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ApiKeys/Index.cshtml` + `.cshtml.cs`
- `tests/PortalSaas.Core.Tests/Seguridad/ApiKeyGeneratorTests.cs`
- `tests/PortalSaas.Core.Tests/Seguridad/ApiKeyAuthenticatorTests.cs`

**Modificados:**
- `src/PortalSaas.Data/PortalSaasDbContext.cs` — nuevo `DbSet<ApiClientCredential>` + Fluent API.
- `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/` y `...SqlServer/Migrations/` — migración nueva.
- `src/PortalSaas.Host/Program.cs` — registrar el esquema `"ExternalApiKey"` y `IApiKeyAuthenticator` en DI.
- `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml` — agregar enlace a `ApiKeys/Index`, junto al enlace existente a `ExternalConnections/Index`.

---

### Task 1: Entidad `ApiClientCredential` + migraciones

**Files:**
- Create: `src/PortalSaas.Data/Entities/ApiClientCredential.cs`
- Modify: `src/PortalSaas.Data/PortalSaasDbContext.cs`
- Migraciones nuevas en `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/` y `src/PortalSaas.Data.Migrations.SqlServer/Migrations/`

**Interfaces:**
- Consumes: nada de tareas anteriores.
- Produces: `ApiClientCredential { Id, CompanyId, Nombre, ApiKeyHash, Activo, CreatedAt, LastUsedAt }` — usado por `ApiKeyAuthenticator` (Task 2) y la UI admin (Task 4).

- [ ] **Step 1: Crear la entidad**

```csharp
namespace PortalSaas.Data.Entities;

public class ApiClientCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CompanyId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}
```

- [ ] **Step 2: Registrar `DbSet` y Fluent API en `PortalSaasDbContext`**

Agregar junto a los `DbSet` existentes:

```csharp
public DbSet<ApiClientCredential> ApiClientCredentials => Set<ApiClientCredential>();
```

Dentro de `OnModelCreating`:

```csharp
modelBuilder.Entity<ApiClientCredential>(entity =>
{
    entity.ToTable("api_client_credentials");
    entity.Property(e => e.Id).HasColumnName("id");
    entity.Property(e => e.CompanyId).HasColumnName("company_id");
    entity.Property(e => e.Nombre).HasColumnName("name").HasMaxLength(200);
    entity.Property(e => e.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(100);
    entity.Property(e => e.Activo).HasColumnName("is_active");
    entity.Property(e => e.CreatedAt).HasColumnName("created_at");
    entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
    entity.HasIndex(e => e.ApiKeyHash).IsUnique();
    entity.HasIndex(e => new { e.CompanyId, e.Activo });
});
```

- [ ] **Step 3: Compilar `PortalSaas.Data`**

Run: `dotnet build src/PortalSaas.Data/PortalSaas.Data.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Generar migración Postgres**

Run: `dotnet ef migrations add AddApiClientCredentials --project src/PortalSaas.Data.Migrations.PostgreSql --startup-project src/PortalSaas.Data.Migrations.PostgreSql`
Expected: se crea archivo de migración nuevo en `src/PortalSaas.Data.Migrations.PostgreSql/Migrations/`.

- [ ] **Step 5: Generar migración SQL Server**

Run: `dotnet ef migrations add AddApiClientCredentials --project src/PortalSaas.Data.Migrations.SqlServer --startup-project src/PortalSaas.Data.Migrations.SqlServer`
Expected: se crea archivo de migración nuevo en `src/PortalSaas.Data.Migrations.SqlServer/Migrations/`.

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Data/Entities/ApiClientCredential.cs src/PortalSaas.Data/PortalSaasDbContext.cs src/PortalSaas.Data.Migrations.PostgreSql/Migrations/ src/PortalSaas.Data.Migrations.SqlServer/Migrations/
git commit -m "feat: agregar entidad ApiClientCredential y migraciones"
```

---

### Task 2: `ApiKeyGenerator` + `IApiKeyAuthenticator`/`ApiKeyAuthenticator`

**Files:**
- Create: `src/PortalSaas.Core/Seguridad/ApiKeyGenerator.cs`
- Create: `src/PortalSaas.Abstractions/Contratos/IApiKeyAuthenticator.cs`
- Create: `src/PortalSaas.Core/Seguridad/ApiKeyAuthenticator.cs`
- Test: `tests/PortalSaas.Core.Tests/Seguridad/ApiKeyGeneratorTests.cs`
- Test: `tests/PortalSaas.Core.Tests/Seguridad/ApiKeyAuthenticatorTests.cs`

**Interfaces:**
- Consumes: `ApiClientCredential`, `PortalSaasDbContext.ApiClientCredentials` (Task 1); `Company`, `PortalSaasDbContext.Companies` (ya existente, `src/PortalSaas.Data/PortalSaasDbContext.cs:41`).
- Produces: `ApiKeyGenerator.GenerateRawKey()`, `ApiKeyGenerator.Hash(string rawKey)`; `IApiKeyAuthenticator.AuthenticateAsync(string rawApiKey, CancellationToken)` retornando `ApiKeyAuthenticationResult` — consumido por `ApiKeyAuthenticationHandler` (Task 3) y por la UI admin (Task 4, para generar keys nuevas).

- [ ] **Step 1: Escribir el test de `ApiKeyGenerator` que falla**

```csharp
using PortalSaas.Core.Seguridad;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class ApiKeyGeneratorTests
{
    [Fact]
    public void GenerateRawKey_GeneraValoresDistintosCadaVez()
    {
        var primeraKey = ApiKeyGenerator.GenerateRawKey();
        var segundaKey = ApiKeyGenerator.GenerateRawKey();

        Assert.NotEqual(primeraKey, segundaKey);
    }

    [Fact]
    public void Hash_EsDeterministaParaElMismoValor()
    {
        var hash1 = ApiKeyGenerator.Hash("mi-clave-de-prueba");
        var hash2 = ApiKeyGenerator.Hash("mi-clave-de-prueba");

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_EsDistintoParaValoresDistintos()
    {
        var hashA = ApiKeyGenerator.Hash("clave-a");
        var hashB = ApiKeyGenerator.Hash("clave-b");

        Assert.NotEqual(hashA, hashB);
    }

    [Fact]
    public void Hash_NuncaContieneLaClaveOriginal()
    {
        var claveOriginal = "clave-super-secreta";
        var hash = ApiKeyGenerator.Hash(claveOriginal);

        Assert.DoesNotContain(claveOriginal, hash);
    }
}
```

- [ ] **Step 2: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter ApiKeyGeneratorTests`
Expected: FAIL — `ApiKeyGenerator` no existe.

- [ ] **Step 3: Implementar `ApiKeyGenerator`**

```csharp
using System.Security.Cryptography;

namespace PortalSaas.Core.Seguridad;

public static class ApiKeyGenerator
{
    public static string GenerateRawKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string rawKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(rawKey);
        return Convert.ToBase64String(SHA256.HashData(bytes));
    }
}
```

- [ ] **Step 4: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter ApiKeyGeneratorTests`
Expected: PASS, 4/4.

- [ ] **Step 5: Crear `IApiKeyAuthenticator` en Abstractions**

```csharp
namespace PortalSaas.Abstractions.Contratos;

public interface IApiKeyAuthenticator
{
    Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken);
}

public sealed class ApiKeyAuthenticationResult
{
    public required bool Success { get; init; }
    public Guid? CompanyId { get; init; }
    public string? CompanyCode { get; init; }
    public string? CompanyDatabase { get; init; }
    public string? CompanyServiceLayerUrl { get; init; }
    public string? CompanyCountry { get; init; }

    public static ApiKeyAuthenticationResult Failure() => new() { Success = false };

    public static ApiKeyAuthenticationResult Ok(Guid companyId, string companyCode, string companyDatabase, string companyServiceLayerUrl, string companyCountry) => new()
    {
        Success = true,
        CompanyId = companyId,
        CompanyCode = companyCode,
        CompanyDatabase = companyDatabase,
        CompanyServiceLayerUrl = companyServiceLayerUrl,
        CompanyCountry = companyCountry,
    };
}
```

- [ ] **Step 6: Escribir el test de `ApiKeyAuthenticator` que falla**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;
using Xunit;

namespace PortalSaas.Core.Tests.Seguridad;

public class ApiKeyAuthenticatorTests
{
    private static PortalSaasDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<PortalSaasDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PortalSaasDbContext(options);
    }

    private static async Task<Company> SembrarCompanyAsync(PortalSaasDbContext contexto)
    {
        var company = new Company
        {
            OrganizationId = Guid.NewGuid(),
            InstanceId = 1,
            Code = "DEPOR01",
            Name = "Comercial Depor",
            DatabaseName = "DEPOR_PRD",
            ServiceLayerUrl = "https://sap.example.com:50000/b1s/v1",
            IntegrationUsername = "integracion",
            IntegrationSecretKey = "no-usado-en-este-test",
            Country = "CL",
            IsActive = true,
        };
        contexto.Companies.Add(company);
        await contexto.SaveChangesAsync();
        return company;
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyValidaYActiva_RetornaDatosDeLaCompany()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        var rawKey = ApiKeyGenerator.GenerateRawKey();
        contexto.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        });
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        var resultado = await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        Assert.True(resultado.Success);
        Assert.Equal(company.Id, resultado.CompanyId);
        Assert.Equal("DEPOR01", resultado.CompanyCode);
        Assert.Equal("DEPOR_PRD", resultado.CompanyDatabase);
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyInexistente_RetornaFailure()
    {
        await using var contexto = CrearContexto();
        var authenticator = new ApiKeyAuthenticator(contexto);

        var resultado = await authenticator.AuthenticateAsync("clave-que-no-existe", CancellationToken.None);

        Assert.False(resultado.Success);
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyRevocada_RetornaFailure()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        var rawKey = ApiKeyGenerator.GenerateRawKey();
        contexto.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = false,
        });
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        var resultado = await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        Assert.False(resultado.Success);
    }

    [Fact]
    public async Task AuthenticateAsync_ConKeyValida_ActualizaLastUsedAt()
    {
        await using var contexto = CrearContexto();
        var company = await SembrarCompanyAsync(contexto);
        var rawKey = ApiKeyGenerator.GenerateRawKey();
        var credencial = new ApiClientCredential
        {
            CompanyId = company.Id,
            Nombre = "Test",
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        };
        contexto.ApiClientCredentials.Add(credencial);
        await contexto.SaveChangesAsync();

        var authenticator = new ApiKeyAuthenticator(contexto);
        await authenticator.AuthenticateAsync(rawKey, CancellationToken.None);

        var credencialActualizada = await contexto.ApiClientCredentials.FirstAsync(c => c.Id == credencial.Id);
        Assert.NotNull(credencialActualizada.LastUsedAt);
    }
}
```

- [ ] **Step 7: Ejecutar el test y verificar que falla**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter ApiKeyAuthenticatorTests`
Expected: FAIL — `ApiKeyAuthenticator` no existe.

- [ ] **Step 8: Implementar `ApiKeyAuthenticator`**

```csharp
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;

namespace PortalSaas.Core.Seguridad;

public class ApiKeyAuthenticator : IApiKeyAuthenticator
{
    private readonly PortalSaasDbContext _contexto;

    public ApiKeyAuthenticator(PortalSaasDbContext contexto)
    {
        _contexto = contexto;
    }

    public async Task<ApiKeyAuthenticationResult> AuthenticateAsync(string rawApiKey, CancellationToken cancellationToken)
    {
        var hash = ApiKeyGenerator.Hash(rawApiKey);

        var credencial = await _contexto.ApiClientCredentials
            .FirstOrDefaultAsync(c => c.ApiKeyHash == hash && c.Activo, cancellationToken);

        if (credencial is null)
        {
            return ApiKeyAuthenticationResult.Failure();
        }

        var company = await _contexto.Companies
            .FirstOrDefaultAsync(c => c.Id == credencial.CompanyId, cancellationToken);

        if (company is null)
        {
            return ApiKeyAuthenticationResult.Failure();
        }

        credencial.LastUsedAt = DateTimeOffset.UtcNow;
        await _contexto.SaveChangesAsync(cancellationToken);

        return ApiKeyAuthenticationResult.Ok(
            company.Id,
            company.Code,
            company.DatabaseName,
            company.ServiceLayerUrl,
            company.Country);
    }
}
```

- [ ] **Step 9: Ejecutar el test y verificar que pasa**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --filter ApiKeyAuthenticatorTests`
Expected: PASS, 4/4.

- [ ] **Step 10: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan (los anteriores + los 8 nuevos de este task).

- [ ] **Step 11: Commit**

```bash
git add src/PortalSaas.Core/Seguridad/ApiKeyGenerator.cs src/PortalSaas.Core/Seguridad/ApiKeyAuthenticator.cs src/PortalSaas.Abstractions/Contratos/IApiKeyAuthenticator.cs tests/PortalSaas.Core.Tests/Seguridad/
git commit -m "feat: agregar ApiKeyGenerator y ApiKeyAuthenticator"
```

---

### Task 3: `ApiKeyAuthenticationHandler` + registro del esquema en `Program.cs`

**Files:**
- Create: `src/PortalSaas.Core/Seguridad/ApiKeyAuthenticationHandler.cs`
- Modify: `src/PortalSaas.Host/Program.cs`

**Interfaces:**
- Consumes: `IApiKeyAuthenticator`, `ApiKeyAuthenticationResult` (Task 2).
- Produces: esquema de autenticación `"ExternalApiKey"` registrado y funcional — consumido por cualquier endpoint futuro marcado `[Authorize(AuthenticationSchemes = "ExternalApiKey")]` (fuera de alcance de este plan; el primer consumidor real es la Ronda A del proyecto de migración de Wms).

- [ ] **Step 1: Implementar `ApiKeyAuthenticationHandler`**

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Seguridad;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>
{
    private const string HeaderName = "X-Api-Key";

    private readonly IApiKeyAuthenticator _authenticator;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyAuthenticator authenticator)
        : base(options, logger, encoder)
    {
        _authenticator = authenticator;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var apiKeyValues) || apiKeyValues.Count == 0)
        {
            return AuthenticateResult.Fail($"Falta el header '{HeaderName}'.");
        }

        var rawApiKey = apiKeyValues[0];
        if (string.IsNullOrWhiteSpace(rawApiKey))
        {
            return AuthenticateResult.Fail($"El header '{HeaderName}' está vacío.");
        }

        var resultado = await _authenticator.AuthenticateAsync(rawApiKey, Context.RequestAborted);
        if (!resultado.Success)
        {
            return AuthenticateResult.Fail("API key inválida o revocada.");
        }

        var claims = new[]
        {
            new Claim("CompanyId", resultado.CompanyId!.Value.ToString()),
            new Claim("CompanyCode", resultado.CompanyCode!),
            new Claim("CompanyDatabase", resultado.CompanyDatabase!),
            new Claim("CompanyServiceLayerUrl", resultado.CompanyServiceLayerUrl!),
            new Claim("CompanyCountry", resultado.CompanyCountry!),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
```

- [ ] **Step 2: Registrar `IApiKeyAuthenticator` y el esquema en `Program.cs`**

Ubicar el bloque `AddAuthentication` (`Program.cs:232-271`) y encadenar el nuevo esquema después de `.AddCookie("PlatformAdmin", ...)`:

```csharp
.AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>("ExternalApiKey", options => { });
```

Agregar el registro DI del servicio, junto a otros registros `AddScoped` existentes (ej. cerca de `ISecretoCifradoService`):

```csharp
builder.Services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();
```

Agregar los `using` correspondientes al inicio de `Program.cs` si no están ya presentes (`PortalSaas.Core.Seguridad` probablemente ya tiene un `using` por `CurrentCompanyAccessor`/`SecretoCifradoService` — verificar antes de duplicar).

- [ ] **Step 3: Compilar la solución completa**

Run: `dotnet build PortalSaas.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: todos los tests pasan, sin regresiones (este task no agrega tests nuevos — un `AuthenticationHandler` de ASP.NET Core se prueba mejor de forma indirecta a través de la lógica ya cubierta en `ApiKeyAuthenticator`; el handler en sí es un adaptador delgado sin lógica propia de negocio).

- [ ] **Step 5: Commit**

```bash
git add src/PortalSaas.Core/Seguridad/ApiKeyAuthenticationHandler.cs src/PortalSaas.Host/Program.cs
git commit -m "feat: registrar esquema de autenticación ExternalApiKey"
```

---

### Task 4: UI admin — generar/revocar API keys por compañía

**Files:**
- Create: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ApiKeys/Index.cshtml.cs`
- Create: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ApiKeys/Index.cshtml`
- Modify: `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml`

**Interfaces:**
- Consumes: `ApiClientCredential` (Task 1); `ApiKeyGenerator` (Task 2, para generar la key cruda al crear); `PortalSaasDbContext.ApiClientCredentials`.
- Produces: página en ruta `/Admin/Organizations/Companies/ApiKeys/Index?companyId={guid}` — hoja final, nada la consume después.

- [ ] **Step 1: Crear el PageModel**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Core.Seguridad;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ApiKeys;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public IndexModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Company Company { get; private set; } = null!;
    public List<ApiClientCredential> Credenciales { get; private set; } = [];

    [TempData]
    public string? RawKeyGenerada { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid companyId, CancellationToken ct)
    {
        var company = await _db.Companies.FindAsync([companyId], ct);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        Credenciales = await _db.ApiClientCredentials
            .Where(c => c.CompanyId == companyId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(Guid companyId, string nombre, CancellationToken ct)
    {
        var company = await _db.Companies.FindAsync([companyId], ct);
        if (company is null)
        {
            return NotFound();
        }

        var rawKey = ApiKeyGenerator.GenerateRawKey();
        _db.ApiClientCredentials.Add(new ApiClientCredential
        {
            CompanyId = companyId,
            Nombre = nombre,
            ApiKeyHash = ApiKeyGenerator.Hash(rawKey),
            Activo = true,
        });
        await _db.SaveChangesAsync(ct);

        RawKeyGenerada = rawKey;
        return RedirectToPage(new { companyId });
    }

    public async Task<IActionResult> OnPostRevokeAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        var credencial = await _db.ApiClientCredentials
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId, ct);
        if (credencial is null)
        {
            return NotFound();
        }

        credencial.Activo = false;
        await _db.SaveChangesAsync(ct);

        return RedirectToPage(new { companyId });
    }
}
```

- [ ] **Step 2: Crear la vista Razor**

```cshtml
@page
@model PortalSaas.Host.Pages.Admin.Organizations.Companies.ApiKeys.IndexModel
@{
    ViewData["Title"] = "API Keys";
}

<div class="d-flex justify-content-between align-items-center mb-3">
    <div>
        <h1 class="h3 mb-0">API Keys: @Model.Company.Name</h1>
        <span class="text-muted">Credenciales para llamadas de sistema externo (máquina-a-máquina)</span>
    </div>
</div>

@if (Model.RawKeyGenerada is string rawKey)
{
    <div class="alert alert-warning">
        <strong>Guarda esta key ahora — no se va a volver a mostrar:</strong>
        <pre class="mb-0 mt-2">@rawKey</pre>
    </div>
}

<form method="post" asp-page-handler="Create" asp-route-companyId="@Model.Company.Id" asp-antiforgery="true" class="row g-2 mb-4 align-items-end">
    <div class="col-auto">
        <label class="form-label" for="nombre">Nombre</label>
        <input type="text" id="nombre" name="nombre" class="form-control" placeholder="ej. Oracle WMS - Ingesta XML" required />
    </div>
    <div class="col-auto">
        <button type="submit" class="btn btn-primary">Generar API key</button>
    </div>
</form>

@if (Model.Credenciales.Count == 0)
{
    <p class="text-muted">No hay API keys generadas para esta compañía.</p>
}
else
{
    <div class="document-list-table-wrapper">
        <table class="table document-list-table mb-0">
            <thead>
                <tr>
                    <th>Nombre</th>
                    <th>Creada</th>
                    <th>Último uso</th>
                    <th>Estado</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>
                @foreach (var credencial in Model.Credenciales)
                {
                    <tr>
                        <td>@credencial.Nombre</td>
                        <td>@credencial.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")</td>
                        <td>@(credencial.LastUsedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "-")</td>
                        <td>
                            @if (credencial.Activo)
                            {
                                <span class="badge bg-success">Activa</span>
                            }
                            else
                            {
                                <span class="badge bg-secondary">Revocada</span>
                            }
                        </td>
                        <td class="text-end">
                            @if (credencial.Activo)
                            {
                                <form method="post" asp-page-handler="Revoke" asp-route-companyId="@Model.Company.Id" asp-route-id="@credencial.Id" asp-antiforgery="true"
                                      onsubmit="return confirm('¿Revocar esta API key? Cualquier sistema que la use dejará de poder autenticarse.');">
                                    <button type="submit" class="btn btn-sm btn-outline-danger">Revocar</button>
                                </form>
                            }
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    </div>
}
```

- [ ] **Step 3: Agregar el enlace desde `Companies/Index.cshtml`**

Abrir `src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml`, ubicar el enlace existente hacia `ExternalConnections/Index` (patrón `asp-page="/Admin/Organizations/Companies/ExternalConnections/Index" asp-route-companyId="..."`) y agregar uno equivalente hacia `ApiKeys/Index` junto a él, mismo estilo de botón/menú que ya usa esa fila.

- [ ] **Step 4: Compilar**

Run: `dotnet build src/PortalSaas.Host/PortalSaas.Host.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Ejecutar toda la suite de tests**

Run: `dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj`
Expected: mismo conteo que al final de la Tarea 3, todos en PASS (esta tarea no agrega tests nuevos, sigue el mismo patrón sin tests de las demás páginas admin del proyecto).

- [ ] **Step 6: Commit**

```bash
git add src/PortalSaas.Host/Pages/Admin/Organizations/Companies/ApiKeys/ src/PortalSaas.Host/Pages/Admin/Organizations/Companies/Index.cshtml
git commit -m "feat: agregar UI admin de generación/revocación de API keys por compañía"
```

---

## Self-Review

**1. Cobertura del spec:** Modelo de datos (`ApiClientCredential`) → Task 1. Autenticación + reutilización de `ICurrentCompanyAccessor` sin modificarlo → Task 3 (el `ClaimsPrincipal` usa exactamente los mismos tipos de claim que `CurrentCompanyAccessor.GetClaim` ya lee). UI admin (crear con reveal-once, revocar) → Task 4. Criterio de éxito del spec ("una request con X-Api-Key válido resuelve `ICurrentCompanyAccessor.CompanyId` sin tocar `CurrentCompanyAccessor`") queda satisfecho estructuralmente por Task 3, aunque verificarlo end-to-end contra un endpoint real queda para cuando exista un consumidor (Ronda A, fuera de este plan — documentado como tal en "Fuera de alcance" del spec).

**2. Placeholder scan:** sin "TBD"/"TODO". Ningún paso describe qué hacer sin mostrar el código exacto.

**3. Consistencia de tipos:** `IApiKeyAuthenticator.AuthenticateAsync` y `ApiKeyAuthenticationResult` (Task 2) se usan con la misma firma en `ApiKeyAuthenticationHandler` (Task 3). `ApiKeyGenerator.GenerateRawKey()`/`Hash(string)` usados idénticamente en `ApiKeyAuthenticator` (Task 2) y en la UI admin (Task 4). Nombres de claim (`CompanyId`, `CompanyCode`, `CompanyDatabase`, `CompanyServiceLayerUrl`, `CompanyCountry`) idénticos entre `ApiKeyAuthenticationHandler` (Task 3) y `CurrentCompanyAccessor.GetClaim` ya existente — verificado contra el código real durante la investigación previa al plan, no supuesto.

**4. Riesgo señalado explícitamente:** Task 3 no agrega tests nuevos para el `AuthenticationHandler` en sí — es una decisión de diseño explícita (adaptador delgado sin lógica propia; la lógica de negocio real ya está cubierta por los tests de `ApiKeyAuthenticator` en Task 2), no un vacío accidental. Si una revisión futura quiere verificación end-to-end del pipeline HTTP completo, requiere un endpoint de prueba real — eso llega naturalmente con la Ronda A (primer consumidor del esquema), fuera de este plan.
