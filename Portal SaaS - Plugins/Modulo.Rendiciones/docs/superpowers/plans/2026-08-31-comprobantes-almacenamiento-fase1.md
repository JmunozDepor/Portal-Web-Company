# Recompresión de comprobantes ya guardados en la BD — Etapa 1 (Implementation Plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reducir el peso que los comprobantes (fotos de boletas) ya almacenados generan en la base de datos del plugin, reescalándolos y recomprimiéndolos **en su lugar**, sin cambiar el esquema y sin tocar todavía el flujo de subida.

**Architecture:** Todo vive dentro de `Modulo.Rendiciones` (plugin externo). Dos piezas: (1) una utilidad `IReceiptImageProcessor` que reorienta por EXIF, reescala y recomprime una imagen a JPEG — detrás de una interfaz para reusarla después en el ingreso; (2) un `IHostedService` de **una sola pasada**, activable por variable de entorno, que recorre los `expense_receipts` existentes por compañía y sobreescribe los que sean imágenes grandes por su versión recomprimida. Incluye modo *dry-run* para dimensionar el ahorro sin escribir nada.

**Tech Stack:** .NET 8, EF Core 8 (motor dual Postgres/SQL Server ya resuelto en runtime), xUnit + EF Core InMemory para tests, **SkiaSharp 2.88.9** (MIT) para el procesamiento de imágenes.

## Global Constraints

- **TargetFramework:** `net8.0`. **Platforms/PlatformTarget:** `x64` (ya fijado en el `.csproj` del plugin).
- **Dependencias del plugin:** solo `PortalSaas.Abstractions`. Nunca `PortalSaas.Core` ni `PortalSaas.Host`. Paquetes NuGet nuevos permitidos.
- **Licencia de la librería de imágenes:** permisiva y sin costo para uso comercial. **SkiaSharp (MIT)**. **No usar SixLabors.ImageSharp** (Six Labors Split License: licencia paga para empresas sobre el umbral de facturación — este producto es vendible).
- **Idioma:** comentarios, logs y textos en español.
- **Base propia del plugin, motor dual:** cualquier consulta EF Core corre igual en Postgres y SQL Server. Nada específico de proveedor.
- **Sin cambios de esquema en esta etapa.** Cero migraciones EF Core nuevas. La recompresión reusa las columnas `content` / `size_bytes` / `mime_type` / `file_name` existentes.
- **No se toca el flujo de subida en esta etapa** (`Detalle.cshtml.cs`, `Importar.cshtml.cs`). Los archivos nuevos siguen entrando como hoy hasta la Etapa 2.
- **La recompresión sobreescribe `content` de forma irreversible.** El plan exige *dry-run* primero y backup de la BD antes de la corrida real (ver Task 5). Si la versión recomprimida no queda más chica que la original, se deja la original.
- **`IHostedService` per-company:** mismo patrón que `RendicionesReminderBackgroundService` — resolver un `RendicionesDbContext` manual por compañía vía `IExternalDatabaseConnectionService`, nunca el registrado por DI (ese depende de `ICurrentCompanyAccessor`, que exige un request HTTP en curso).
- **`CopyLocalLockFileAssemblies=true`** ya está en el `.csproj` — el asset nativo de SkiaSharp (`libSkiaSharp`) debe quedar físicamente en el output del plugin.
- **Commits frecuentes**, uno por tarea como mínimo.

---

## File Structure

**Nuevos:**

| Archivo | Responsabilidad |
|---|---|
| `src/Modulo.Rendiciones/Servicios/IReceiptImageProcessor.cs` | Contrato: normalizar una imagen (reorientar EXIF, reescalar, recomprimir a JPEG). PDF y no-imágenes pasan sin tocar. |
| `src/Modulo.Rendiciones/Servicios/SkiaReceiptImageProcessor.cs` | Implementación con SkiaSharp. Única clase que referencia SkiaSharp. |
| `src/Modulo.Rendiciones/Servicios/ReceiptRecompressionStartupService.cs` | `IHostedService` de una sola pasada (activable por env): recomprime comprobantes existentes grandes. |
| `src/Modulo.Rendiciones.Tests/Servicios/SkiaReceiptImageProcessorTests.cs` | Tests puros del procesador (reescala, reorienta, passthrough PDF). |
| `src/Modulo.Rendiciones.Tests/Servicios/ReceiptRecompressionTests.cs` | Tests de la lógica de selección/idempotencia. |

**Modificados:**

| Archivo | Cambio |
|---|---|
| `src/Modulo.Rendiciones/Modulo.Rendiciones.csproj` | `PackageReference` a `SkiaSharp` + `SkiaSharp.NativeAssets.Win32`. |
| `src/Modulo.Rendiciones/ModuloRendiciones.cs` | Registrar `IReceiptImageProcessor` y el `IHostedService` de recompresión. |
| `PENDIENTE.md` | Registrar lo entregado y lo que queda para etapas siguientes. |

---

## Task 1: Agregar SkiaSharp y el contrato `IReceiptImageProcessor`

**Files:**
- Modify: `src/Modulo.Rendiciones/Modulo.Rendiciones.csproj`
- Create: `src/Modulo.Rendiciones/Servicios/IReceiptImageProcessor.cs`

**Interfaces:**
- Produces:
  - `record ProcessedReceipt(string FileName, string MimeType, byte[] Content)`
  - `interface IReceiptImageProcessor` con
    `Task<ProcessedReceipt> ProcessAsync(string fileName, string mimeType, byte[] content, int? maxLongEdgePx = null, int? jpegQuality = null, CancellationToken ct = default)`
  - (los defaults `maxLongEdgePx`/`jpegQuality` = null → la implementación usa sus constantes; parametrizables para poder tunear la corrida sin recompilar)

- [ ] **Step 1: Agregar los paquetes NuGet**

En `src/Modulo.Rendiciones/Modulo.Rendiciones.csproj`, dentro del mismo `<ItemGroup>` que tiene `Azure.AI.DocumentIntelligence`, agregar:

```xml
    <!-- Normalización de imágenes de comprobantes (reescalar/recomprimir).
         SkiaSharp = licencia MIT (apta para producto comercial, a diferencia de
         SixLabors.ImageSharp). El asset nativo Win32 se copia al output por
         CopyLocalLockFileAssemblies=true; para el futuro SaaS Linux agregar además
         SkiaSharp.NativeAssets.Linux.NoDependencies. -->
    <PackageReference Include="SkiaSharp" Version="2.88.9" />
    <PackageReference Include="SkiaSharp.NativeAssets.Win32" Version="2.88.9" />
```

- [ ] **Step 2: Restaurar y compilar**

Run: `dotnet build "src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: PASS (0 errores). En `src/Modulo.Rendiciones/bin/x64/Debug/net8.0/runtimes/win-x64/native/` debe aparecer `libSkiaSharp.dll`.

- [ ] **Step 3: Crear el contrato**

Crear `src/Modulo.Rendiciones/Servicios/IReceiptImageProcessor.cs`:

```csharp
namespace Modulo.Rendiciones.Servicios;

/// <summary>Comprobante ya normalizado y listo para persistir.</summary>
public sealed record ProcessedReceipt(string FileName, string MimeType, byte[] Content);

/// <summary>
/// Normaliza el archivo de un comprobante: las imágenes se reorientan por EXIF, se
/// reescalan a un lado máximo razonable para leer una boleta y se recomprimen a
/// JPEG -- una foto de celular de 8 MB baja a ~300-600 KB. Los PDF y cualquier tipo
/// no reconocido se devuelven tal cual (esta etapa no toca PDF).
/// </summary>
public interface IReceiptImageProcessor
{
    /// <summary>
    /// Devuelve el archivo normalizado. Si mimeType no es una imagen raster soportada
    /// (jpeg/png/webp), devuelve fileName/mimeType/content sin cambios.
    /// maxLongEdgePx / jpegQuality en null usan los valores por defecto de la
    /// implementación; se pueden pasar para tunear una corrida puntual.
    /// </summary>
    Task<ProcessedReceipt> ProcessAsync(
        string fileName, string mimeType, byte[] content,
        int? maxLongEdgePx = null, int? jpegQuality = null, CancellationToken ct = default);
}
```

- [ ] **Step 4: Compilar**

Run: `dotnet build "src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add "src/Modulo.Rendiciones/Modulo.Rendiciones.csproj" "src/Modulo.Rendiciones/Servicios/IReceiptImageProcessor.cs"
git commit -m "chore(rendiciones): agregar SkiaSharp y contrato IReceiptImageProcessor"
```

---

## Task 2: Implementar `SkiaReceiptImageProcessor` (TDD)

**Files:**
- Create: `src/Modulo.Rendiciones/Servicios/SkiaReceiptImageProcessor.cs`
- Test: `src/Modulo.Rendiciones.Tests/Servicios/SkiaReceiptImageProcessorTests.cs`

**Interfaces:**
- Consumes: `IReceiptImageProcessor`, `ProcessedReceipt` (Task 1).
- Produces: `class SkiaReceiptImageProcessor : IReceiptImageProcessor` con
  `public const int DefaultMaxLongEdgePx = 2200;` y `public const int DefaultJpegQuality = 78;`.

**Reglas de decisión fijadas:**
- MIME raster soportados: `image/jpeg`, `image/jpg`, `image/png`, `image/webp`. Cualquier otro (incl. `application/pdf`, `image/heic`) → passthrough sin cambios.
- Si es imagen: decodificar, aplicar orientación EXIF (`SKCodec.EncodedOrigin`), si el lado largo > `maxLongEdgePx` reescalar manteniendo proporción, reencodear a JPEG calidad `jpegQuality`. Nombre de salida: mismo `fileName` con extensión `.jpg`; MIME de salida `image/jpeg`.
- Si el decode falla (archivo corrupto / no es realmente imagen) → devolver el contenido original sin tocar.
- Si el resultado quedara **≥** el tamaño original → devolver el original.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `src/Modulo.Rendiciones.Tests/Servicios/SkiaReceiptImageProcessorTests.cs`:

```csharp
using Modulo.Rendiciones.Servicios;
using SkiaSharp;

namespace Modulo.Rendiciones.Tests.Servicios;

public class SkiaReceiptImageProcessorTests
{
    private static byte[] MakePng(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task ProcessAsync_downscales_large_image_and_returns_jpeg()
    {
        var sut = new SkiaReceiptImageProcessor();
        var big = MakePng(4000, 3000);

        var result = await sut.ProcessAsync("boleta.png", "image/png", big);

        Assert.Equal("image/jpeg", result.MimeType);
        Assert.EndsWith(".jpg", result.FileName);
        Assert.True(result.Content.Length < big.Length);

        using var codec = SKCodec.Create(new MemoryStream(result.Content));
        Assert.NotNull(codec);
        Assert.True(Math.Max(codec!.Info.Width, codec.Info.Height) <= SkiaReceiptImageProcessor.DefaultMaxLongEdgePx);
    }

    [Fact]
    public async Task ProcessAsync_respects_explicit_max_edge_override()
    {
        var sut = new SkiaReceiptImageProcessor();
        var big = MakePng(4000, 3000);

        var result = await sut.ProcessAsync("b.png", "image/png", big, maxLongEdgePx: 1000, jpegQuality: 70);

        using var codec = SKCodec.Create(new MemoryStream(result.Content));
        Assert.True(Math.Max(codec!.Info.Width, codec.Info.Height) <= 1000);
    }

    [Fact]
    public async Task ProcessAsync_leaves_pdf_untouched()
    {
        var sut = new SkiaReceiptImageProcessor();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

        var result = await sut.ProcessAsync("boleta.pdf", "application/pdf", bytes);

        Assert.Equal("boleta.pdf", result.FileName);
        Assert.Equal("application/pdf", result.MimeType);
        Assert.Equal(bytes, result.Content);
    }

    [Fact]
    public async Task ProcessAsync_returns_original_when_bytes_are_not_a_real_image()
    {
        var sut = new SkiaReceiptImageProcessor();
        var junk = new byte[] { 1, 2, 3, 4, 5 };

        var result = await sut.ProcessAsync("x.jpg", "image/jpeg", junk);

        Assert.Equal(junk, result.Content);
    }

    [Fact]
    public async Task ProcessAsync_returns_original_when_recompressed_is_not_smaller()
    {
        var sut = new SkiaReceiptImageProcessor();
        var tiny = MakePng(8, 8); // ya minúsculo: el JPEG no va a quedar más chico

        var result = await sut.ProcessAsync("t.png", "image/png", tiny);

        Assert.Equal(tiny, result.Content);
        Assert.Equal("image/png", result.MimeType);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test "src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj" --filter "FullyQualifiedName~SkiaReceiptImageProcessorTests"`
Expected: FAIL — no compila ("SkiaReceiptImageProcessor" no existe).

- [ ] **Step 3: Implementar `SkiaReceiptImageProcessor`**

Crear `src/Modulo.Rendiciones/Servicios/SkiaReceiptImageProcessor.cs`:

```csharp
using SkiaSharp;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Implementación de IReceiptImageProcessor con SkiaSharp (MIT). Única clase del
/// plugin que referencia SkiaSharp -- si algún día cambia la librería, se reemplaza
/// solo este archivo.
/// </summary>
public sealed class SkiaReceiptImageProcessor : IReceiptImageProcessor
{
    /// <summary>Lado largo máximo tras reescalar. Suficiente para leer una boleta.</summary>
    public const int DefaultMaxLongEdgePx = 2200;

    /// <summary>Calidad JPEG de salida por defecto.</summary>
    public const int DefaultJpegQuality = 78;

    private static readonly HashSet<string> RasterMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp",
    };

    public Task<ProcessedReceipt> ProcessAsync(
        string fileName, string mimeType, byte[] content,
        int? maxLongEdgePx = null, int? jpegQuality = null, CancellationToken ct = default)
    {
        var mime = (mimeType ?? "").Trim();
        if (!RasterMimes.Contains(mime))
            return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

        var maxEdge = maxLongEdgePx is > 0 ? maxLongEdgePx.Value : DefaultMaxLongEdgePx;
        var quality = jpegQuality is >= 1 and <= 100 ? jpegQuality.Value : DefaultJpegQuality;

        try
        {
            using var input = new MemoryStream(content, writable: false);
            using var codec = SKCodec.Create(input);
            if (codec is null)
                return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

            using var original = SKBitmap.Decode(codec);
            if (original is null)
                return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

            using var oriented = ApplyOrientation(original, codec.EncodedOrigin);
            using var scaled = Downscale(oriented, maxEdge);

            using var image = SKImage.FromBitmap(scaled);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            var jpegBytes = encoded.ToArray();

            if (jpegBytes.Length >= content.Length)
                return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

            var newName = Path.ChangeExtension(fileName, ".jpg");
            return Task.FromResult(new ProcessedReceipt(newName, "image/jpeg", jpegBytes));
        }
        catch
        {
            // Un archivo que dice ser imagen pero no se puede decodificar no debe
            // reventar la corrida -- se deja tal cual.
            return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));
        }
    }

    private static SKBitmap ApplyOrientation(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
            return src.Copy();

        var swapWh = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var dst = new SKBitmap(swapWh ? src.Height : src.Width, swapWh ? src.Width : src.Height);
        using var canvas = new SKCanvas(dst);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-src.Width, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, src.Width / 2f, src.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -src.Height); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(dst.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(dst.Width, dst.Height); canvas.RotateDegrees(90); canvas.Scale(-1, 1); canvas.Translate(-src.Width, 0); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, dst.Height); canvas.RotateDegrees(-90); break;
        }

        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }

    private static SKBitmap Downscale(SKBitmap src, int maxLongEdge)
    {
        var longEdge = Math.Max(src.Width, src.Height);
        if (longEdge <= maxLongEdge)
            return src.Copy();

        var ratio = (double)maxLongEdge / longEdge;
        var w = Math.Max(1, (int)Math.Round(src.Width * ratio));
        var h = Math.Max(1, (int)Math.Round(src.Height * ratio));

        return src.Resize(new SKImageInfo(w, h), SKFilterQuality.Medium) ?? src.Copy();
    }
}
```

- [ ] **Step 4: Correr los tests**

Run: `dotnet test "src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj" --filter "FullyQualifiedName~SkiaReceiptImageProcessorTests"`
Expected: PASS (todos).

- [ ] **Step 5: Commit**

```bash
git add "src/Modulo.Rendiciones/Servicios/SkiaReceiptImageProcessor.cs" "src/Modulo.Rendiciones.Tests/Servicios/SkiaReceiptImageProcessorTests.cs"
git commit -m "feat(rendiciones): SkiaReceiptImageProcessor (reescala + recomprime imágenes)"
```

---

## Task 3: Registrar `IReceiptImageProcessor` en DI

**Files:**
- Modify: `src/Modulo.Rendiciones/ModuloRendiciones.cs:274` (junto al registro de `IAttachmentStorageService`)

**Interfaces:**
- Consumes: `IReceiptImageProcessor`, `SkiaReceiptImageProcessor` (Tasks 1-2).

- [ ] **Step 1: Agregar el registro**

En `src/Modulo.Rendiciones/ModuloRendiciones.cs`, inmediatamente después de:

```csharp
        services.AddScoped<IAttachmentStorageService, AttachmentStorageService>();
```

agregar:

```csharp
        services.AddSingleton<IReceiptImageProcessor, SkiaReceiptImageProcessor>();
```

- [ ] **Step 2: Compilar**

Run: `dotnet build "src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add "src/Modulo.Rendiciones/ModuloRendiciones.cs"
git commit -m "chore(rendiciones): registrar IReceiptImageProcessor en DI"
```

---

## Task 4: `ReceiptRecompressionStartupService` — pasada única (TDD)

**Files:**
- Create: `src/Modulo.Rendiciones/Servicios/ReceiptRecompressionStartupService.cs`
- Modify: `src/Modulo.Rendiciones/ModuloRendiciones.cs` (registrar el `IHostedService`)
- Test: `src/Modulo.Rendiciones.Tests/Servicios/ReceiptRecompressionTests.cs`

**Interfaces:**
- Consumes: `IReceiptImageProcessor` (Task 1), `IExternalDatabaseConnectionService` + `ModuleCompanyDto` + `ExternalDatabaseEngineType` (de `PortalSaas.Abstractions`), `RendicionesDbContext`, `ExpenseReceipt`.
- Produces:
  - `class ReceiptRecompressionStartupService : IHostedService`
  - `static class ReceiptRecompression` con
    `static bool ShouldRecompress(ExpenseReceipt r, long minBytes)` — true si el MIME es imagen raster (`image/jpeg`/`image/jpg`/`image/png`/`image/webp`) y `Content.Length >= minBytes`.

**Comportamiento fijado:**
- Se activa solo si `RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP` == `true` (case-insensitive). Si no, `StartAsync` retorna sin hacer nada.
- `RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN` == `true` → calcula y loguea el ahorro proyectado por compañía, **no escribe nada**.
- Overrides opcionales sin recompilar: `RENDICIONES_RECOMPRESS_MAX_EDGE` (int), `RENDICIONES_RECOMPRESS_QUALITY` (int 1-100), `RENDICIONES_RECOMPRESS_MIN_KB` (int, default 500).
- Recorre todas las compañías con Rendiciones activo (`ListActiveCompanyIdsAsync`), resolviendo un `RendicionesDbContext` manual por compañía. Un fallo en una compañía no frena las demás.
- Por compañía: pagina `expense_receipts` por `Id` ascendente en lotes de 100 (no carga todo en memoria). Por cada fila donde `ShouldRecompress` es true, llama a `ProcessAsync`; si el resultado quedó más chico, sobreescribe `Content`/`SizeBytes`/`MimeType`/`FileName`. `SaveChangesAsync` por lote.
- Idempotente: una segunda corrida encuentra los ya recomprimidos por debajo del umbral (o `ProcessAsync` devuelve algo no menor) y los saltea.
- Loguea por compañía: `{Recomprimidos} de {Revisados}, {MB} liberados` (o `[DRY-RUN]` + proyección).

- [ ] **Step 1: Escribir el test que falla**

Crear `src/Modulo.Rendiciones.Tests/Servicios/ReceiptRecompressionTests.cs`:

```csharp
using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ReceiptRecompressionTests
{
    private static ExpenseReceipt R(string mime, int len) => new()
    {
        CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(),
        FileName = "x", MimeType = mime, Content = new byte[len],
    };

    [Theory]
    [InlineData("image/jpeg", 600_000, true)]
    [InlineData("image/png", 600_000, true)]
    [InlineData("image/webp", 600_000, true)]
    [InlineData("image/jpeg", 100_000, false)]        // ya chico
    [InlineData("application/pdf", 5_000_000, false)] // PDF nunca
    [InlineData("image/heic", 5_000_000, false)]      // no raster soportado
    public void ShouldRecompress_picks_only_large_raster_images(string mime, int len, bool expected)
    {
        Assert.Equal(expected, ReceiptRecompression.ShouldRecompress(R(mime, len), minBytes: 500_000));
    }
}
```

- [ ] **Step 2: Correr el test y verlo fallar**

Run: `dotnet test "src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj" --filter "FullyQualifiedName~ReceiptRecompressionTests"`
Expected: FAIL — "The name 'ReceiptRecompression' does not exist".

- [ ] **Step 3: Implementar el helper + el `IHostedService`**

Crear `src/Modulo.Rendiciones/Servicios/ReceiptRecompressionStartupService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Data;
using Modulo.Rendiciones.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.Rendiciones.Servicios;

public static class ReceiptRecompression
{
    private static readonly HashSet<string> RasterMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp",
    };

    public static bool ShouldRecompress(ExpenseReceipt r, long minBytes) =>
        r.Content is { } c && c.Length >= minBytes && RasterMimes.Contains(r.MimeType ?? "");
}

/// <summary>
/// Pasada única de "descongestión": al arrancar el Host, si
/// RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP = true, reescala y recomprime los
/// comprobantes imagen ya guardados que superen el umbral, sobreescribiéndolos en su
/// lugar. El operador prende la variable, reinicia el Host una vez, revisa el log del
/// resumen y la vuelve a apagar. Soporta RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN=true
/// (no escribe, solo proyecta). Idempotente. Mismo patrón per-company que
/// RendicionesReminderBackgroundService (el DbContext de DI depende de un request
/// HTTP en curso, acá no hay ninguno).
/// </summary>
public sealed class ReceiptRecompressionStartupService : IHostedService
{
    private const string ModuleCode = "Rendiciones";
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IReceiptImageProcessor _imageProcessor;
    private readonly ILogger<ReceiptRecompressionStartupService> _logger;

    public ReceiptRecompressionStartupService(
        IServiceScopeFactory scopeFactory,
        IReceiptImageProcessor imageProcessor,
        ILogger<ReceiptRecompressionStartupService> logger)
    {
        _scopeFactory = scopeFactory;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (!EnvBool("RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP"))
            return;

        var dryRun = EnvBool("RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN");
        var minBytes = (long)EnvInt("RENDICIONES_RECOMPRESS_MIN_KB", 500) * 1024;
        int? maxEdge = EnvIntOrNull("RENDICIONES_RECOMPRESS_MAX_EDGE");
        int? quality = EnvIntOrNull("RENDICIONES_RECOMPRESS_QUALITY");

        _logger.LogInformation(
            "Recompresión de comprobantes: iniciando{DryRun} (min {MinKB} KB, maxEdge {MaxEdge}, calidad {Quality}).",
            dryRun ? " [DRY-RUN]" : "", minBytes / 1024,
            maxEdge?.ToString() ?? "default", quality?.ToString() ?? "default");

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var externalDb = scope.ServiceProvider.GetRequiredService<IExternalDatabaseConnectionService>();
            var companies = await externalDb.ListActiveCompanyIdsAsync(ModuleCode, ct);

            foreach (var company in companies)
            {
                try
                {
                    await ProcessCompanyAsync(company, externalDb, dryRun, minBytes, maxEdge, quality, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recompresión de comprobantes: falló la compañía {CompanyId} -- se sigue con las demás.", company.CompanyId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recompresión de comprobantes: fallo general -- se omite en este arranque.");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task ProcessCompanyAsync(
        ModuleCompanyDto company, IExternalDatabaseConnectionService externalDb,
        bool dryRun, long minBytes, int? maxEdge, int? quality, CancellationToken ct)
    {
        var connection = await externalDb.ResolveConnectionAsync(ModuleCode, company.CompanyId, ct);

        var optionsBuilder = new DbContextOptionsBuilder<RendicionesDbContext>();
        switch (connection.EngineType)
        {
            case ExternalDatabaseEngineType.Postgres:
                optionsBuilder.UseNpgsql(connection.ConnectionString);
                break;
            case ExternalDatabaseEngineType.SqlServer:
                optionsBuilder.UseSqlServer(connection.ConnectionString);
                break;
            default:
                throw new InvalidOperationException($"Motor de base de datos externa no soportado: '{connection.EngineType}'.");
        }

        await using var db = new RendicionesDbContext(optionsBuilder.Options);

        int reviewed = 0, hit = 0;
        long saved = 0;
        long lastId = 0;

        while (!ct.IsCancellationRequested)
        {
            var batch = await db.ExpenseReceipts
                .Where(r => r.CompanyId == company.CompanyId && r.Id > lastId)
                .OrderBy(r => r.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            lastId = batch[^1].Id;

            foreach (var r in batch)
            {
                reviewed++;
                if (!ReceiptRecompression.ShouldRecompress(r, minBytes))
                    continue;

                var processed = await _imageProcessor.ProcessAsync(r.FileName, r.MimeType, r.Content, maxEdge, quality, ct);
                if (processed.Content.Length >= r.Content.Length)
                    continue;

                saved += r.Content.Length - processed.Content.Length;
                hit++;

                if (dryRun)
                    continue;

                r.FileName = processed.FileName;
                r.MimeType = processed.MimeType;
                r.Content = processed.Content;
                r.SizeBytes = processed.Content.Length;
            }

            if (!dryRun)
                await db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Recompresión de comprobantes (compañía {CompanyId}){DryRun}: {Hit} de {Reviewed}, {SavedMB:N1} MB {Verbo}.",
            company.CompanyId, dryRun ? " [DRY-RUN]" : "", hit, reviewed,
            saved / (1024d * 1024d), dryRun ? "liberables" : "liberados");
    }

    private static bool EnvBool(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "true", StringComparison.OrdinalIgnoreCase);

    private static int EnvInt(string name, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : fallback;

    private static int? EnvIntOrNull(string name) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) && v > 0 ? v : null;
}
```

- [ ] **Step 4: Correr el test**

Run: `dotnet test "src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj" --filter "FullyQualifiedName~ReceiptRecompressionTests"`
Expected: PASS.

- [ ] **Step 5: Registrar el `IHostedService`**

En `src/Modulo.Rendiciones/ModuloRendiciones.cs`, después de:

```csharp
        services.AddHostedService<RendicionesReminderBackgroundService>();
```

agregar:

```csharp
        services.AddHostedService<ReceiptRecompressionStartupService>();
```

- [ ] **Step 6: Compilar el plugin**

Run: `dotnet build "src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add "src/Modulo.Rendiciones/Servicios/ReceiptRecompressionStartupService.cs" "src/Modulo.Rendiciones/ModuloRendiciones.cs" "src/Modulo.Rendiciones.Tests/Servicios/ReceiptRecompressionTests.cs"
git commit -m "feat(rendiciones): recompresión de una pasada de comprobantes existentes (env + dry-run)"
```

---

## Task 5: Verificación y corrida controlada

**Files:** ninguno (verificación y operación).

- [ ] **Step 1: Suite completa de tests del plugin**

Run: `dotnet test "src/Modulo.Rendiciones.Tests/Modulo.Rendiciones.Tests.csproj"`
Expected: PASS — todos los previos + `SkiaReceiptImageProcessorTests` + `ReceiptRecompressionTests`.

- [ ] **Step 2: Build Release y verificación del asset nativo**

Run: `dotnet build -c Release "src/Modulo.Rendiciones/Modulo.Rendiciones.csproj"`
Expected: PASS (0 warnings / 0 errores).
Verificar: existe `src/Modulo.Rendiciones/bin/x64/Release/net8.0/runtimes/win-x64/native/libSkiaSharp.dll` **y** que el target `PublicarComoPlugin` lo copió a `dist/Modulo.Rendiciones/1.0.0/runtimes/win-x64/native/libSkiaSharp.dll`.

- [ ] **Step 3: Copiar el plugin al portal y levantar el Host (sin activar la recompresión)**

Copiar `dist/Modulo.Rendiciones/1.0.0/` a `artifacts/plugins/Modulo.Rendiciones/1.0.0/` del repo `Portal SaaS - Core` (mismo procedimiento que las entregas anteriores, ver `PENDIENTE.md`).

Run (desde `Portal SaaS - Core`, **sin** ninguna variable `RENDICIONES_RECOMPRESS_*` seteada): `dotnet run --project src/PortalSaas.Host --launch-profile https`
Expected: en el log, `Módulo Rendiciones v1.0.0 cargado (15 entradas de menú)` **sin** `ReflectionTypeLoadException` ni `TypeLoadException` (si SkiaSharp no resolviera el nativo bajo el `AssemblyLoadContext` del plugin, el crash aparece acá). No debe aparecer ninguna línea de "Recompresión de comprobantes".

- [ ] **Step 4: DRY-RUN contra la BD real**

1. Medir el tamaño actual:
   - Postgres: `SELECT count(*) AS filas, pg_size_pretty(sum(octet_length(content))) AS total FROM expense_receipts;`
   - SQL Server: `SELECT COUNT(*) AS filas, SUM(DATALENGTH(content)) AS total_bytes FROM expense_receipts;`
2. Detener el Host. Setear `RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP=true` y `RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN=true`.
3. Levantar el Host una vez. En el log debe aparecer, por compañía:
   `Recompresión de comprobantes (compañía ...) [DRY-RUN]: {Hit} de {Reviewed}, {X} MB liberables.`
4. Revisar que `{X} MB liberables` sea significativo y que `filas`/`total` de la BD **no cambiaron** (dry-run no escribe).
5. Detener el Host.

- [ ] **Step 5: Backup + corrida real (ambiente de prueba primero, luego producción)**

1. **Backup de la BD del plugin** (la recompresión sobreescribe `content` de forma irreversible).
2. Quitar `RENDICIONES_RECOMPRESS_RECEIPTS_DRY_RUN` (o ponerla en `false`). Dejar `RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP=true`.
3. Levantar el Host una vez. Log esperado por compañía:
   `Recompresión de comprobantes (compañía ...): {Hit} de {Reviewed}, {X} MB liberados.`
4. Volver a medir el tamaño (mismas queries del Step 4) → debe bajar acorde a lo proyectado.
5. **Apagar** `RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP` (sin setear o `false`) y reiniciar el Host. En este arranque no debe correr nada.
6. Abrir el visor de un par de comprobantes recomprimidos (`/rendiciones/gastos/comprobante/{id}`) → se ven completos, legibles y con la orientación correcta.

- [ ] **Step 6: Verificar idempotencia**

Volver a activar `RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP=true` (sin dry-run) y reiniciar una vez más → el log debe reportar `0 de {Reviewed}` (o un puñado marginal), sin cambio de tamaño. Apagar la variable de nuevo.

- [ ] **Step 7: Actualizar `PENDIENTE.md` y commit**

Agregar a `PENDIENTE.md` una sección "Etapa 1 — recompresión de comprobantes en BD" con:
- lo entregado (`IReceiptImageProcessor` + `SkiaReceiptImageProcessor`, `ReceiptRecompressionStartupService` con dry-run y overrides por env),
- las variables de entorno y el procedimiento de corrida,
- el resultado real (MB liberados) tras la corrida en producción,
- lo que queda para las etapas siguientes (ver abajo).

```bash
git add PENDIENTE.md
git commit -m "docs(rendiciones): registrar Etapa 1 (recompresión de comprobantes en BD)"
```

---

## Fuera de esta etapa (etapas siguientes, NO implementar acá)

- **Etapa 2 — normalizar al subir:** aplicar `IReceiptImageProcessor` + validación de tipo MIME + tope de tamaño en `Detalle.cshtml.cs` e `Importar.cshtml.cs`, para que los archivos nuevos ya entren recomprimidos. El `IReceiptImageProcessor` de esta etapa queda listo para reusar.
- **Etapa 3 — sacar el binario de la BD:** mover `content` a un directorio / object storage (filesystem, MinIO, o S3/R2/B2) detrás de la misma `IAttachmentStorageService`; agregar `storage_key` + `content_hash` (migración de esquema), dedup por hash, y opcionalmente el modelo híbrido caliente-en-BD / frío-afuera. Requiere coordinación de backups.
- **Limpieza de comprobantes huérfanos:** `BackgroundService` que borra `expense_receipts` sin línea que los referencie y con `uploaded_at` viejo. Independiente; se puede hacer en cualquier etapa.
- **No traer el blob al editar un gasto:** `IExpenseService.GetAsync` hace `.Include(d => d.ExpenseReceipt)` y trae la columna binaria aunque el flujo de edición solo use `ExpenseReceiptId`. Optimización menor para más adelante.

---

## Notas de despliegue

- **Migraciones EF Core:** esta etapa **no genera ninguna**. No se toca `Modulo.Rendiciones.Migrations.Postgres` / `.SqlServer` ni se aplica nada contra bases reales.
- **El `ReceiptRecompressionStartupService` no hace nada** hasta que alguien setee `RENDICIONES_RECOMPRESS_RECEIPTS_ON_STARTUP=true` — decisión y ventana del operador. Con la variable apagada, el plugin actualizado se comporta exactamente como antes.
- **Orden recomendado de corrida:** dry-run → revisar proyección → backup → corrida real en ambiente de prueba → corrida real en producción → apagar la variable.
- **SaaS Linux (futuro):** agregar `SkiaSharp.NativeAssets.Linux.NoDependencies` al `.csproj` antes de correr en Linux. No hace falta para el despliegue on-premise (Windows).

---

## Self-Review

**Cobertura del alcance acordado (solo recomprimir lo que está en la BD):**
- Utilidad de reescalado/recompresión de imágenes → Tasks 1-2. ✅
- Registro en DI para poder usarla desde el hosted service → Task 3. ✅
- Pasada única sobre `expense_receipts` existentes, sobreescribiendo en su lugar → Task 4. ✅
- Sin tocar el flujo de subida → confirmado, ninguna task modifica `Detalle.cshtml.cs` / `Importar.cshtml.cs`. ✅
- Sin cambios de esquema → confirmado, ninguna task agrega columnas ni migraciones. ✅
- Rollout cauteloso (dry-run + backup + idempotencia) → Task 4 (dry-run, overrides) + Task 5 (procedimiento). ✅
- Librería con licencia apta para producto comercial → Global Constraints + Task 1 (SkiaSharp MIT). ✅
- "Subir archivos a un directorio" explícitamente diferido → sección "Fuera de esta etapa", Etapa 3. ✅

**Placeholders:** revisado — todos los pasos de código traen contenido real; los tests, aserciones concretas. Sin "TODO" / "similar a Task N".

**Consistencia de tipos:**
- `ProcessedReceipt` / `IReceiptImageProcessor.ProcessAsync(string, string, byte[], int?, int?, CancellationToken)` — definido en Task 1, usado con la misma firma en Tasks 2 y 4 (los tests de Task 2 pasan overrides posicionales/nombrados que coinciden).
- `SkiaReceiptImageProcessor.DefaultMaxLongEdgePx` / `DefaultJpegQuality` — definidos en Task 2, referenciados por sus tests.
- `ReceiptRecompression.ShouldRecompress(ExpenseReceipt, long)` — misma firma en Task 4 (impl + test).
- Patrón per-company (`ListActiveCompanyIdsAsync` / `ResolveConnectionAsync` / `ExternalDatabaseEngineType` / `ModuleCompanyDto`) — idéntico al de `RendicionesReminderBackgroundService` ya en el repo.
- `ExpenseReceipt` tiene `Content` (`byte[]`), `SizeBytes` (`int`), `MimeType` (`string`), `FileName` (`string`), `Id` (`long`), `CompanyId` (`Guid`) — coincide con `src/Modulo.Rendiciones/Models/ExpenseReceipt.cs`.
