# Auditoría de Inventario — lógica de negocio pendiente Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Completar los puntos 2-7 de `PENDIENTE.md` del plugin `Modulo.AuditoriaInventario`: hash de contraseña con sal, motor de diferencias (capturado vs. congelado), importador de Excel de congelados, mapeo de códigos SAP en la aprobación de ajustes, y las pantallas de administración de catálogo (Sucursales/Sectores/Capturadores) que hoy no existen.

**Architecture:** Todo el trabajo vive dentro de `Modulo.AuditoriaInventario` (plugin autocontenido, solo referencia `PortalSaas.Abstractions`). Se agregan tres servicios de dominio nuevos (`PasswordHasher`, `IDiferenciaEngine`/`DiferenciaEngine`, `IAjusteService`/`AjusteService`, `CongeladoExcelParser`) registrados vía DI en `AuditoriaInventarioModule.RegisterServices` (resolviendo el TODO ya escrito ahí), y tres páginas Razor nuevas de administración de catálogo siguiendo el patrón de drawer ya usado en `Modulo.SellOut/Pages/Sucursales`.

**Tech Stack:** ASP.NET Core Razor Pages (.NET 8), EF Core motor dual (Postgres/SqlServer) vía `IExternalDatabaseConnectionService`, `ExcelDataReader`/`ExcelDataReader.DataSet` 3.7.0 para leer `.xlsx` (mismo paquete que usa `PortalSaas.Core` para `Modulo.ImportacionGenerica`), xUnit + EF Core InMemory para tests.

**Spec:** `Portal SaaS - Plugins/Modulo.AuditoriaInventario/PENDIENTE.md` (puntos 2-7, "Alcance confirmado por el dueño del proyecto") y `docs/superpowers/plans/2026-09-14-auditoria-inventario-conteo-fisico.md` (contexto arquitectónico completo).

## Global Constraints

- `ModuleCode = "AuditoriaInventario"` -- nunca cambiar.
- El plugin referencia SOLO `PortalSaas.Abstractions` -- nunca `PortalSaas.Core`, nunca `PortalSaas.Host`, nunca otro plugin (`Modulo.Inventario`/`Modulo.ImportacionGenerica` explícitamente descartados por el dueño del proyecto).
- Motor dual: ninguna columna nueva usa `HasColumnType` con un tipo específico de un motor -- usar `HasPrecision` para decimales, tipos EF genéricos para el resto.
- Cada cambio de modelo requiere migración generada para **los dos** proyectos de migraciones (`Modulo.AuditoriaInventario.Migrations.Postgres` y `...SqlServer`), nunca uno solo. Comando, ejecutado desde `Portal SaaS - Plugins/Modulo.AuditoriaInventario/src/`:
  ```
  dotnet tool run dotnet-ef migrations add <Nombre> --project Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj --startup-project Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj --context AuditoriaInventarioDbContext

  dotnet tool run dotnet-ef migrations add <Nombre> --project Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj --startup-project Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj --context AuditoriaInventarioDbContext
  ```
  Esto NO requiere una base de datos real corriendo (el `DesignTimeDbContextFactory` de cada proyecto solo construye el modelo, no se conecta). Aplicar las migraciones contra Postgres/SQL Server real (`dotnet ef database update`) queda **fuera del alcance de este plan** -- requiere levantar Docker Desktop (`docker compose up -d` en `Portal SaaS - Core`, el daemon no está corriendo en este entorno) y es un paso de infraestructura, no de código; se hace en una sesión aparte con el dueño del proyecto presente.
- Todo `MenuItemDefinition` nuevo lleva `Icon` (`bi-*`) -- regla dura de la plataforma, sin excepción.
- Nombres de tabla/columna: inglés, plural, snake_case (`docs/01-CONVENCION-NOMBRES-BD.md`), ya seguido por el resto del contexto.
- Páginas de administración de catálogo puras (sin lógica de negocio calculada, solo alta/edición/baja) **no llevan test xUnit dedicado** -- mismo criterio ya documentado y usado en `Modulo.Rendiciones/PENDIENTE.md` para `Plans`/`Subscriptions` ("es CRUD sin lógica de negocio computada"). Se verifican con `dotnet build` + revisión manual del código. La lógica de negocio real (hash, motor de diferencias, mapeo SAP, parser de Excel) SÍ lleva test xUnit con EF Core InMemory, mismo patrón que `AuditoriaInventarioDbContextTests.cs`.
- Comandos de build/test se ejecutan desde `Portal SaaS - Plugins/Modulo.AuditoriaInventario/src/` contra los `.csproj` puntuales (no existe un `.sln` en este repo de plugin):
  ```
  dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj
  dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj
  dotnet build Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj
  dotnet build Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj
  ```

---

### Task 1: Hash de contraseña con sal para CaptureUser

**Files:**
- Modify: `Modulo.AuditoriaInventario/Models/CaptureUser.cs`
- Modify: `Modulo.AuditoriaInventario/Data/AuditoriaInventarioDbContext.cs:87-98` (bloque `CaptureUser`)
- Create: `Modulo.AuditoriaInventario/Servicios/PasswordHasher.cs`
- Modify: `Modulo.AuditoriaInventario/Servicios/AuditoriaInventarioApiService.cs`
- Modify: `Modulo.AuditoriaInventario.Tests/AuditoriaInventarioDbContextTests.cs:29,83`
- Create: `Modulo.AuditoriaInventario.Tests/PasswordHasherTests.cs`
- Create: migraciones `AddCaptureUserPasswordSalt` (Postgres + SqlServer)

**Interfaces:**
- Produces: `PasswordHasher.Hash(string password) -> (string Hash, string Salt)`, `PasswordHasher.Verify(string password, string expectedHash, string saltBase64) -> bool`, ambos en `Modulo.AuditoriaInventario.Servicios`. Usados por Task 7 (alta/edición de `CaptureUser` desde el portal).

- [ ] **Step 1: Escribir el test que falla**

Crear `Modulo.AuditoriaInventario.Tests/PasswordHasherTests.cs`:

```csharp
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verify_ConLaMismaContrasena_DevuelveTrue()
    {
        var (hash, salt) = PasswordHasher.Hash("Clave-Segura-123");

        Assert.True(PasswordHasher.Verify("Clave-Segura-123", hash, salt));
    }

    [Fact]
    public void Verify_ConContrasenaDistinta_DevuelveFalse()
    {
        var (hash, salt) = PasswordHasher.Hash("Clave-Segura-123");

        Assert.False(PasswordHasher.Verify("otra-clave", hash, salt));
    }

    [Fact]
    public void Hash_ConLaMismaContrasenaDosVeces_GeneraSalesYHashesDistintos()
    {
        var (hash1, salt1) = PasswordHasher.Hash("Clave-Segura-123");
        var (hash2, salt2) = PasswordHasher.Hash("Clave-Segura-123");

        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }
}
```

- [ ] **Step 2: Confirmar que no compila (PasswordHasher no existe todavía)**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: error de compilación `The type or namespace name 'PasswordHasher' does not exist`.

- [ ] **Step 3: Implementar `PasswordHasher`**

Crear `Modulo.AuditoriaInventario/Servicios/PasswordHasher.cs`:

```csharp
using System.Security.Cryptography;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// PBKDF2-SHA256 con sal aleatoria por usuario -- mismo algoritmo que
/// PortalSaas.Core.Seguridad.PasswordHasher (100k iteraciones, sal de 128 bits,
/// hash de 256 bits), portado acá porque un plugin nunca referencia
/// PortalSaas.Core (solo PortalSaas.Abstractions, ver Modulo.AuditoriaInventario.csproj).
/// Reemplaza el placeholder SHA-256 sin sal que tenía AuditoriaInventarioApiService.
/// </summary>
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    public static (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(string password, string expectedHash, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(expectedHash));
    }
}
```

- [ ] **Step 4: Correr el test, confirmar que pasa**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj --filter PasswordHasherTests`
Expected: 3/3 PASS.

- [ ] **Step 5: Agregar `PasswordSalt` al modelo `CaptureUser`**

En `Modulo.AuditoriaInventario/Models/CaptureUser.cs`, agregar después de `PasswordHash`:

```csharp
    public required string PasswordHash { get; set; }

    public required string PasswordSalt { get; set; }
```

- [ ] **Step 6: Mapear la columna nueva en el DbContext**

En `Modulo.AuditoriaInventario/Data/AuditoriaInventarioDbContext.cs`, dentro del bloque `modelBuilder.Entity<CaptureUser>` (línea ~94), agregar después de la línea de `PasswordHash`:

```csharp
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.PasswordSalt).HasColumnName("password_salt").HasMaxLength(200).IsRequired();
```

- [ ] **Step 7: Actualizar los dos tests existentes que construyen un `CaptureUser`**

En `Modulo.AuditoriaInventario.Tests/AuditoriaInventarioDbContextTests.cs`, líneas 29 y 83, cambiar:

```csharp
        var user = new CaptureUser { CompanyId = companyId, Username = "auditor1", PasswordHash = "x" };
```

por:

```csharp
        var user = new CaptureUser { CompanyId = companyId, Username = "auditor1", PasswordHash = "x", PasswordSalt = "x" };
```

(en ambas ocurrencias -- son idénticas, `replace_all`).

- [ ] **Step 8: Usar `PasswordHasher` en `AuditoriaInventarioApiService`, quitar el placeholder**

En `Modulo.AuditoriaInventario/Servicios/AuditoriaInventarioApiService.cs`:

1. Quitar `using System.Text;` (línea 2, ya no se usa).
2. Reemplazar el cuerpo de `LoginAsync` (líneas 53-76):

```csharp
    public async Task<CaptureLoginResult?> LoginAsync(Guid companyId, string username, string password, CancellationToken ct = default)
    {
        await using var db = await CreateDbContextAsync(companyId, ct);

        var user = await db.CaptureUsers.FirstOrDefaultAsync(u => u.Username == username && u.IsActive, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash, user.PasswordSalt))
        {
            return null;
        }

        var token = new CaptureAuthToken
        {
            CompanyId = companyId,
            CaptureUserId = user.Id,
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(12),
        };

        db.CaptureAuthTokens.Add(token);
        await db.SaveChangesAsync(ct);

        return new CaptureLoginResult(token.Token, token.ExpiresAt, user.FullName ?? user.Username);
    }
```

3. Borrar el método `HashPassword` completo (líneas 223-225, el placeholder SHA-256 con su comentario TODO).

- [ ] **Step 9: Compilar todo el conjunto**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj`
Expected: Build succeeded, 0 errores.

- [ ] **Step 10: Generar las migraciones (los dos motores)**

Desde `Portal SaaS - Plugins/Modulo.AuditoriaInventario/src/`:

```
dotnet tool run dotnet-ef migrations add AddCaptureUserPasswordSalt --project Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj --startup-project Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj --context AuditoriaInventarioDbContext

dotnet tool run dotnet-ef migrations add AddCaptureUserPasswordSalt --project Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj --startup-project Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj --context AuditoriaInventarioDbContext
```

Expected: cada comando genera 3 archivos nuevos en su carpeta `Migrations/` (`<timestamp>_AddCaptureUserPasswordSalt.cs`, `.Designer.cs`, y actualiza `AuditoriaInventarioDbContextModelSnapshot.cs`). Revisar que la migración generada sea un `AddColumn` simple sobre `capture_users`, sin `AlterColumn` en otras tablas (si aparece algo más, algo quedó mal referenciado -- ver el bug documentado en `Modulo.Rendiciones/PENDIENTE.md` sobre mezclar ensamblados de motores).

- [ ] **Step 11: Correr toda la suite de tests**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: todos los tests (los 3 existentes + los 3 nuevos de `PasswordHasher`) en PASS.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): hash de contraseña con sal (PBKDF2) para CaptureUser"
```

---

### Task 2: Motor de diferencias (InventoryDifference)

**Files:**
- Create: `Modulo.AuditoriaInventario/Servicios/IDiferenciaEngine.cs`
- Create: `Modulo.AuditoriaInventario/Servicios/DiferenciaEngine.cs`
- Modify: `Modulo.AuditoriaInventario/Servicios/AuditoriaInventarioApiService.cs` (constructor + `UpsertSesionAsync`)
- Modify: `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs` (`RegisterServices`)
- Modify: `Modulo.AuditoriaInventario/Pages/Diferencias/Index.cshtml.cs`
- Create: `Modulo.AuditoriaInventario.Tests/DiferenciaEngineTests.cs`

**Interfaces:**
- Consumes: `AuditoriaInventarioDbContext` (existente), modelos `InventorySession`, `FrozenInventorySnapshot`, `FrozenInventoryLine`, `InventoryCapture`, `InventoryDifference` (todos existentes, ver Task 1 para `CaptureUser.PasswordSalt` si el test crea usuarios).
- Produces: `IDiferenciaEngine.CalcularDiferenciasAsync(AuditoriaInventarioDbContext db, Guid sessionId, CancellationToken ct = default) -> Task<int>` (cantidad de filas de diferencia creadas). Registrado como `Scoped` en DI. Usado por Task 4 indirectamente (las diferencias que consume `AjusteService` las produce este motor).

- [ ] **Step 1: Escribir el test que falla**

Crear `Modulo.AuditoriaInventario.Tests/DiferenciaEngineTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class DiferenciaEngineTests
{
    private static AuditoriaInventarioDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task CalcularDiferenciasAsync_SobranteYFaltante_CalculaCantidadYMontoCorrectos()
    {
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro" };
        db.Branches.Add(branch);
        var sector = new InventorySector { CompanyId = companyId, Name = "Bodega" };
        db.InventorySectors.Add(sector);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-001",
            ResponsibleUserId = user.Id,
        });

        var snapshot = new FrozenInventorySnapshot
        {
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-001",
            LoadedByUserId = Guid.NewGuid(),
            FileName = "congelado.xlsx",
        };
        db.FrozenInventorySnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        db.FrozenInventoryLines.Add(new FrozenInventoryLine { SnapshotId = snapshot.Id, Barcode = "AAA", Quantity = 10, UnitCost = 100m });
        db.FrozenInventoryLines.Add(new FrozenInventoryLine { SnapshotId = snapshot.Id, Barcode = "BBB", Quantity = 5, UnitCost = 50m });

        db.InventoryCaptures.Add(new InventoryCapture { Id = Guid.NewGuid(), SessionId = sessionId, SectorId = sector.Id, Barcode = "AAA", Quantity = 12, CapturedByUserId = user.Id });
        // "BBB" nunca se capturó -- debe salir como faltante total.
        await db.SaveChangesAsync();

        var engine = new DiferenciaEngine();
        var creadas = await engine.CalcularDiferenciasAsync(db, sessionId);

        Assert.Equal(2, creadas);

        var diffAAA = await db.InventoryDifferences.FirstAsync(d => d.Barcode == "AAA");
        Assert.Equal(12, diffAAA.CapturedQuantity);
        Assert.Equal(10, diffAAA.FrozenQuantity);
        Assert.Equal(2, diffAAA.QuantityDiff);
        Assert.Equal(200m, diffAAA.AmountDiff);
        Assert.Equal(sector.Id, diffAAA.SectorId);

        var diffBBB = await db.InventoryDifferences.FirstAsync(d => d.Barcode == "BBB");
        Assert.Equal(0, diffBBB.CapturedQuantity);
        Assert.Equal(5, diffBBB.FrozenQuantity);
        Assert.Equal(-5, diffBBB.QuantityDiff);
        Assert.Equal(-250m, diffBBB.AmountDiff);
        Assert.Null(diffBBB.SectorId);
    }

    [Fact]
    public async Task CalcularDiferenciasAsync_SinCongeladoCargado_NoCreaNadaYDevuelveCero()
    {
        using var db = CrearContexto();
        var companyId = Guid.NewGuid();
        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro" };
        db.Branches.Add(branch);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession
        {
            Id = sessionId,
            CompanyId = companyId,
            BranchId = branch.Id,
            InventoryNumber = "INV-SIN-CONGELADO",
            ResponsibleUserId = user.Id,
        });
        await db.SaveChangesAsync();

        var engine = new DiferenciaEngine();
        var creadas = await engine.CalcularDiferenciasAsync(db, sessionId);

        Assert.Equal(0, creadas);
        Assert.Empty(await db.InventoryDifferences.ToListAsync());
    }
}
```

- [ ] **Step 2: Confirmar que no compila**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: error de compilación, `DiferenciaEngine` no existe.

- [ ] **Step 3: Crear la interfaz**

Crear `Modulo.AuditoriaInventario/Servicios/IDiferenciaEngine.cs`:

```csharp
using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Motor de diferencias: cruza InventoryCapture (agregado por sesión+sector+barcode)
/// contra FrozenInventoryLine del snapshot congelado que corresponde a la misma
/// sucursal + Nro. de Inventario de la sesión, y materializa el resultado en
/// InventoryDifference. Se dispara al cerrar una sesión (ver
/// AuditoriaInventarioApiService.UpsertSesionAsync). Recibe el DbContext ya abierto
/// en vez de crear el suyo -- funciona igual desde la API (sin sesión de portal) que
/// desde una futura pantalla del portal.
/// </summary>
public interface IDiferenciaEngine
{
    Task<int> CalcularDiferenciasAsync(AuditoriaInventarioDbContext db, Guid sessionId, CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el motor**

Crear `Modulo.AuditoriaInventario/Servicios/DiferenciaEngine.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Servicios;

public sealed class DiferenciaEngine : IDiferenciaEngine
{
    public async Task<int> CalcularDiferenciasAsync(AuditoriaInventarioDbContext db, Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"La sesión '{sessionId}' no existe.");

        var snapshot = await db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == session.CompanyId && s.BranchId == session.BranchId && s.InventoryNumber == session.InventoryNumber)
            .OrderByDescending(s => s.LoadedAt)
            .FirstOrDefaultAsync(ct);

        if (snapshot is null)
        {
            // Sin congelado cargado todavía para esa sucursal/número de inventario --
            // nada contra qué comparar. La sesión queda cerrada igual.
            return 0;
        }

        var lineasCongeladas = await db.FrozenInventoryLines
            .Where(l => l.SnapshotId == snapshot.Id)
            .ToListAsync(ct);

        var congeladoPorBarcode = lineasCongeladas
            .GroupBy(l => l.Barcode)
            .ToDictionary(
                g => g.Key,
                g => (Quantity: g.Sum(x => x.Quantity), UnitCost: g.Select(x => x.UnitCost).FirstOrDefault(c => c.HasValue) ?? 0m));

        var capturasPorSectorYBarcode = await db.InventoryCaptures
            .Where(c => c.SessionId == sessionId)
            .GroupBy(c => new { c.SectorId, c.Barcode })
            .Select(g => new { g.Key.SectorId, g.Key.Barcode, Quantity = g.Sum(x => x.Quantity) })
            .ToListAsync(ct);

        var existentes = await db.InventoryDifferences.Where(d => d.SessionId == sessionId).ToListAsync(ct);
        db.InventoryDifferences.RemoveRange(existentes);

        var ahora = DateTimeOffset.UtcNow;
        var nuevas = new List<InventoryDifference>();
        var barcodesCapturados = new HashSet<string>();

        foreach (var g in capturasPorSectorYBarcode)
        {
            barcodesCapturados.Add(g.Barcode);
            congeladoPorBarcode.TryGetValue(g.Barcode, out var congelado);
            var quantityDiff = g.Quantity - congelado.Quantity;

            nuevas.Add(new InventoryDifference
            {
                SessionId = sessionId,
                SnapshotId = snapshot.Id,
                SectorId = g.SectorId,
                Barcode = g.Barcode,
                CapturedQuantity = g.Quantity,
                FrozenQuantity = congelado.Quantity,
                QuantityDiff = quantityDiff,
                AmountDiff = quantityDiff * congelado.UnitCost,
                CalculatedAt = ahora,
            });
        }

        // Códigos que estaban en el congelado pero no se capturaron en ninguna sesión --
        // faltante total, sin sector asociado (no se escaneó en ningún lado).
        foreach (var (barcode, congelado) in congeladoPorBarcode)
        {
            if (barcodesCapturados.Contains(barcode))
            {
                continue;
            }

            var quantityDiff = -congelado.Quantity;
            nuevas.Add(new InventoryDifference
            {
                SessionId = sessionId,
                SnapshotId = snapshot.Id,
                SectorId = null,
                Barcode = barcode,
                CapturedQuantity = 0,
                FrozenQuantity = congelado.Quantity,
                QuantityDiff = quantityDiff,
                AmountDiff = quantityDiff * congelado.UnitCost,
                CalculatedAt = ahora,
            });
        }

        db.InventoryDifferences.AddRange(nuevas);
        await db.SaveChangesAsync(ct);
        return nuevas.Count;
    }
}
```

- [ ] **Step 5: Correr los tests, confirmar que pasan**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj --filter DiferenciaEngineTests`
Expected: 2/2 PASS.

- [ ] **Step 6: Registrar el motor en DI**

En `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`, reemplazar el bloque final de `RegisterServices` (líneas 130-131):

```csharp
        // TODO: registrar acá los servicios de dominio a medida que se implementen
        // (IDiferenciaEngine, IAjusteService) -- ver PENDIENTE.md.
```

por:

```csharp
        services.AddScoped<IDiferenciaEngine, DiferenciaEngine>();
```

(la segunda línea, `IAjusteService`, se agrega en la Task 4 -- dejar el `using Modulo.AuditoriaInventario.Servicios;` ya existente, no hace falta agregarlo de nuevo).

- [ ] **Step 7: Disparar el motor al cerrar una sesión**

En `Modulo.AuditoriaInventario/Servicios/AuditoriaInventarioApiService.cs`:

1. Agregar el campo y el parámetro de constructor (líneas 26-31):

```csharp
    private readonly IExternalDatabaseConnectionService _externalDb;
    private readonly IDiferenciaEngine _diferenciaEngine;

    public AuditoriaInventarioApiService(IExternalDatabaseConnectionService externalDb, IDiferenciaEngine diferenciaEngine)
    {
        _externalDb = externalDb;
        _diferenciaEngine = diferenciaEngine;
    }
```

2. En `UpsertSesionAsync`, reemplazar el bloque `else` que actualiza una sesión existente (líneas 169-173):

```csharp
        else
        {
            existing.Status = request.Status;
            existing.ClosedAt = request.Status == "CLOSED" ? DateTimeOffset.UtcNow : existing.ClosedAt;
        }
```

por:

```csharp
        else
        {
            var estabaCerrada = existing.Status == "CLOSED";
            existing.Status = request.Status;
            existing.ClosedAt = request.Status == "CLOSED" ? DateTimeOffset.UtcNow : existing.ClosedAt;

            if (request.Status == "CLOSED" && !estabaCerrada)
            {
                await _diferenciaEngine.CalcularDiferenciasAsync(db, existing.Id, ct);
            }
        }
```

- [ ] **Step 8: Filtrar `Diferencias/Index` por compañía actual**

Reemplazar el contenido completo de `Modulo.AuditoriaInventario/Pages/Diferencias/Index.cshtml.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Diferencias;

/// <summary>
/// Listado de diferencias calculadas (capturado vs. congelado), materializadas por
/// DiferenciaEngine al cerrar una sesión.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    public IReadOnlyList<DiferenciaRowDto> Diferencias { get; private set; } = Array.Empty<DiferenciaRowDto>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // InventoryDifference no lleva company_id directo -- llega vía InventorySession.
        Diferencias = await (
            from d in _db.InventoryDifferences
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where s.CompanyId == _currentCompany.CompanyId
            orderby d.CalculatedAt descending
            select new DiferenciaRowDto(
                d.Id, d.SessionId, d.Barcode, d.CapturedQuantity, d.FrozenQuantity,
                d.QuantityDiff, d.AmountDiff, d.CalculatedAt))
            .Take(200)
            .ToListAsync(ct);
    }

    public sealed record DiferenciaRowDto(
        long Id,
        Guid SessionId,
        string Barcode,
        int CapturedQuantity,
        int FrozenQuantity,
        int QuantityDiff,
        decimal AmountDiff,
        DateTimeOffset CalculatedAt);
}
```

- [ ] **Step 9: Compilar y correr toda la suite**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj && dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: build OK, todos los tests en PASS.

Nota para el revisor: el disparo del motor desde `UpsertSesionAsync` (Step 7) no tiene test dedicado -- requeriría mockear `IExternalDatabaseConnectionService` para instanciar `AuditoriaInventarioApiService`, y ese servicio no tiene ningún test hoy (cero tests preexistentes sobre `AuditoriaInventarioApiService`). El motor en sí (`DiferenciaEngine`) está cubierto end-to-end por los tests del Step 1. Aceptable para este alcance; si se quiere cerrar esa brecha, es una tarea aparte (agregar tests de integración de `AuditoriaInventarioApiService` en general, no específico de esta feature).

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): motor de diferencias capturado vs. congelado al cerrar sesión"
```

---

### Task 3: Importador de Excel para congelados

**Files:**
- Modify: `Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj` (agregar `ExcelDataReader`/`ExcelDataReader.DataSet`)
- Modify: `Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj` (agregar `ClosedXML`, solo para generar el `.xlsx` de prueba)
- Create: `Modulo.AuditoriaInventario/Servicios/CongeladoExcelParser.cs`
- Create: `Modulo.AuditoriaInventario.Tests/CongeladoExcelParserTests.cs`
- Modify: `Modulo.AuditoriaInventario/Pages/Congelados/Index.cshtml.cs`
- Modify: `Modulo.AuditoriaInventario/Pages/Congelados/Index.cshtml`

**Interfaces:**
- Produces: `CongeladoExcelParser.Parse(Stream excelStream) -> CongeladoParseResult` con `CongeladoParseResult(IReadOnlyList<CongeladoFilaParseada> Filas, IReadOnlyList<CongeladoFilaError> Errores)`, `CongeladoFilaParseada(string Barcode, string? ProductCode, int Quantity, decimal? UnitCost)`, `CongeladoFilaError(int NumeroFila, string Mensaje)`. Todo en `Modulo.AuditoriaInventario.Servicios`.
- **Decisión de formato (ruling):** el archivo del punto de venta no tiene un contrato de columnas documentado en este repo. Se sigue el mismo criterio que `GenericImportService` de `PortalSaas.Core` (mapeo por **posición de columna**, no por nombre de encabezado, porque el archivo lo genera un sistema externo con formato fijo): fila 1 = encabezado (se ignora), columna A = código de barra, columna B = cantidad, columna C = costo unitario (opcional). Si el archivo real del punto de venta usa otro orden, ajustar los índices en `CongeladoExcelParser` -- están en un solo lugar.

- [ ] **Step 1: Agregar las dependencias**

En `Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj`, agregar dentro del `ItemGroup` de paquetes (junto a `Microsoft.EntityFrameworkCore.SqlServer`/`Npgsql...`):

```xml
    <PackageReference Include="ExcelDataReader" Version="3.7.0" />
    <PackageReference Include="ExcelDataReader.DataSet" Version="3.7.0" />
```

En `Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`, agregar al `ItemGroup` de paquetes:

```xml
    <PackageReference Include="ClosedXML" Version="0.104.1" />
```

- [ ] **Step 2: Escribir los tests que fallan**

Crear `Modulo.AuditoriaInventario.Tests/CongeladoExcelParserTests.cs`:

```csharp
using ClosedXML.Excel;
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class CongeladoExcelParserTests
{
    private static MemoryStream CrearWorkbook(IEnumerable<(string? Barcode, string? Cantidad, string? Costo)> filas)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Congelado");
        ws.Cell(1, 1).Value = "Codigo de Barra";
        ws.Cell(1, 2).Value = "Cantidad";
        ws.Cell(1, 3).Value = "Costo Unitario";

        var fila = 2;
        foreach (var (barcode, cantidad, costo) in filas)
        {
            ws.Cell(fila, 1).Value = barcode ?? string.Empty;
            ws.Cell(fila, 2).Value = cantidad ?? string.Empty;
            ws.Cell(fila, 3).Value = costo ?? string.Empty;
            fila++;
        }

        var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void Parse_FilaValida_DevuelveUnaFilaSinErrores()
    {
        using var stream = CrearWorkbook(new[] { ("7801234567890", "10", "1500.50") });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Empty(resultado.Errores);
        Assert.Single(resultado.Filas);
        Assert.Equal("7801234567890", resultado.Filas[0].Barcode);
        Assert.Equal(10, resultado.Filas[0].Quantity);
        Assert.Equal(1500.50m, resultado.Filas[0].UnitCost);
    }

    [Fact]
    public void Parse_CantidadNoNumerica_AcumulaErrorYSigueConElRestoDeLasFilas()
    {
        using var stream = CrearWorkbook(new[]
        {
            ("7801234567890", "no-es-numero", (string?)null),
            ("7809999999999", "5", (string?)null),
        });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Single(resultado.Errores);
        Assert.Contains("Cantidad inválida", resultado.Errores[0].Mensaje);
        Assert.Single(resultado.Filas);
        Assert.Equal("7809999999999", resultado.Filas[0].Barcode);
    }

    [Fact]
    public void Parse_FilaSinBarcode_AcumulaError()
    {
        using var stream = CrearWorkbook(new[] { ((string?)null, "10", (string?)null) });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Single(resultado.Errores);
        Assert.Empty(resultado.Filas);
    }

    [Fact]
    public void Parse_FilaCompletamenteVacia_SeIgnoraSinError()
    {
        using var stream = CrearWorkbook(new[]
        {
            ("7801234567890", "10", (string?)null),
            ((string?)null, (string?)null, (string?)null),
        });

        var resultado = CongeladoExcelParser.Parse(stream);

        Assert.Empty(resultado.Errores);
        Assert.Single(resultado.Filas);
    }
}
```

- [ ] **Step 3: Confirmar que no compila**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: error de compilación, `CongeladoExcelParser` no existe.

- [ ] **Step 4: Implementar el parser**

Crear `Modulo.AuditoriaInventario/Servicios/CongeladoExcelParser.cs`:

```csharp
using System.Data;
using System.Globalization;
using ExcelDataReader;

namespace Modulo.AuditoriaInventario.Servicios;

public sealed record CongeladoFilaParseada(string Barcode, string? ProductCode, int Quantity, decimal? UnitCost);

public sealed record CongeladoFilaError(int NumeroFila, string Mensaje);

public sealed record CongeladoParseResult(IReadOnlyList<CongeladoFilaParseada> Filas, IReadOnlyList<CongeladoFilaError> Errores);

/// <summary>
/// Parser del Excel de congelado exportado desde el punto de venta. Mapeo por
/// POSICIÓN de columna (no por nombre de encabezado) -- mismo criterio que
/// GenericImportService de PortalSaas.Core, porque el archivo lo genera un sistema
/// externo con formato fijo: A = código de barra, B = cantidad, C = costo unitario
/// (opcional). Fila 1 = encabezado, se ignora. Una fila inválida se acumula como
/// error y NO aborta el resto del archivo (mismo criterio que ImportacionGenerica).
/// </summary>
public static class CongeladoExcelParser
{
    public static CongeladoParseResult Parse(Stream excelStream)
    {
        using var reader = ExcelReaderFactory.CreateReader(excelStream);
        // Nombres completamente calificados para Configuration.* -- ExcelDataReader.DataSet
        // pone ExcelDataSetConfiguration/ExcelDataTableConfiguration en el namespace anidado
        // ExcelDataReader.Configuration, no en ExcelDataReader; evita depender de un using
        // adicional que puede no resolver según la versión exacta del paquete.
        var dataSet = reader.AsDataSet(new ExcelDataReader.Configuration.ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataReader.Configuration.ExcelDataTableConfiguration { UseHeaderRow = false },
        });
        var table = dataSet.Tables[0];

        var filas = new List<CongeladoFilaParseada>();
        var errores = new List<CongeladoFilaError>();

        string? Celda(DataRow fila, int indice) => indice < table.Columns.Count ? fila[indice]?.ToString()?.Trim() : null;

        for (var i = 1; i < table.Rows.Count; i++)
        {
            var fila = table.Rows[i];
            var numeroFila = i + 1; // 1-based, tal como lo ve el usuario en Excel.

            var barcode = Celda(fila, 0);
            var cantidadTexto = Celda(fila, 1);
            var costoTexto = Celda(fila, 2);

            var filaVacia = string.IsNullOrWhiteSpace(barcode) && string.IsNullOrWhiteSpace(cantidadTexto) && string.IsNullOrWhiteSpace(costoTexto);
            if (filaVacia)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(barcode))
            {
                errores.Add(new CongeladoFilaError(numeroFila, "Falta el código de barra (columna A)."));
                continue;
            }

            if (!int.TryParse(cantidadTexto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cantidad))
            {
                errores.Add(new CongeladoFilaError(numeroFila, $"Cantidad inválida en columna B: '{cantidadTexto}'."));
                continue;
            }

            decimal? costoUnitario = null;
            if (!string.IsNullOrWhiteSpace(costoTexto))
            {
                if (!decimal.TryParse(costoTexto, NumberStyles.Number, CultureInfo.InvariantCulture, out var costo))
                {
                    errores.Add(new CongeladoFilaError(numeroFila, $"Costo unitario inválido en columna C: '{costoTexto}'."));
                    continue;
                }
                costoUnitario = costo;
            }

            filas.Add(new CongeladoFilaParseada(barcode, null, cantidad, costoUnitario));
        }

        return new CongeladoParseResult(filas, errores);
    }
}
```

- [ ] **Step 5: Correr los tests, confirmar que pasan**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj --filter CongeladoExcelParserTests`
Expected: 4/4 PASS.

- [ ] **Step 6: Reemplazar `Congelados/Index.cshtml.cs` con el formulario de carga**

Reemplazar el contenido completo del archivo:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Congelados;

/// <summary>
/// Listado de congelados cargados + formulario de carga (Excel exportado del punto
/// de venta contra un Nro. de Inventario) -- ver CongeladoExcelParser para el
/// mapeo de columnas.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
    }

    [BindProperty]
    public CargaCongeladoInput Input { get; set; } = new();

    public IReadOnlyList<CongeladoRowDto> Congelados { get; private set; } = Array.Empty<CongeladoRowDto>();
    public IReadOnlyList<Branch> SucursalesActivas { get; private set; } = Array.Empty<Branch>();
    public IReadOnlyList<string> ErroresDeCarga { get; private set; } = Array.Empty<string>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        await CargarCongeladosAsync(ct);
        await CargarSucursalesAsync(ct);
    }

    public async Task<IActionResult> OnPostCargarAsync(CancellationToken ct)
    {
        await CargarCongeladosAsync(ct);
        await CargarSucursalesAsync(ct);

        if (Input.Archivo is null || Input.Archivo.Length == 0)
        {
            ErrorMessage = "Seleccioná un archivo Excel para cargar.";
            return Page();
        }

        if (Input.BranchId is null || string.IsNullOrWhiteSpace(Input.InventoryNumber))
        {
            ErrorMessage = "Seleccioná la sucursal e ingresá el Nro. de Inventario.";
            return Page();
        }

        CongeladoParseResult resultado;
        await using (var stream = Input.Archivo.OpenReadStream())
        {
            resultado = CongeladoExcelParser.Parse(stream);
        }

        if (resultado.Errores.Count > 0)
        {
            ErroresDeCarga = resultado.Errores.Select(e => $"Fila {e.NumeroFila}: {e.Mensaje}").ToList();
            ErrorMessage = $"El archivo tiene {resultado.Errores.Count} fila(s) con errores -- corregilas y volvé a subirlo.";
            return Page();
        }

        if (resultado.Filas.Count == 0)
        {
            ErrorMessage = "El archivo no tiene filas para cargar.";
            return Page();
        }

        var snapshot = new FrozenInventorySnapshot
        {
            CompanyId = _currentCompany.CompanyId,
            BranchId = Input.BranchId.Value,
            InventoryNumber = Input.InventoryNumber,
            LoadedByUserId = _currentUser.UserId,
            FileName = Input.Archivo.FileName,
        };
        _db.FrozenInventorySnapshots.Add(snapshot);
        await _db.SaveChangesAsync(ct);

        _db.FrozenInventoryLines.AddRange(resultado.Filas.Select(f => new FrozenInventoryLine
        {
            SnapshotId = snapshot.Id,
            Barcode = f.Barcode,
            ProductCode = f.ProductCode,
            Quantity = f.Quantity,
            UnitCost = f.UnitCost,
        }));
        await _db.SaveChangesAsync(ct);

        SuccessMessage = $"Congelado cargado: {resultado.Filas.Count} línea(s).";
        return RedirectToPage();
    }

    private async Task CargarCongeladosAsync(CancellationToken ct)
    {
        var snapshots = await _db.FrozenInventorySnapshots
            .Where(s => s.CompanyId == _currentCompany.CompanyId)
            .OrderByDescending(s => s.LoadedAt)
            .Take(200)
            .ToListAsync(ct);

        var branchIds = snapshots.Select(s => s.BranchId).Distinct().ToList();
        var branches = await _db.Branches
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, ct);

        var lineCounts = await _db.FrozenInventoryLines
            .Where(l => snapshots.Select(s => s.Id).Contains(l.SnapshotId))
            .GroupBy(l => l.SnapshotId)
            .Select(g => new { SnapshotId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SnapshotId, x => x.Count, ct);

        Congelados = snapshots.Select(s => new CongeladoRowDto(
            s.Id,
            branches.GetValueOrDefault(s.BranchId, "?"),
            s.InventoryNumber,
            s.FileName,
            lineCounts.GetValueOrDefault(s.Id, 0),
            s.LoadedAt
        )).ToList();
    }

    private async Task CargarSucursalesAsync(CancellationToken ct)
    {
        SucursalesActivas = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId && b.IsActive)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public sealed class CargaCongeladoInput
    {
        public long? BranchId { get; set; }
        public string? InventoryNumber { get; set; }
        public IFormFile? Archivo { get; set; }
    }

    public sealed record CongeladoRowDto(
        long Id,
        string BranchName,
        string InventoryNumber,
        string FileName,
        int LineCount,
        DateTimeOffset LoadedAt);
}
```

- [ ] **Step 7: Actualizar `Congelados/Index.cshtml` con el formulario**

Reemplazar el contenido completo del archivo:

```html
@page "/auditoria-inventario/congelados"
@model Modulo.AuditoriaInventario.Pages.Congelados.IndexModel
@{
    ViewData["Title"] = "Congelados";
}

<div class="card-surface">
    <h2 class="mb-1">Inventario Congelado</h2>
    <p class="text-muted mb-3">
        Congelado extraído del punto de venta y cargado vía Excel contra un Nro. de
        Inventario.
    </p>

    @if (Model.SuccessMessage is not null)
    {
        <div class="alert alert-success">@Model.SuccessMessage</div>
    }
    @if (Model.ErrorMessage is not null)
    {
        <div class="alert alert-danger">@Model.ErrorMessage</div>
    }
    @if (Model.ErroresDeCarga.Count > 0)
    {
        <ul class="text-danger">
            @foreach (var error in Model.ErroresDeCarga)
            {
                <li>@error</li>
            }
        </ul>
    }

    <form method="post" asp-page-handler="Cargar" enctype="multipart/form-data" class="d-flex align-items-end gap-3 flex-wrap mb-4">
        <div class="mb-0" style="min-width:220px;">
            <label asp-for="Input.BranchId" class="form-label">Sucursal</label>
            <select asp-for="Input.BranchId" class="form-select">
                <option value="">Seleccionar...</option>
                @foreach (var b in Model.SucursalesActivas)
                {
                    <option value="@b.Id">@b.Name</option>
                }
            </select>
        </div>
        <div class="mb-0">
            <label asp-for="Input.InventoryNumber" class="form-label">Nro. Inventario</label>
            <input asp-for="Input.InventoryNumber" class="form-control" />
        </div>
        <div class="mb-0">
            <label asp-for="Input.Archivo" class="form-label">Archivo Excel</label>
            <input asp-for="Input.Archivo" type="file" class="form-control" accept=".xlsx,.xls" />
        </div>
        <button type="submit" class="btn-primary">Cargar</button>
    </form>

    @if (Model.Congelados.Count == 0)
    {
        <p class="text-muted">Todavía no hay congelados cargados.</p>
    }
    else
    {
        <div class="document-list-table-wrapper">
            <table class="table document-list-table mb-0">
                <thead>
                    <tr>
                        <th>Sucursal</th>
                        <th>Nro. Inventario</th>
                        <th>Archivo</th>
                        <th>Líneas</th>
                        <th>Cargado</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var c in Model.Congelados)
                    {
                        <tr>
                            <td>@c.BranchName</td>
                            <td>@c.InventoryNumber</td>
                            <td>@c.FileName</td>
                            <td>@c.LineCount.ToString("N0")</td>
                            <td>@c.LoadedAt.ToLocalTime().ToString("dd-MM-yyyy HH:mm")</td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    }
</div>
```

- [ ] **Step 8: Compilar y correr toda la suite**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj && dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: build OK, todos los tests en PASS.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): importador de Excel para congelados"
```

---

### Task 4: Mapeo SAP en la aprobación de ajustes

**Files:**
- Modify: `Modulo.AuditoriaInventario/Models/Branch.cs`
- Modify: `Modulo.AuditoriaInventario/Models/Product.cs`
- Modify: `Modulo.AuditoriaInventario/Data/AuditoriaInventarioDbContext.cs`
- Create: `Modulo.AuditoriaInventario/Servicios/IAjusteService.cs`
- Create: `Modulo.AuditoriaInventario/Servicios/AjusteService.cs`
- Modify: `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`
- Modify: `Modulo.AuditoriaInventario/Pages/Ajustes/Index.cshtml.cs`
- Create: `Modulo.AuditoriaInventario.Tests/AjusteServiceTests.cs`
- Create: migraciones `AddSapMappingColumns` (Postgres + SqlServer)

**Interfaces:**
- Consumes: `InventoryAdjustment`, `InventoryDifference`, `InventorySession`, `Branch`, `Product`, `SapAdjustmentQueueItem` (todos existentes).
- Produces: `IAjusteService.AprobarAsync(AuditoriaInventarioDbContext db, long adjustmentId, Guid approvedByUserId, CancellationToken ct = default) -> Task<AprobarAjusteResultado>` con `AprobarAjusteResultado(bool Exitoso, string Mensaje)`.

- [ ] **Step 1: Escribir el test que falla**

Crear `Modulo.AuditoriaInventario.Tests/AjusteServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;

namespace Modulo.AuditoriaInventario.Tests;

public class AjusteServiceTests
{
    private static AuditoriaInventarioDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<AuditoriaInventarioDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<(AuditoriaInventarioDbContext Db, InventoryAdjustment Ajuste)> PrepararEscenarioAsync(
        string? sapCompanyCode, string? sapWarehouseCode, string? sapMaterialCode)
    {
        var db = CrearContexto();
        var companyId = Guid.NewGuid();

        var branch = new Branch { CompanyId = companyId, BranchCode = "001", Name = "Centro", SapCompanyCode = sapCompanyCode, SapWarehouseCode = sapWarehouseCode };
        db.Branches.Add(branch);
        var user = new CaptureUser { CompanyId = companyId, Username = "a1", PasswordHash = "x", PasswordSalt = "x" };
        db.CaptureUsers.Add(user);
        await db.SaveChangesAsync();

        var sessionId = Guid.NewGuid();
        db.InventorySessions.Add(new InventorySession { Id = sessionId, CompanyId = companyId, BranchId = branch.Id, InventoryNumber = "INV-1", ResponsibleUserId = user.Id });

        var snapshot = new FrozenInventorySnapshot { CompanyId = companyId, BranchId = branch.Id, InventoryNumber = "INV-1", LoadedByUserId = Guid.NewGuid(), FileName = "x.xlsx" };
        db.FrozenInventorySnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        if (sapMaterialCode is not null)
        {
            db.Products.Add(new Product { CompanyId = companyId, Barcode = "AAA", ProductCode = "SKU-1", SapMaterialCode = sapMaterialCode });
        }

        var diferencia = new InventoryDifference
        {
            SessionId = sessionId,
            SnapshotId = snapshot.Id,
            Barcode = "AAA",
            CapturedQuantity = 12,
            FrozenQuantity = 10,
            QuantityDiff = 2,
            AmountDiff = 200m,
        };
        db.InventoryDifferences.Add(diferencia);
        await db.SaveChangesAsync();

        var ajuste = new InventoryAdjustment { DifferenceId = diferencia.Id, ProposedByUserId = Guid.NewGuid() };
        db.InventoryAdjustments.Add(ajuste);
        await db.SaveChangesAsync();

        return (db, ajuste);
    }

    [Fact]
    public async Task AprobarAsync_ConMapeoSapCompleto_EncolaYCambiaEstado()
    {
        var (db, ajuste) = await PrepararEscenarioAsync("1000", "WH01", "MAT-AAA");
        var service = new AjusteService();
        var aprobadoPor = Guid.NewGuid();

        var resultado = await service.AprobarAsync(db, ajuste.Id, aprobadoPor);

        Assert.True(resultado.Exitoso);
        var recargado = await db.InventoryAdjustments.FirstAsync(a => a.Id == ajuste.Id);
        Assert.Equal("APPROVED", recargado.Status);
        Assert.Equal(aprobadoPor, recargado.ApprovedByUserId);

        var cola = await db.SapAdjustmentQueueItems.FirstAsync(q => q.AdjustmentId == ajuste.Id);
        Assert.Equal("1000", cola.SapCompanyCode);
        Assert.Equal("WH01", cola.SapWarehouseCode);
        Assert.Equal("MAT-AAA", cola.SapMaterialCode);
        Assert.Equal(2, cola.Quantity);
        Assert.Equal("READY", cola.Status);
    }

    [Fact]
    public async Task AprobarAsync_SinCodigoSapEnSucursal_RechazaSinCambiarEstado()
    {
        var (db, ajuste) = await PrepararEscenarioAsync(null, null, "MAT-AAA");
        var service = new AjusteService();

        var resultado = await service.AprobarAsync(db, ajuste.Id, Guid.NewGuid());

        Assert.False(resultado.Exitoso);
        var recargado = await db.InventoryAdjustments.FirstAsync(a => a.Id == ajuste.Id);
        Assert.Equal("PROPOSED", recargado.Status);
        Assert.Empty(await db.SapAdjustmentQueueItems.ToListAsync());
    }

    [Fact]
    public async Task AprobarAsync_SinProductoMapeadoASap_RechazaSinCambiarEstado()
    {
        var (db, ajuste) = await PrepararEscenarioAsync("1000", "WH01", null);
        var service = new AjusteService();

        var resultado = await service.AprobarAsync(db, ajuste.Id, Guid.NewGuid());

        Assert.False(resultado.Exitoso);
        Assert.Empty(await db.SapAdjustmentQueueItems.ToListAsync());
    }
}
```

- [ ] **Step 2: Confirmar que no compila**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: error de compilación (`AjusteService` no existe, `Branch.SapCompanyCode`/`Product.SapMaterialCode` no existen).

- [ ] **Step 3: Agregar los campos de mapeo SAP a los modelos**

En `Modulo.AuditoriaInventario/Models/Branch.cs`, agregar antes del cierre de la clase:

```csharp
    public string? SapCompanyCode { get; set; }

    public string? SapWarehouseCode { get; set; }
```

En `Modulo.AuditoriaInventario/Models/Product.cs`, agregar antes de `Source`:

```csharp
    public string? SapMaterialCode { get; set; }
```

- [ ] **Step 4: Mapear las columnas nuevas en el DbContext**

En `Modulo.AuditoriaInventario/Data/AuditoriaInventarioDbContext.cs`, dentro del bloque `modelBuilder.Entity<Branch>` (línea ~69, después de `IsActive`), agregar:

```csharp
            e.Property(x => x.SapCompanyCode).HasColumnName("sap_company_code").HasMaxLength(20);
            e.Property(x => x.SapWarehouseCode).HasColumnName("sap_warehouse_code").HasMaxLength(20);
```

Dentro del bloque `modelBuilder.Entity<Product>` (línea ~53, después de `Source`), agregar:

```csharp
            e.Property(x => x.SapMaterialCode).HasColumnName("sap_material_code").HasMaxLength(50);
```

- [ ] **Step 5: Implementar `IAjusteService`/`AjusteService`**

Crear `Modulo.AuditoriaInventario/Servicios/IAjusteService.cs`:

```csharp
using Modulo.AuditoriaInventario.Data;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Aprobación manual de un InventoryAdjustment: resuelve el mapeo SAP
/// (SapCompanyCode/SapWarehouseCode desde Branch, SapMaterialCode desde Product) y
/// genera la fila en SapAdjustmentQueueItem -- el único punto donde algo entra a esa
/// cola, decisión explícita del dueño del proyecto (nunca envío automático).
/// </summary>
public interface IAjusteService
{
    Task<AprobarAjusteResultado> AprobarAsync(AuditoriaInventarioDbContext db, long adjustmentId, Guid approvedByUserId, CancellationToken ct = default);
}

public sealed record AprobarAjusteResultado(bool Exitoso, string Mensaje);
```

Crear `Modulo.AuditoriaInventario/Servicios/AjusteService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Servicios;

public sealed class AjusteService : IAjusteService
{
    public async Task<AprobarAjusteResultado> AprobarAsync(AuditoriaInventarioDbContext db, long adjustmentId, Guid approvedByUserId, CancellationToken ct = default)
    {
        var ajuste = await db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId, ct);
        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            return new AprobarAjusteResultado(false, "El ajuste ya no está disponible para aprobar.");
        }

        var diferencia = await db.InventoryDifferences.FirstOrDefaultAsync(d => d.Id == ajuste.DifferenceId, ct);
        if (diferencia is null)
        {
            return new AprobarAjusteResultado(false, "La diferencia asociada a este ajuste ya no existe.");
        }

        var sesion = await db.InventorySessions.FirstOrDefaultAsync(s => s.Id == diferencia.SessionId, ct);
        if (sesion is null)
        {
            return new AprobarAjusteResultado(false, "La sesión asociada a esta diferencia ya no existe.");
        }

        var sucursal = await db.Branches.FirstOrDefaultAsync(b => b.Id == sesion.BranchId, ct);
        if (sucursal is null || string.IsNullOrWhiteSpace(sucursal.SapCompanyCode) || string.IsNullOrWhiteSpace(sucursal.SapWarehouseCode))
        {
            return new AprobarAjusteResultado(false, "La sucursal no tiene código SAP configurado -- completalo en el catálogo de sucursales antes de aprobar.");
        }

        var producto = await db.Products.FirstOrDefaultAsync(p => p.CompanyId == sesion.CompanyId && p.Barcode == diferencia.Barcode, ct);
        if (producto is null || string.IsNullOrWhiteSpace(producto.SapMaterialCode))
        {
            return new AprobarAjusteResultado(false, $"El código de barra '{diferencia.Barcode}' no tiene material SAP mapeado -- completalo en el maestro de productos antes de aprobar.");
        }

        ajuste.Status = "APPROVED";
        ajuste.ApprovedAt = DateTimeOffset.UtcNow;
        ajuste.ApprovedByUserId = approvedByUserId;

        db.SapAdjustmentQueueItems.Add(new SapAdjustmentQueueItem
        {
            AdjustmentId = ajuste.Id,
            SapCompanyCode = sucursal.SapCompanyCode,
            SapWarehouseCode = sucursal.SapWarehouseCode,
            SapMaterialCode = producto.SapMaterialCode,
            Quantity = diferencia.QuantityDiff,
            Status = "READY",
        });

        await db.SaveChangesAsync(ct);
        return new AprobarAjusteResultado(true, "Ajuste aprobado y encolado hacia SAP.");
    }
}
```

- [ ] **Step 6: Correr los tests, confirmar que pasan**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj --filter AjusteServiceTests`
Expected: 3/3 PASS.

- [ ] **Step 7: Registrar el servicio en DI**

En `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`, agregar debajo de la línea agregada en la Task 2 (`services.AddScoped<IDiferenciaEngine, DiferenciaEngine>();`):

```csharp
        services.AddScoped<IAjusteService, AjusteService>();
```

- [ ] **Step 8: Simplificar `Ajustes/Index.cshtml.cs` para usar el servicio + filtrar por compañía + completar `ApprovedByUserId`**

Reemplazar el contenido completo del archivo:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Ajustes;

/// <summary>
/// Cola de ajustes propuestos, con el gate de aprobación manual obligatorio antes
/// de escribir en SapAdjustmentQueueItem -- ver AjusteService para el mapeo real.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private readonly ICurrentUserContext _currentUser;
    private readonly IAjusteService _ajusteService;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany, ICurrentUserContext currentUser, IAjusteService ajusteService)
    {
        _db = db;
        _currentCompany = currentCompany;
        _currentUser = currentUser;
        _ajusteService = ajusteService;
    }

    public IReadOnlyList<InventoryAdjustment> Ajustes { get; private set; } = Array.Empty<InventoryAdjustment>();

    public async Task OnGetAsync(CancellationToken ct)
    {
        // InventoryAdjustment no lleva company_id directo -- llega vía
        // InventoryDifference -> InventorySession.
        Ajustes = await (
            from a in _db.InventoryAdjustments
            join d in _db.InventoryDifferences on a.DifferenceId equals d.Id
            join s in _db.InventorySessions on d.SessionId equals s.Id
            where s.CompanyId == _currentCompany.CompanyId
            orderby a.ProposedAt descending
            select a)
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<IActionResult> OnPostAprobarAsync(long id, CancellationToken ct)
    {
        var resultado = await _ajusteService.AprobarAsync(_db, id, _currentUser.UserId, ct);
        if (!resultado.Exitoso)
        {
            ErrorMessage = resultado.Mensaje;
            return RedirectToPage();
        }

        SuccessMessage = resultado.Mensaje;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRechazarAsync(long id, CancellationToken ct)
    {
        var ajuste = await _db.InventoryAdjustments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ajuste is null || ajuste.Status != "PROPOSED")
        {
            ErrorMessage = "El ajuste ya no está disponible para rechazar.";
            return RedirectToPage();
        }

        ajuste.Status = "REJECTED";
        await _db.SaveChangesAsync(ct);

        SuccessMessage = "Ajuste rechazado.";
        return RedirectToPage();
    }
}
```

- [ ] **Step 9: Compilar todo el conjunto**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj`
Expected: Build succeeded, 0 errores.

- [ ] **Step 10: Generar las migraciones (los dos motores)**

Desde `Portal SaaS - Plugins/Modulo.AuditoriaInventario/src/`:

```
dotnet tool run dotnet-ef migrations add AddSapMappingColumns --project Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj --startup-project Modulo.AuditoriaInventario.Migrations.Postgres/Modulo.AuditoriaInventario.Migrations.Postgres.csproj --context AuditoriaInventarioDbContext

dotnet tool run dotnet-ef migrations add AddSapMappingColumns --project Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj --startup-project Modulo.AuditoriaInventario.Migrations.SqlServer/Modulo.AuditoriaInventario.Migrations.SqlServer.csproj --context AuditoriaInventarioDbContext
```

Expected: cada comando genera un `AddColumn` (3 columnas: `sap_company_code`, `sap_warehouse_code` en `branches`; `sap_material_code` en `products`), sin tocar otras tablas.

- [ ] **Step 11: Correr toda la suite de tests**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: todos los tests en PASS.

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): mapeo SAP en la aprobación de ajustes (Branch/Product -> SapAdjustmentQueueItem)"
```

---

### Task 5: Página admin de Sucursales

**Files:**
- Create: `Modulo.AuditoriaInventario/Pages/Sucursales/Index.cshtml`
- Create: `Modulo.AuditoriaInventario/Pages/Sucursales/Index.cshtml.cs`
- Modify: `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs` (`GetMenu`)

**Interfaces:**
- Consumes: `Branch` (con `SapCompanyCode`/`SapWarehouseCode` de la Task 4).

- [ ] **Step 1: Crear el code-behind**

Crear `Modulo.AuditoriaInventario/Pages/Sucursales/Index.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Sucursales;

/// <summary>Mantenedor de Branch -- catálogo simple, sin lógica de negocio calculada.</summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public SucursalInput Input { get; set; } = new();

    public IReadOnlyList<Branch> Sucursales { get; private set; } = Array.Empty<Branch>();
    public long? Editando { get; set; }

    public async Task OnGetAsync(long? editando, CancellationToken ct)
    {
        await CargarListaAsync(ct);

        if (editando is { } id)
        {
            var sucursal = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == _currentCompany.CompanyId, ct);
            if (sucursal is not null)
            {
                Editando = id;
                Input = new SucursalInput
                {
                    BranchCode = sucursal.BranchCode,
                    Name = sucursal.Name,
                    IsActive = sucursal.IsActive,
                    SapCompanyCode = sucursal.SapCompanyCode,
                    SapWarehouseCode = sucursal.SapWarehouseCode,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? editando, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var sucursal = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == _currentCompany.CompanyId, ct)
                    ?? throw new InvalidOperationException("La sucursal no existe.");
                AplicarInput(sucursal);
                SuccessMessage = "Sucursal actualizada.";
            }
            else
            {
                if (await _db.Branches.AnyAsync(b => b.CompanyId == _currentCompany.CompanyId && b.BranchCode == Input.BranchCode, ct))
                {
                    throw new InvalidOperationException($"Ya existe una sucursal con el código '{Input.BranchCode}'.");
                }

                var sucursal = new Branch { CompanyId = _currentCompany.CompanyId, BranchCode = Input.BranchCode, Name = Input.Name };
                AplicarInput(sucursal);
                _db.Branches.Add(sucursal);
                SuccessMessage = "Sucursal creada.";
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            var sucursal = await _db.Branches.FirstOrDefaultAsync(b => b.Id == id && b.CompanyId == _currentCompany.CompanyId, ct)
                ?? throw new InvalidOperationException("La sucursal no existe.");
            _db.Branches.Remove(sucursal);
            await _db.SaveChangesAsync(ct);
            SuccessMessage = "Sucursal eliminada.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private void AplicarInput(Branch sucursal)
    {
        sucursal.BranchCode = Input.BranchCode;
        sucursal.Name = Input.Name;
        sucursal.IsActive = Input.IsActive;
        sucursal.SapCompanyCode = string.IsNullOrWhiteSpace(Input.SapCompanyCode) ? null : Input.SapCompanyCode;
        sucursal.SapWarehouseCode = string.IsNullOrWhiteSpace(Input.SapWarehouseCode) ? null : Input.SapWarehouseCode;
    }

    private async Task CargarListaAsync(CancellationToken ct)
    {
        Sucursales = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
    }

    public sealed class SucursalInput
    {
        [Required(ErrorMessage = "El código es obligatorio.")]
        [MaxLength(20)]
        public string BranchCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        [MaxLength(20)]
        public string? SapCompanyCode { get; set; }

        [MaxLength(20)]
        public string? SapWarehouseCode { get; set; }
    }
}
```

- [ ] **Step 2: Crear la vista con el drawer**

Crear `Modulo.AuditoriaInventario/Pages/Sucursales/Index.cshtml`:

```html
@page "/auditoria-inventario/sucursales"
@model Modulo.AuditoriaInventario.Pages.Sucursales.IndexModel
@{
    ViewData["Title"] = "Sucursales";
    var formularioAbierto = Model.Editando is not null || !ViewData.ModelState.IsValid;
}

<div class="card-surface">
    <div class="d-flex align-items-center justify-content-between gap-3 mb-3">
        <h2 class="mb-0">Sucursales</h2>
        <button type="button" class="btn-primary" data-drawer-open="drawer-sucursal">+ Nueva sucursal</button>
    </div>

    @if (Model.SuccessMessage is not null)
    {
        <div class="alert alert-success">@Model.SuccessMessage</div>
    }
    @if (Model.ErrorMessage is not null)
    {
        <div class="alert alert-danger">@Model.ErrorMessage</div>
    }

    <table class="table-ps">
        <thead><tr><th>Código</th><th>Nombre</th><th>Cód. SAP compañía</th><th>Cód. SAP bodega</th><th>Activa</th><th class="col-actions"></th></tr></thead>
        <tbody>
            @foreach (var s in Model.Sucursales)
            {
                <tr class="@(s.IsActive ? "" : "row-inactive")">
                    <td>@s.BranchCode</td>
                    <td>@s.Name</td>
                    <td>@(s.SapCompanyCode ?? "-")</td>
                    <td>@(s.SapWarehouseCode ?? "-")</td>
                    <td>@(s.IsActive ? "Sí" : "No")</td>
                    <td class="col-actions">
                        <a href="~/auditoria-inventario/sucursales?editando=@s.Id" class="btn-ghost" title="Editar"><i class="bi bi-pencil"></i></a>
                        <form asp-page="./Index" asp-page-handler="Eliminar" asp-route-id="@s.Id" method="post" class="d-inline-block"
                              onsubmit="return confirm('¿Eliminar la sucursal &quot;@s.Name&quot;?');">
                            <button type="submit" class="btn-ghost btn-ghost--danger" title="Eliminar"><i class="bi bi-trash"></i></button>
                        </form>
                    </td>
                </tr>
            }
            @if (Model.Sucursales.Count == 0)
            {
                <tr><td colspan="6" class="text-muted">Sin sucursales todavía.</td></tr>
            }
        </tbody>
    </table>

    <div id="drawer-sucursal" class="drawer-overlay @(formularioAbierto ? "is-open" : "")" data-drawer>
        <aside class="drawer-panel @(formularioAbierto ? "is-open" : "")">
            <div class="drawer-header">
                <h3>@(Model.Editando is null ? "Nueva sucursal" : "Editar sucursal")</h3>
                <button type="button" class="btn-ghost" data-drawer-close aria-label="Cerrar"><i class="bi bi-x-lg"></i></button>
            </div>
            <form asp-page="./Index" asp-page-handler="Guardar" asp-route-editando="@Model.Editando" method="post">
                <div class="drawer-body">
                    <div asp-validation-summary="All" class="text-danger mb-3"></div>
                    <div class="mb-3">
                        <label asp-for="Input.BranchCode" class="form-label">Código</label>
                        <input asp-for="Input.BranchCode" class="form-control" />
                    </div>
                    <div class="mb-3">
                        <label asp-for="Input.Name" class="form-label">Nombre</label>
                        <input asp-for="Input.Name" class="form-control" />
                    </div>
                    <div class="mb-3">
                        <label asp-for="Input.SapCompanyCode" class="form-label">Código SAP compañía</label>
                        <input asp-for="Input.SapCompanyCode" class="form-control" />
                    </div>
                    <div class="mb-3">
                        <label asp-for="Input.SapWarehouseCode" class="form-label">Código SAP bodega</label>
                        <input asp-for="Input.SapWarehouseCode" class="form-control" />
                    </div>
                    <div class="mb-3 form-check">
                        <input asp-for="Input.IsActive" type="checkbox" class="form-check-input" />
                        <label asp-for="Input.IsActive" class="form-check-label">Activa</label>
                    </div>
                </div>
                <div class="drawer-footer">
                    <button type="button" class="btn-secondary" data-drawer-close>Cancelar</button>
                    <button type="submit" class="btn-primary">@(Model.Editando is null ? "Crear" : "Guardar")</button>
                </div>
            </form>
        </aside>
    </div>
</div>
```

- [ ] **Step 3: Agregar la entrada de menú**

En `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`, agregar dentro de `GetMenu()`, después del bloque `ajustes` (después de la línea 82, antes del `}` de cierre del método):

```csharp
        yield return new MenuItemDefinition
        {
            Code = "sucursales",
            ParentCode = "raiz",
            Name = "Sucursales",
            Icon = "bi-shop",
            PageRoute = "/auditoria-inventario/sucursales",
            Order = 5,
        };
```

- [ ] **Step 4: Compilar (sin tests dedicados -- ver Global Constraints)**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj`
Expected: Build succeeded, 0 errores.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): página de administración de Sucursales"
```

---

### Task 6: Página admin de Sectores

**Files:**
- Create: `Modulo.AuditoriaInventario/Pages/Sectores/Index.cshtml`
- Create: `Modulo.AuditoriaInventario/Pages/Sectores/Index.cshtml.cs`
- Modify: `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs` (`GetMenu`)

**Interfaces:**
- Consumes: `InventorySector`, `Branch` (existentes).

- [ ] **Step 1: Crear el code-behind**

Crear `Modulo.AuditoriaInventario/Pages/Sectores/Index.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Sectores;

/// <summary>Mantenedor de InventorySector -- catálogo simple, sin lógica de negocio calculada.</summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public SectorInput Input { get; set; } = new();

    public IReadOnlyList<SectorRowDto> Sectores { get; private set; } = Array.Empty<SectorRowDto>();
    public List<SelectListItem> SucursalesDisponibles { get; private set; } = new();
    public long? Editando { get; set; }

    public async Task OnGetAsync(long? editando, CancellationToken ct)
    {
        await CargarListasAsync(ct);

        if (editando is { } id)
        {
            var sector = await _db.InventorySectors.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == _currentCompany.CompanyId, ct);
            if (sector is not null)
            {
                Editando = id;
                Input = new SectorInput { Name = sector.Name, BranchId = sector.BranchId, IsActive = sector.IsActive };
            }
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? editando, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            Editando = editando;
            await CargarListasAsync(ct);
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var sector = await _db.InventorySectors.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == _currentCompany.CompanyId, ct)
                    ?? throw new InvalidOperationException("El sector no existe.");
                AplicarInput(sector);
                SuccessMessage = "Sector actualizado.";
            }
            else
            {
                var sector = new InventorySector { CompanyId = _currentCompany.CompanyId, Name = Input.Name };
                AplicarInput(sector);
                _db.InventorySectors.Add(sector);
                SuccessMessage = "Sector creado.";
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Editando = editando;
            await CargarListasAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            var sector = await _db.InventorySectors.FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == _currentCompany.CompanyId, ct)
                ?? throw new InvalidOperationException("El sector no existe.");
            _db.InventorySectors.Remove(sector);
            await _db.SaveChangesAsync(ct);
            SuccessMessage = "Sector eliminado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private void AplicarInput(InventorySector sector)
    {
        sector.Name = Input.Name;
        sector.BranchId = Input.BranchId;
        sector.IsActive = Input.IsActive;
    }

    private async Task CargarListasAsync(CancellationToken ct)
    {
        var sucursales = await _db.Branches
            .Where(b => b.CompanyId == _currentCompany.CompanyId)
            .OrderBy(b => b.Name)
            .ToListAsync(ct);
        SucursalesDisponibles = sucursales.Select(b => new SelectListItem(b.Name, b.Id.ToString())).ToList();
        var nombreSucursal = sucursales.ToDictionary(b => b.Id, b => b.Name);

        var sectores = await _db.InventorySectors
            .Where(s => s.CompanyId == _currentCompany.CompanyId)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        Sectores = sectores.Select(s => new SectorRowDto(
            s.Id, s.Name, s.BranchId is { } bid ? nombreSucursal.GetValueOrDefault(bid, "?") : "Todas", s.IsActive)).ToList();
    }

    public sealed class SectorInput
    {
        [Required(ErrorMessage = "El nombre es obligatorio.")]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public long? BranchId { get; set; }

        public bool IsActive { get; set; } = true;
    }

    public sealed record SectorRowDto(long Id, string Name, string BranchName, bool IsActive);
}
```

- [ ] **Step 2: Crear la vista con el drawer**

Crear `Modulo.AuditoriaInventario/Pages/Sectores/Index.cshtml`:

```html
@page "/auditoria-inventario/sectores"
@model Modulo.AuditoriaInventario.Pages.Sectores.IndexModel
@{
    ViewData["Title"] = "Sectores";
    var formularioAbierto = Model.Editando is not null || !ViewData.ModelState.IsValid;
}

<div class="card-surface">
    <div class="d-flex align-items-center justify-content-between gap-3 mb-3">
        <h2 class="mb-0">Sectores de Captura</h2>
        <button type="button" class="btn-primary" data-drawer-open="drawer-sector">+ Nuevo sector</button>
    </div>

    @if (Model.SuccessMessage is not null)
    {
        <div class="alert alert-success">@Model.SuccessMessage</div>
    }
    @if (Model.ErrorMessage is not null)
    {
        <div class="alert alert-danger">@Model.ErrorMessage</div>
    }

    <table class="table-ps">
        <thead><tr><th>Nombre</th><th>Sucursal</th><th>Activo</th><th class="col-actions"></th></tr></thead>
        <tbody>
            @foreach (var s in Model.Sectores)
            {
                <tr class="@(s.IsActive ? "" : "row-inactive")">
                    <td>@s.Name</td>
                    <td>@s.BranchName</td>
                    <td>@(s.IsActive ? "Sí" : "No")</td>
                    <td class="col-actions">
                        <a href="~/auditoria-inventario/sectores?editando=@s.Id" class="btn-ghost" title="Editar"><i class="bi bi-pencil"></i></a>
                        <form asp-page="./Index" asp-page-handler="Eliminar" asp-route-id="@s.Id" method="post" class="d-inline-block"
                              onsubmit="return confirm('¿Eliminar el sector &quot;@s.Name&quot;?');">
                            <button type="submit" class="btn-ghost btn-ghost--danger" title="Eliminar"><i class="bi bi-trash"></i></button>
                        </form>
                    </td>
                </tr>
            }
            @if (Model.Sectores.Count == 0)
            {
                <tr><td colspan="4" class="text-muted">Sin sectores todavía.</td></tr>
            }
        </tbody>
    </table>

    <div id="drawer-sector" class="drawer-overlay @(formularioAbierto ? "is-open" : "")" data-drawer>
        <aside class="drawer-panel @(formularioAbierto ? "is-open" : "")">
            <div class="drawer-header">
                <h3>@(Model.Editando is null ? "Nuevo sector" : "Editar sector")</h3>
                <button type="button" class="btn-ghost" data-drawer-close aria-label="Cerrar"><i class="bi bi-x-lg"></i></button>
            </div>
            <form asp-page="./Index" asp-page-handler="Guardar" asp-route-editando="@Model.Editando" method="post">
                <div class="drawer-body">
                    <div asp-validation-summary="All" class="text-danger mb-3"></div>
                    <div class="mb-3">
                        <label asp-for="Input.Name" class="form-label">Nombre</label>
                        <input asp-for="Input.Name" class="form-control" />
                    </div>
                    <div class="mb-3">
                        <label asp-for="Input.BranchId" class="form-label">Sucursal</label>
                        <select asp-for="Input.BranchId" asp-items="Model.SucursalesDisponibles" class="form-select">
                            <option value="">Todas las sucursales</option>
                        </select>
                    </div>
                    <div class="mb-3 form-check">
                        <input asp-for="Input.IsActive" type="checkbox" class="form-check-input" />
                        <label asp-for="Input.IsActive" class="form-check-label">Activo</label>
                    </div>
                </div>
                <div class="drawer-footer">
                    <button type="button" class="btn-secondary" data-drawer-close>Cancelar</button>
                    <button type="submit" class="btn-primary">@(Model.Editando is null ? "Crear" : "Guardar")</button>
                </div>
            </form>
        </aside>
    </div>
</div>
```

- [ ] **Step 3: Agregar la entrada de menú**

En `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`, agregar después del bloque `sucursales` creado en la Task 5:

```csharp
        yield return new MenuItemDefinition
        {
            Code = "sectores",
            ParentCode = "raiz",
            Name = "Sectores",
            Icon = "bi-grid-3x3-gap",
            PageRoute = "/auditoria-inventario/sectores",
            Order = 6,
        };
```

- [ ] **Step 4: Compilar (sin tests dedicados -- ver Global Constraints)**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj`
Expected: Build succeeded, 0 errores.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): página de administración de Sectores"
```

---

### Task 7: Página admin de Capturadores

**Files:**
- Create: `Modulo.AuditoriaInventario/Pages/Capturadores/Index.cshtml`
- Create: `Modulo.AuditoriaInventario/Pages/Capturadores/Index.cshtml.cs`
- Modify: `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs` (`GetMenu`)

**Interfaces:**
- Consumes: `CaptureUser` (con `PasswordSalt` de la Task 1), `PasswordHasher.Hash(string) -> (string Hash, string Salt)` (de la Task 1).

- [ ] **Step 1: Crear el code-behind**

Crear `Modulo.AuditoriaInventario/Pages/Capturadores/Index.cshtml.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using Modulo.AuditoriaInventario.Servicios;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Pages.Capturadores;

/// <summary>
/// Mantenedor de CaptureUser -- catálogo simple con un campo write-only
/// (Password): dejarlo en blanco al editar no cambia la contraseña actual, mismo
/// criterio que el resto de la plataforma para secretos.
/// </summary>
public sealed class IndexModel : AuditoriaInventarioPageModelBase
{
    private readonly AuditoriaInventarioDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;

    public IndexModel(AuditoriaInventarioDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    [BindProperty]
    public CapturadorInput Input { get; set; } = new();

    public IReadOnlyList<CaptureUser> Capturadores { get; private set; } = Array.Empty<CaptureUser>();
    public long? Editando { get; set; }

    public async Task OnGetAsync(long? editando, CancellationToken ct)
    {
        await CargarListaAsync(ct);

        if (editando is { } id)
        {
            var capturador = await _db.CaptureUsers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct);
            if (capturador is not null)
            {
                Editando = id;
                Input = new CapturadorInput { Username = capturador.Username, FullName = capturador.FullName, IsActive = capturador.IsActive };
            }
        }
    }

    public async Task<IActionResult> OnPostGuardarAsync(long? editando, CancellationToken ct)
    {
        if (editando is null && string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError("Input.Password", "La contraseña es obligatoria para un capturador nuevo.");
        }

        if (!ModelState.IsValid)
        {
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        try
        {
            if (editando is { } id)
            {
                var capturador = await _db.CaptureUsers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct)
                    ?? throw new InvalidOperationException("El capturador no existe.");

                if (await _db.CaptureUsers.AnyAsync(c => c.CompanyId == _currentCompany.CompanyId && c.Username == Input.Username && c.Id != id, ct))
                {
                    throw new InvalidOperationException($"Ya existe un capturador con el usuario '{Input.Username}'.");
                }

                capturador.Username = Input.Username;
                capturador.FullName = string.IsNullOrWhiteSpace(Input.FullName) ? null : Input.FullName;
                capturador.IsActive = Input.IsActive;

                if (!string.IsNullOrWhiteSpace(Input.Password))
                {
                    var (hash, salt) = PasswordHasher.Hash(Input.Password);
                    capturador.PasswordHash = hash;
                    capturador.PasswordSalt = salt;
                }

                SuccessMessage = "Capturador actualizado.";
            }
            else
            {
                if (await _db.CaptureUsers.AnyAsync(c => c.CompanyId == _currentCompany.CompanyId && c.Username == Input.Username, ct))
                {
                    throw new InvalidOperationException($"Ya existe un capturador con el usuario '{Input.Username}'.");
                }

                var (hash, salt) = PasswordHasher.Hash(Input.Password!);
                var capturador = new CaptureUser
                {
                    CompanyId = _currentCompany.CompanyId,
                    Username = Input.Username,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    FullName = string.IsNullOrWhiteSpace(Input.FullName) ? null : Input.FullName,
                    IsActive = Input.IsActive,
                };
                _db.CaptureUsers.Add(capturador);
                SuccessMessage = "Capturador creado.";
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
            Editando = editando;
            await CargarListaAsync(ct);
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEliminarAsync(long id, CancellationToken ct)
    {
        try
        {
            var capturador = await _db.CaptureUsers.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == _currentCompany.CompanyId, ct)
                ?? throw new InvalidOperationException("El capturador no existe.");
            _db.CaptureUsers.Remove(capturador);
            await _db.SaveChangesAsync(ct);
            SuccessMessage = "Capturador eliminado.";
        }
        catch (Exception ex)
        {
            ErrorMessage = GetErrorMessage(ex);
        }

        return RedirectToPage();
    }

    private async Task CargarListaAsync(CancellationToken ct)
    {
        Capturadores = await _db.CaptureUsers
            .Where(c => c.CompanyId == _currentCompany.CompanyId)
            .OrderBy(c => c.Username)
            .ToListAsync(ct);
    }

    public sealed class CapturadorInput
    {
        [Required(ErrorMessage = "El usuario es obligatorio.")]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? FullName { get; set; }

        public string? Password { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
```

- [ ] **Step 2: Crear la vista con el drawer**

Crear `Modulo.AuditoriaInventario/Pages/Capturadores/Index.cshtml`:

```html
@page "/auditoria-inventario/capturadores"
@model Modulo.AuditoriaInventario.Pages.Capturadores.IndexModel
@{
    ViewData["Title"] = "Capturadores";
    var formularioAbierto = Model.Editando is not null || !ViewData.ModelState.IsValid;
}

<div class="card-surface">
    <div class="d-flex align-items-center justify-content-between gap-3 mb-3">
        <h2 class="mb-0">Capturadores</h2>
        <button type="button" class="btn-primary" data-drawer-open="drawer-capturador">+ Nuevo capturador</button>
    </div>

    <p class="text-muted mb-3">Usuarios livianos para el equipo de captura (PWA) -- login independiente del portal.</p>

    @if (Model.SuccessMessage is not null)
    {
        <div class="alert alert-success">@Model.SuccessMessage</div>
    }
    @if (Model.ErrorMessage is not null)
    {
        <div class="alert alert-danger">@Model.ErrorMessage</div>
    }

    <table class="table-ps">
        <thead><tr><th>Usuario</th><th>Nombre completo</th><th>Activo</th><th class="col-actions"></th></tr></thead>
        <tbody>
            @foreach (var c in Model.Capturadores)
            {
                <tr class="@(c.IsActive ? "" : "row-inactive")">
                    <td>@c.Username</td>
                    <td>@(c.FullName ?? "-")</td>
                    <td>@(c.IsActive ? "Sí" : "No")</td>
                    <td class="col-actions">
                        <a href="~/auditoria-inventario/capturadores?editando=@c.Id" class="btn-ghost" title="Editar"><i class="bi bi-pencil"></i></a>
                        <form asp-page="./Index" asp-page-handler="Eliminar" asp-route-id="@c.Id" method="post" class="d-inline-block"
                              onsubmit="return confirm('¿Eliminar el capturador &quot;@c.Username&quot;?');">
                            <button type="submit" class="btn-ghost btn-ghost--danger" title="Eliminar"><i class="bi bi-trash"></i></button>
                        </form>
                    </td>
                </tr>
            }
            @if (Model.Capturadores.Count == 0)
            {
                <tr><td colspan="4" class="text-muted">Sin capturadores todavía.</td></tr>
            }
        </tbody>
    </table>

    <div id="drawer-capturador" class="drawer-overlay @(formularioAbierto ? "is-open" : "")" data-drawer>
        <aside class="drawer-panel @(formularioAbierto ? "is-open" : "")">
            <div class="drawer-header">
                <h3>@(Model.Editando is null ? "Nuevo capturador" : "Editar capturador")</h3>
                <button type="button" class="btn-ghost" data-drawer-close aria-label="Cerrar"><i class="bi bi-x-lg"></i></button>
            </div>
            <form asp-page="./Index" asp-page-handler="Guardar" asp-route-editando="@Model.Editando" method="post">
                <div class="drawer-body">
                    <div asp-validation-summary="All" class="text-danger mb-3"></div>
                    <div class="mb-3">
                        <label asp-for="Input.Username" class="form-label">Usuario</label>
                        <input asp-for="Input.Username" class="form-control" />
                    </div>
                    <div class="mb-3">
                        <label asp-for="Input.FullName" class="form-label">Nombre completo</label>
                        <input asp-for="Input.FullName" class="form-control" />
                    </div>
                    <div class="mb-3">
                        <label asp-for="Input.Password" class="form-label">Contraseña</label>
                        <input asp-for="Input.Password" type="password" class="form-control" placeholder="@(Model.Editando is null ? "" : "Dejar en blanco para no cambiarla")" />
                    </div>
                    <div class="mb-3 form-check">
                        <input asp-for="Input.IsActive" type="checkbox" class="form-check-input" />
                        <label asp-for="Input.IsActive" class="form-check-label">Activo</label>
                    </div>
                </div>
                <div class="drawer-footer">
                    <button type="button" class="btn-secondary" data-drawer-close>Cancelar</button>
                    <button type="submit" class="btn-primary">@(Model.Editando is null ? "Crear" : "Guardar")</button>
                </div>
            </form>
        </aside>
    </div>
</div>
```

- [ ] **Step 3: Agregar la entrada de menú**

En `Modulo.AuditoriaInventario/AuditoriaInventarioModule.cs`, agregar después del bloque `sectores` creado en la Task 6 (esta es la última entrada del menú):

```csharp
        yield return new MenuItemDefinition
        {
            Code = "capturadores",
            ParentCode = "raiz",
            Name = "Capturadores",
            Icon = "bi-person-badge",
            PageRoute = "/auditoria-inventario/capturadores",
            Order = 7,
        };
```

- [ ] **Step 4: Compilar (sin tests dedicados -- ver Global Constraints)**

Run: `dotnet build Modulo.AuditoriaInventario/Modulo.AuditoriaInventario.csproj`
Expected: Build succeeded, 0 errores.

- [ ] **Step 5: Correr toda la suite completa una última vez**

Run: `dotnet test Modulo.AuditoriaInventario.Tests/Modulo.AuditoriaInventario.Tests.csproj`
Expected: todos los tests en PASS -- 3 existentes (`AuditoriaInventarioDbContextTests`) + 12 nuevos (Task 1: 3 `PasswordHasherTests`, Task 2: 2 `DiferenciaEngineTests`, Task 3: 4 `CongeladoExcelParserTests`, Task 4: 3 `AjusteServiceTests`) = 15 tests en total.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(auditoria-inventario): página de administración de Capturadores"
```

---

## Fuera de alcance de este plan (siguiente sesión)

- `Product.SapMaterialCode` (agregado en la Task 4) no tiene pantalla de administración
  -- `PENDIENTE.md` punto 7 solo pide catálogo de Sucursales/Sectores/Capturadores, no
  de Productos (el maestro de producto se carga por otro medio, "MANUAL" o
  "INTEGRACION", sin importador definido todavía). Hasta que exista esa pantalla o
  importador, `SapMaterialCode` solo se puede completar escribiendo directo en la
  base -- `AjusteService.AprobarAsync` rechaza correctamente la aprobación mientras
  falte (cubierto por el test `AprobarAsync_SinProductoMapeadoASap_RechazaSinCambiarEstado`
  de la Task 4), así que no es un bug, es una dependencia real para poder aprobar
  ajustes de punta a punta contra datos reales.
- Aplicar las migraciones generadas (`AddCaptureUserPasswordSalt`, `AddSapMappingColumns`, más `InitialCreate` de la sesión anterior) contra un Postgres real -- requiere levantar Docker Desktop (`docker compose up -d` en `Portal SaaS - Core`) y correr `dotnet ef database update`. SQL Server real queda fuera también (el servicio de Windows está detenido y no es el motor default del Host).
- Publicar el plugin a `artifacts/plugins/Modulo.AuditoriaInventario/1.0.0/` del Host y probarlo con `dotnet run` real (mismo verificado ya para `Modulo.Rendiciones` -- ver su PENDIENTE.md).
- Probar los endpoints Minimal API (`AuditoriaInventarioInboundEndpoints`) contra el Host real (login, sync, upload de capturas) -- la PWA de captura todavía no existe como repo, así que solo se puede probar con un cliente HTTP genérico (curl/Postman) simulando el contrato.
