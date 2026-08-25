# Ingesta SQL directa a staging completo (Items) — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar el flujo Bajada de Items (hoy limitado a 3 campos vía SAP Service
Layer) por un conector SQL directo (HANA/SQL Server) que trae el set completo de campos
que usaba el legado, con diff por valor (no por fecha) para decidir cuándo reenviar a
WMS Cloud, y un XML de Subida armado dinámicamente a partir de esos campos.

**Architecture:** Ver spec completo en
`docs/superpowers/specs/2026-08-21-ingesta-sql-directa-staging-items-design.md`. Resumen:
nuevo `ConectorTipo=Sql` (reutiliza `IHanaService`, ya existente) → `wms_sap_stage_item`
gana `extra_fields JSONB` → `WmsSapStageItemWriter` compara valores contra una lista
configurable en `wms_validation_fields` antes de marcar `Pendiente` → `WmsCloudConnector`
arma el XML iterando dinámicamente el registro completo.

**Tech Stack:** .NET 8, EF Core (Postgres + SqlServer, motor dual), `Sap.Data.Hana` /
`Microsoft.Data.SqlClient` (vía `IHanaService` ya existente), xUnit.

## Global Constraints

- Alcance de esta ronda: **solo Item**. No tocar Store/Traslado/Order.
- Staging sigue en nuestra base propia (Postgres/SQL Server) — nunca HANA.
- El motor de polling/cursor/timeout/batching de `IntegrationSyncHostedService` ya
  existe y NO se toca en este plan salvo donde se indique explícitamente.
- Todo cambio de esquema necesita migración para **ambos** proveedores
  (`Modulo.Wms.Migrations.Postgres` y `Modulo.Wms.Migrations.SqlServer`), mismo patrón
  ya usado en el resto del proyecto.
- Antes de cada `dotnet build`/`dotnet test` del plugin, verificar que `PortalSaas.Host`
  no esté corriendo (bloquea los DLLs) — usar
  `Get-CimInstance Win32_Process -Filter "Name='PortalSaas.Host.exe'" | Stop-Process -Force`
  si hace falta.
- Tras cualquier cambio a `IIntegrationConnector` o a cualquier tipo público consumido
  por `PortalSaas.Core`, republicar el plugin (`Portal SaaS - Plugins/Modulo.Wms/publish-dist.ps1`)
  antes de levantar el Host — `build-all.ps1` NO reconstruye `Modulo.Wms`.
- No commitear salvo pedido explícito del usuario.

---

### Task 1: Verificar `InvntItem`/`CodeBars` como columnas SQL directas contra HANA real

**Files:**
- Ninguno (spike de verificación, no toca código de producción).

**Interfaces:**
- Consumes: `IHanaService` (ya existe, `Portal SaaS - Core/src/PortalSaas.Core/Sap/HanaService.cs`).
- Produces: confirmación escrita (en el reporte de esta tarea) de si la query de
  referencia del spec corre sin error contra el HANA real de "Depor (Testing)".

- [ ] **Step 1: Armar un proyecto scratch de verificación**

Reutilizar el patrón ya usado en la sesión anterior (`wms-check5`): un `dotnet new
console` en el scratchpad, con referencia a `PortalSaas.Data` y al paquete
`Sap.Data.Hana` (mismo que usa `HanaService`).

- [ ] **Step 2: Ejecutar la query de referencia del spec contra HANA real**

```csharp
using Sap.Data.Hana;

var connString = "Server=<host>:<port>;UserID=<user>;Password=<pass>;Current Schema=<schema>";
using var conn = new HanaConnection(connString);
await conn.OpenAsync();

var sql = """
    SELECT
        UPPER(T0."ItemCode") AS "item_alternate_code",
        UPPER(REPLACE(T0."ItemName",'&','-')) AS "description",
        T0."CodeBars" AS "barcode",
        T0."BLength1" AS "unit_length",
        T0."BWidth1" AS "unit_width",
        T0."BHeight1" AS "unit_height",
        UPPER(TO_VARCHAR(T0."U_GSP_Season")) AS "season_code",
        UPPER(T2."ItmsGrpNam") AS "brand_code",
        UPPER(T2."ItmsGrpNam") AS "hierarchy1_code",
        UPPER(IFNULL(T3."Name",'')) AS "hierarchy2_code",
        UPPER(T0."U_GSP_SECTION") AS "hierarchy2_description",
        UPPER(T0."U_GSP_REFERENCE") AS "external_style",
        UPPER(SUBSTRING(REPLACE(T0."ItemName",'&','-'),1,30)) AS "short_descr",
        UPPER(IFNULL(T4."U_putaway_type",'')) AS "putaway_type",
        T0."UpdateDate" AS "SourceUpdateDate"
    FROM "OITM" T0
        INNER JOIN "OITB" T2 ON T0."ItmsGrpCod" = T2."ItmsGrpCod"
        LEFT JOIN "@GSP_BSSECCION" T3 ON T3."Code" = T0."U_GSP_SECTION"
        LEFT JOIN "@NX_PUTAWAY_TYPE" T4 ON T4."U_ItmsGrpNam" = T2."ItmsGrpNam" AND T4."U_GSP_SECTION" = T3."Name"
    WHERE T0."U_NX_EnviarWMS" = 'Y'
        AND T0."InvntItem" = 'Y'
        AND IFNULL(NULLIF(T0."CodeBars", '0'), '0') != '0'
    LIMIT 5
    """;

using var cmd = new HanaCommand(sql, conn);
using var reader = await cmd.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    for (var i = 0; i < reader.FieldCount; i++)
        Console.Write($"{reader.GetName(i)}={reader.GetValue(i)} | ");
    Console.WriteLine();
}
```

Credenciales: tomar `Company.Instance` de "Depor (Testing)" desde `ps_comdepor`
(`Instances`/`Companies` tables), desencriptando `TechnicalSecretKey` con el mismo
mecanismo AES-GCM ya usado toda la sesión anterior.

- [ ] **Step 3: Documentar el resultado**

Si la query corre sin error y trae filas con datos coherentes (barcode no vacío, joins
resolviendo), queda confirmado que SQL directo acepta `InvntItem`/`CodeBars` como
columnas reales — anotarlo en el reporte de la tarea. Si falla, ajustar la query de
referencia (documentarlo también) antes de continuar con la Tarea 3.

- [ ] **Step 4: Sin commit** (nada de código de producción cambia en esta tarea).

---

### Task 2: `IHanaService.QueryDynamicAsync`

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IHanaService.cs`
- Modify: `Portal SaaS - Core/src/PortalSaas.Core/Sap/HanaService.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Sap/HanaServiceTests.cs` (crear si no existe)

**Interfaces:**
- Consumes: nada nuevo — mismo `ResolveConnectionAsync`/`HanaToSqlServerTranslator` ya
  usados por `QueryAsync<T>`.
- Produces: `Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(string sql, object? parametros, CancellationToken ct)`
  — usado por `SqlDirectConnector` (Task 3).

- [ ] **Step 1: Agregar el método a la interfaz**

En `IHanaService.cs`, junto a `QueryAsync<T>`:

```csharp
/// <summary>
/// Igual que QueryAsync&lt;T&gt; pero sin DTO fijo -- cada fila se devuelve como
/// diccionario columna→valor, para conectores que no conocen de antemano el set de
/// columnas (ver SqlDirectConnector, Ronda de ingesta SQL directa a staging).
/// </summary>
Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
    string sqlParametrizado, object? parametros = null, CancellationToken ct = default);
```

- [ ] **Step 2: Implementar en `HanaService`**

Mismo patrón dual-motor que `QueryAsync<T>` (líneas 44-83 del archivo actual), pero sin
`RowReflectionMapper` — mapea cada fila leyendo `reader.GetName(i)`/`reader.GetValue(i)`
directo a un `Dictionary<string, object?>`:

```csharp
public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
    string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
{
    var connection = await ResolveConnectionAsync(ct);
    var result = new List<IReadOnlyDictionary<string, object?>>();

    if (connection.EngineType == SapEngineType.SqlServer)
    {
        var translatedSql = HanaToSqlServerTranslator.Translate(sqlParametrizado);
        using var conn = new SqlConnection(connection.ConnectionString);
        using var cmd = new SqlCommand(translatedSql, conn);
        AddParametersSqlServer(cmd, parametros);

        await conn.OpenAsync(ct);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(MapRowToDictionary(reader));
        }
        return result;
    }

    using var connHana = new HanaConnection(connection.ConnectionString);
    using var cmdHana = new HanaCommand(sqlParametrizado, connHana);
    AddParametersHana(cmdHana, parametros);

    await connHana.OpenAsync(ct);
    using var readerHana = (HanaDataReader)await cmdHana.ExecuteReaderAsync(ct);
    while (await readerHana.ReadAsync(ct))
    {
        result.Add(MapRowToDictionary(readerHana));
    }
    return result;
}

private static IReadOnlyDictionary<string, object?> MapRowToDictionary(System.Data.Common.DbDataReader reader)
{
    var fila = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < reader.FieldCount; i++)
    {
        fila[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
    }
    return fila;
}
```

- [ ] **Step 3: Test — fila simple**

```csharp
[Fact]
public async Task QueryDynamicAsync_FilaConVariasColumnas_DevuelveDiccionarioPorNombre()
{
    // Usa el mismo doble/fake de conexión que ya exista en HanaServiceTests para
    // QueryAsync<T> (si no existe el archivo, crear un fake mínimo de IDbConnection
    // compatible, o -- si HanaService no es testeable sin una BD real hoy -- documentar
    // en el reporte que este test se cubre solo vía Task 1 (spike contra HANA real) y
    // Task 3 (test del conector con ISapSession-equivalente fake), y omitir este test
    // unitario aislado.
}
```

Nota para quien ejecute esta tarea: si `HanaService` no tiene hoy ningún test unitario
existente (no depende de una interfaz inyectable para la conexión, usa `new
HanaConnection`/`new SqlConnection` directo), **no inventar un mock de ADO.NET** solo
para este método — dejarlo cubierto por la verificación de Task 1 (spike real) y por el
test de integración de Task 3 (que sí puede fakear en la capa de `IHanaService`, no de
`HanaService`). Documentar esta decisión en el reporte en vez de forzar un test frágil.

- [ ] **Step 4: Build + test**

```bash
cd "Portal SaaS - Core"
dotnet build PortalSaas.sln --no-restore
dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --no-build
```

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/IHanaService.cs" "Portal SaaS - Core/src/PortalSaas.Core/Sap/HanaService.cs"
git commit -m "feat: agregar IHanaService.QueryDynamicAsync para filas sin DTO fijo"
```

---

### Task 3: `IntegrationConectorTipo.Sql` + `SqlDirectConnector`

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Data/Entities/Integraciones/IntegrationDefinition.cs`
- Create: `Portal SaaS - Core/src/PortalSaas.Core/Integraciones/SqlDirectConnector.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integraciones/SqlDirectConnectorTests.cs`

**Interfaces:**
- Consumes: `IHanaService.QueryDynamicAsync` (Task 2), `IIntegrationConnector` (contrato
  existente, con el parámetro `cursorIncremental` ya agregado esta sesión).
- Produces: `SqlDirectConnector.Tipo => "Sql"`, registrado en DI (Task 3, Step 5) para
  que `IntegrationSyncHostedService` lo resuelva junto a `SapDocumentConnector`/`WmsCloudConnector`.

- [ ] **Step 1: Agregar el valor al enum**

```csharp
public enum IntegrationConectorTipo { Sap, Rest, Archivo, WmsCloud, Sql }
```

Revisar el CHECK constraint de Postgres/SqlServer en `PortalSaasDbContext.cs` (línea
~505-506, `ck_integration_definitions_connector_type`) — agregar `'sql'` a la lista de
valores permitidos, y el conversor `ConectorTipoAProveedor`/`ConectorTipoDesdeProveedor`
si existe una función de mapeo enum↔string (buscar en el mismo archivo cerca de línea
515-517).

- [ ] **Step 2: Escribir el conector**

```csharp
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Contratos.Integraciones;

namespace PortalSaas.Core.Integraciones;

/// <summary>
/// Conector de ingesta por SQL directo (HANA/SQL Server, vía IHanaService) -- reemplaza
/// Service Layer para entidades cuyo mapeo real requiere joins que OData no puede
/// expresar (ver spec 2026-08-21-ingesta-sql-directa-staging-items-design.md). La query
/// la escribe un admin en /Admin/Integraciones/Nuevo; el conector solo reemplaza el
/// placeholder ":cursor" por el valor real y ejecuta.
/// </summary>
public class SqlDirectConnector : IIntegrationConnector
{
    private readonly IHanaService _hanaService;

    public SqlDirectConnector(IHanaService hanaService)
    {
        _hanaService = hanaService;
    }

    public string Tipo => "Sql";

    private sealed record SqlDirectConfig(string Query);

    public async Task<IReadOnlyList<IntegrationRecord>> PullAsync(
        string conectorConfigJson,
        DateTimeOffset? cursorIncremental,
        CancellationToken cancellationToken)
    {
        var config = System.Text.Json.JsonSerializer.Deserialize<SqlDirectConfig>(conectorConfigJson)
            ?? throw new InvalidOperationException("Config de conector Sql inválida o vacía.");

        var filas = await _hanaService.QueryDynamicAsync(
            config.Query,
            new Dictionary<string, object?> { ["cursor"] = cursorIncremental?.UtcDateTime },
            cancellationToken);

        return filas
            .Select(fila => new IntegrationRecord(new Dictionary<string, object?>(fila, StringComparer.OrdinalIgnoreCase)))
            .ToList();
    }

    public string DescribirConsulta(string conectorConfigJson, DateTimeOffset? cursorIncremental = null)
    {
        SqlDirectConfig? config;
        try
        {
            config = System.Text.Json.JsonSerializer.Deserialize<SqlDirectConfig>(conectorConfigJson);
        }
        catch (Exception ex)
        {
            return $"Config de conector Sql inválida: {ex.Message}";
        }

        if (config is null)
        {
            return "Config de conector Sql vacía.";
        }

        var cursorTexto = cursorIncremental?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss") ?? "(sin cursor, primera corrida)";
        return $"SQL directo (:cursor={cursorTexto}): {config.Query}";
    }

    public Task<IReadOnlyList<IntegrationPushResult>> PushAsync(
        string conectorConfigJson,
        IReadOnlyList<IntegrationRecord> registros,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("SqlDirectConnector.PushAsync no está implementado -- este conector solo lee (Bajada), nunca escribe hacia SAP.");
}
```

Nota: `IntegrationRecord` — revisar su constructor real en
`PortalSaas.Abstractions/Contratos/Integraciones/IntegrationRecord.cs` antes de escribir
esto (los conectores existentes lo construyen con
`new IntegrationRecord(new Dictionary<string, object?> {...})`, confirmar que acepta un
diccionario ya armado directamente, como se usa arriba).

- [ ] **Step 3: Tests**

```csharp
public class SqlDirectConnectorTests
{
    private sealed class HanaServiceFalso : IHanaService
    {
        public IReadOnlyList<IReadOnlyDictionary<string, object?>>? FilasARetornar { get; set; }
        public object? ParametrosRecibidos { get; private set; }
        public string? SqlRecibido { get; private set; }

        public Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDynamicAsync(
            string sqlParametrizado, object? parametros = null, CancellationToken ct = default)
        {
            SqlRecibido = sqlParametrizado;
            ParametrosRecibidos = parametros;
            return Task.FromResult(FilasARetornar ?? Array.Empty<IReadOnlyDictionary<string, object?>>());
        }

        public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parametros = null, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<int> ExecuteAsync(string sql, object? parametros = null, CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
        public Task<string> GetPlatformEngineTypeAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("No debería llamarse en este test.");
    }

    [Fact]
    public async Task PullAsync_FilasConColumnasDinamicas_MapeaTodasLasClavesAlRegistro()
    {
        var hana = new HanaServiceFalso
        {
            FilasARetornar = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["item_alternate_code"] = "ITM1", ["brand_code"] = "NIKE", ["putaway_type"] = "A" },
            },
        };
        var conector = new SqlDirectConnector(hana);

        var resultado = await conector.PullAsync("""{"Query":"SELECT * FROM OITM"}""", null, CancellationToken.None);

        var registro = Assert.Single(resultado);
        Assert.Equal("ITM1", registro["item_alternate_code"]);
        Assert.Equal("NIKE", registro["brand_code"]);
        Assert.Equal("A", registro["putaway_type"]);
    }

    [Fact]
    public async Task PullAsync_ConCursor_PasaElValorComoParametro()
    {
        var hana = new HanaServiceFalso { FilasARetornar = new List<IReadOnlyDictionary<string, object?>>() };
        var conector = new SqlDirectConnector(hana);
        var cursor = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

        await conector.PullAsync("""{"Query":"SELECT 1 WHERE :cursor IS NULL"}""", cursor, CancellationToken.None);

        var parametros = Assert.IsType<Dictionary<string, object?>>(hana.ParametrosRecibidos);
        Assert.Equal(cursor.UtcDateTime, parametros["cursor"]);
    }

    [Fact]
    public void DescribirConsulta_SinCursor_IndicaPrimeraCorrida()
    {
        var conector = new SqlDirectConnector(new HanaServiceFalso());

        var descripcion = conector.DescribirConsulta("""{"Query":"SELECT 1"}""");

        Assert.Contains("sin cursor, primera corrida", descripcion);
    }

    [Fact]
    public async Task PushAsync_SiempreLanzaNotSupportedException()
    {
        var conector = new SqlDirectConnector(new HanaServiceFalso());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => conector.PushAsync("{}", Array.Empty<IntegrationRecord>(), CancellationToken.None));
    }
}
```

- [ ] **Step 4: Registrar en DI**

En `Portal SaaS - Core/src/PortalSaas.Host/Program.cs`, junto a la línea 111:

```csharp
builder.Services.AddScoped<IIntegrationConnector, SqlDirectConnector>();
```

- [ ] **Step 5: Build + test**

```bash
cd "Portal SaaS - Core"
dotnet build PortalSaas.sln --no-restore
dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --no-build
```

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Data/Entities/Integraciones/IntegrationDefinition.cs" "Portal SaaS - Core/src/PortalSaas.Core/Integraciones/SqlDirectConnector.cs" "Portal SaaS - Core/src/PortalSaas.Host/Program.cs" "Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integraciones/SqlDirectConnectorTests.cs" "Portal SaaS - Core/src/PortalSaas.Data/PortalSaasDbContext.cs"
git commit -m "feat: agregar conector SqlDirectConnector (ConectorTipo=Sql) para ingesta vía SQL directo a HANA/SQL Server"
```

---

### Task 4: `wms_sap_stage_item.extra_fields` (columna nueva)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsSapStageItem.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs`
- Create: migraciones en `Modulo.Wms.Migrations.Postgres` y `Modulo.Wms.Migrations.SqlServer`

**Interfaces:**
- Produces: `WmsSapStageItem.ExtraFieldsJson` (string?, serializado) — consumido por
  Task 6 (writer) y Task 8 (reader).

- [ ] **Step 1: Agregar la propiedad a la entidad**

```csharp
public class WmsSapStageItem
{
    public long LineId { get; set; }
    public Guid CompanyId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? BarCode { get; set; }
    public DateTime SourceUpdateDate { get; set; }
    public WmsSapStageStatus Status { get; set; } = WmsSapStageStatus.Pendiente;
    public int RetryCount { get; set; }
    public string? ErrorMsg { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; set; }

    /// <summary>
    /// Campos adicionales traídos por SqlDirectConnector que no tienen columna propia
    /// (brand_code, putaway_type, dimensiones, etc.) -- serializado como JSON,
    /// Dictionary&lt;string,object?&gt;. Ver spec 2026-08-21-ingesta-sql-directa-staging-items-design.md.
    /// </summary>
    public string? ExtraFieldsJson { get; set; }
}
```

- [ ] **Step 2: Mapear en `WmsDbContext`**

Junto a la línea 388 (`entity.Property(e => e.SyncedAt)...`):

```csharp
entity.Property(e => e.ExtraFieldsJson).HasColumnName("extra_fields");
```

Para Postgres, forzar tipo `jsonb` en la migración (Step 3) editando el `.cs` generado
si `dotnet ef migrations add` no lo infiere solo (columna `string?` por defecto genera
`text`, hay que cambiarlo a `jsonb` a mano en el archivo de migración generado, no en el
`DbContext` — mantener el tipo CLR como `string` es intencional, el `(de)serializado
manual pasa por `System.Text.Json` en el writer/reader, no por un value converter de EF).

- [ ] **Step 3: Generar migraciones**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet ef migrations add AddWmsSapStageItemExtraFields --project src/Modulo.Wms.Migrations.Postgres --context WmsDbContext
dotnet ef migrations add AddWmsSapStageItemExtraFields --project src/Modulo.Wms.Migrations.SqlServer --context WmsDbContext
```

Revisar el `.cs` generado para Postgres: cambiar `AddColumn<string>(..., type: "text",
...)` a `type: "jsonb"` si EF no lo detectó solo. Para SqlServer, `nvarchar(max)` está
bien tal cual.

- [ ] **Step 4: Aplicar a la base de "Depor (Testing)"**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
ConnectionStrings__Default="Host=172.16.122.171;Port=5432;Database=ps_comdepor_wms_qa;Username=admin_saas;Password=<ver build-secrets.local.ps1>" dotnet ef database update --project src/Modulo.Wms.Migrations.Postgres --context WmsDbContext
```

(usar la contraseña real de `build-secrets.local.ps1`, no hardcodearla en ningún
archivo del repo).

- [ ] **Step 5: Build**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet build src/Modulo.Wms/Modulo.Wms.csproj
```

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsSapStageItem.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres/Migrations/" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.SqlServer/Migrations/"
git commit -m "feat: agregar columna extra_fields (jsonb) a wms_sap_stage_item"
```

---

### Task 5: Tabla `wms_validation_fields`

**Files:**
- Create: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsValidationField.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs`
- Create: migraciones Postgres + SqlServer

**Interfaces:**
- Produces: `WmsDbContext.ValidationFields` (`DbSet<WmsValidationField>`) — consumido
  por Task 6.

- [ ] **Step 1: Modelo**

```csharp
namespace Modulo.Wms.Models;

/// <summary>
/// Qué campos, al cambiar de VALOR (no de fecha), disparan Status=Pendiente en el
/// staging de una entidad -- ver spec 2026-08-21-ingesta-sql-directa-staging-items-design.md.
/// Mismo patrón que WmsFieldMapping (Fase Subida) pero para el lado Bajada.
/// </summary>
public sealed class WmsValidationField
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TipoEntidad { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
```

- [ ] **Step 2: Mapear en `WmsDbContext`**

Junto al `DbSet<WmsFieldMapping>` (línea 29):

```csharp
public DbSet<WmsValidationField> ValidationFields => Set<WmsValidationField>();
```

Y el `modelBuilder.Entity<...>` (mismo bloque que `wms_oracle_field_mappings`, líneas
44-61):

```csharp
modelBuilder.Entity<WmsValidationField>(e =>
{
    e.ToTable("wms_validation_fields");
    e.HasKey(x => x.Id);
    e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
    e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
    e.Property(x => x.TipoEntidad).HasColumnName("tipo_entidad").HasMaxLength(50).IsRequired();
    e.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(100).IsRequired();
    e.Property(x => x.IsActive).HasColumnName("is_active");
    e.HasIndex(x => new { x.CompanyId, x.TipoEntidad, x.FieldName })
        .IsUnique()
        .HasDatabaseName("uk_wms_validation_fields_key");
});
```

- [ ] **Step 3: Generar migraciones + seed inicial**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet ef migrations add AddWmsValidationFields --project src/Modulo.Wms.Migrations.Postgres --context WmsDbContext
dotnet ef migrations add AddWmsValidationFields --project src/Modulo.Wms.Migrations.SqlServer --context WmsDbContext
```

Agregar al método `Up` de AMBAS migraciones (Postgres y SqlServer), después del
`CreateTable`, el seed para "Depor (Testing)" (companyId
`0f5f72d4-abe6-4a75-bd26-c663848abbb4`, confirmado esta sesión vía
`module_external_connections`):

```csharp
migrationBuilder.InsertData(
    table: "wms_validation_fields",
    columns: new[] { "company_id", "tipo_entidad", "field_name", "is_active" },
    values: new object[,]
    {
        { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "description", true },
        { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "barcode", true },
        { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "brand_code", true },
        { new Guid("0f5f72d4-abe6-4a75-bd26-c663848abbb4"), "Item", "putaway_type", true },
    });
```

- [ ] **Step 4: Aplicar a la base y build**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
ConnectionStrings__Default="Host=172.16.122.171;Port=5432;Database=ps_comdepor_wms_qa;Username=admin_saas;Password=<ver build-secrets.local.ps1>" dotnet ef database update --project src/Modulo.Wms.Migrations.Postgres --context WmsDbContext
dotnet build src/Modulo.Wms/Modulo.Wms.csproj
```

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Models/WmsValidationField.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Data/WmsDbContext.cs" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.Postgres/Migrations/" "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms.Migrations.SqlServer/Migrations/"
git commit -m "feat: agregar tabla wms_validation_fields con seed para Item"
```

---

### Task 6: Reescribir `WmsSapStageItemWriter` (diff por valor)

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageItemWriter.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSapStageItemWriterTests.cs`

**Interfaces:**
- Consumes: `WmsDbContext.ValidationFields` (Task 5), `WmsSapStageItem.ExtraFieldsJson` (Task 4).
- Produces: comportamiento nuevo de `EscribirAsync` — mismo contrato público
  (`IIntegrationEntityWriter`), sin cambios de firma.

- [ ] **Step 1: Escribir el test que define el comportamiento nuevo (falla primero)**

```csharp
[Fact]
public async Task EscribirAsync_CambiaCampoQueNoEstaEnValidationFields_NoMarcaPendiente()
{
    var companyId = Guid.NewGuid();
    await using var contexto = CrearContexto();
    contexto.ValidationFields.Add(new WmsValidationField { CompanyId = companyId, TipoEntidad = "Item", FieldName = "brand_code", IsActive = true });
    contexto.WmsSapStageItems.Add(new WmsSapStageItem
    {
        CompanyId = companyId, ItemCode = "ITM1", ItemName = "Original", BarCode = "123",
        Status = WmsSapStageStatus.ProcesadoWms,
        ExtraFieldsJson = """{"brand_code":"NIKE","unit_length":30}""",
    });
    await contexto.SaveChangesAsync();

    var writer = new WmsSapStageItemWriter(contexto);
    var registro = new IntegrationRecord(new Dictionary<string, object?>
    {
        ["item_alternate_code"] = "ITM1", ["description"] = "Original", ["barcode"] = "123",
        ["brand_code"] = "NIKE", ["unit_length"] = 45, // cambia unit_length, NO está en ValidationFields
    });

    await writer.EscribirAsync(companyId, new[] { registro }, CancellationToken.None);

    var fila = await contexto.WmsSapStageItems.SingleAsync(f => f.ItemCode == "ITM1");
    Assert.Equal(WmsSapStageStatus.ProcesadoWms, fila.Status); // NO cambió a Pendiente
    Assert.Contains("\"unit_length\":45", fila.ExtraFieldsJson); // pero el dato SÍ se actualizó
}

[Fact]
public async Task EscribirAsync_CambiaCampoQueEstaEnValidationFields_MarcaPendiente()
{
    var companyId = Guid.NewGuid();
    await using var contexto = CrearContexto();
    contexto.ValidationFields.Add(new WmsValidationField { CompanyId = companyId, TipoEntidad = "Item", FieldName = "brand_code", IsActive = true });
    contexto.WmsSapStageItems.Add(new WmsSapStageItem
    {
        CompanyId = companyId, ItemCode = "ITM1", ItemName = "Original", BarCode = "123",
        Status = WmsSapStageStatus.ProcesadoWms,
        ExtraFieldsJson = """{"brand_code":"NIKE"}""",
    });
    await contexto.SaveChangesAsync();

    var writer = new WmsSapStageItemWriter(contexto);
    var registro = new IntegrationRecord(new Dictionary<string, object?>
    {
        ["item_alternate_code"] = "ITM1", ["description"] = "Original", ["barcode"] = "123",
        ["brand_code"] = "NIKE-KIDS", // SÍ está en ValidationFields y cambió
    });

    await writer.EscribirAsync(companyId, new[] { registro }, CancellationToken.None);

    var fila = await contexto.WmsSapStageItems.SingleAsync(f => f.ItemCode == "ITM1");
    Assert.Equal(WmsSapStageStatus.Pendiente, fila.Status);
}
```

Revisar el helper `CrearContexto()` existente en el archivo de test (probablemente ya
usa `UseInMemoryDatabase` con un nombre único por test, mismo patrón que
`IntegrationSyncHostedServiceTests`).

- [ ] **Step 2: Correr los tests y confirmar que fallan**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter "FullyQualifiedName~WmsSapStageItemWriterTests"
```

Esperado: FALLAN (el writer actual no tiene esta lógica todavía).

- [ ] **Step 3: Reescribir `EscribirAsync`**

```csharp
public async Task EscribirAsync(Guid companyId, IReadOnlyList<IntegrationRecord> registros, CancellationToken cancellationToken)
{
    var itemCodes = registros.Select(r => (string)r["item_alternate_code"]!).ToList();
    var existentes = await _contexto.WmsSapStageItems
        .Where(f => f.CompanyId == companyId && itemCodes.Contains(f.ItemCode))
        .ToDictionaryAsync(f => f.ItemCode, cancellationToken);

    var camposValidacion = (await _contexto.ValidationFields
        .Where(v => v.CompanyId == companyId && v.TipoEntidad == "Item" && v.IsActive)
        .Select(v => v.FieldName)
        .ToListAsync(cancellationToken))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var registro in registros)
    {
        var itemCode = (string)registro["item_alternate_code"]!;
        var existente = existentes.GetValueOrDefault(itemCode);

        var (itemName, barCode, extraFieldsJson) = SepararCampos(registro);

        if (existente is null)
        {
            _contexto.WmsSapStageItems.Add(new WmsSapStageItem
            {
                CompanyId = companyId,
                ItemCode = itemCode,
                ItemName = itemName,
                BarCode = barCode,
                ExtraFieldsJson = extraFieldsJson,
                Status = WmsSapStageStatus.Pendiente,
            });
            continue;
        }

        var extraFieldsExistentes = string.IsNullOrEmpty(existente.ExtraFieldsJson)
            ? new Dictionary<string, object?>()
            : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(existente.ExtraFieldsJson)!;
        var extraFieldsNuevos = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(extraFieldsJson)!;

        var cambioAlgunCampoDeValidacion = CampoDeValidacionCambio("description", existente.ItemName, itemName, camposValidacion)
            || CampoDeValidacionCambio("barcode", existente.BarCode, barCode, camposValidacion)
            || camposValidacion.Any(campo => ValorCambio(extraFieldsExistentes, extraFieldsNuevos, campo));

        if (cambioAlgunCampoDeValidacion || existente.Status == WmsSapStageStatus.ErrorWms)
        {
            existente.Status = WmsSapStageStatus.Pendiente;
            existente.ErrorMsg = null;
        }

        existente.ItemName = itemName;
        existente.BarCode = barCode;
        existente.ExtraFieldsJson = extraFieldsJson;
    }

    await _contexto.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// "description"/"barcode" son las claves que trae SqlDirectConnector (nombres de
/// columna reales de la query, ver spec) -- item_name/bar_code son las columnas
/// tipadas que las reciben. El resto de las claves del registro cae a extra_fields.
/// </summary>
private static (string ItemName, string? BarCode, string ExtraFieldsJson) SepararCampos(IntegrationRecord registro)
{
    var itemName = (string?)registro["description"] ?? string.Empty;
    var barCode = (string?)registro["barcode"];

    var extra = registro.Campos // revisar el nombre real del miembro que expone el diccionario interno de IntegrationRecord
        .Where(kvp => kvp.Key is not ("item_alternate_code" or "description" or "barcode"))
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

    return (itemName, barCode, System.Text.Json.JsonSerializer.Serialize(extra));
}

private static bool CampoDeValidacionCambio(string campo, string? valorExistente, string? valorNuevo, HashSet<string> camposValidacion) =>
    camposValidacion.Contains(campo) && valorExistente != valorNuevo;

private static bool ValorCambio(Dictionary<string, object?> existentes, Dictionary<string, object?> nuevos, string campo)
{
    var tieneExistente = existentes.TryGetValue(campo, out var valorExistente);
    var tieneNuevo = nuevos.TryGetValue(campo, out var valorNuevo);
    if (!tieneExistente && !tieneNuevo) return false;
    return valorExistente?.ToString() != valorNuevo?.ToString();
}
```

**Nota para quien implemente:** `registro.Campos` es un nombre supuesto — revisar el
archivo real `IntegrationRecord.cs` para el miembro correcto que expone el diccionario
interno (puede ser un indexador únicamente, sin enumeración pública — si es así, agregar
un método/propiedad de solo lectura `IReadOnlyDictionary<string, object?> Campos` a
`IntegrationRecord` como parte de este mismo Step, ya que **hace falta** poder iterar
todas las claves para separar identidad de `extra_fields` acá, y de nuevo en Task 7
(XML dinámico) — mismo requisito en dos lugares, resolverlo una sola vez en el tipo
base).

- [ ] **Step 4: Correr tests, confirmar que pasan**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter "FullyQualifiedName~WmsSapStageItemWriterTests"
```

- [ ] **Step 5: Correr toda la suite del plugin (no romper nada existente)**

```bash
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageItemWriter.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSapStageItemWriterTests.cs"
git commit -m "feat: WmsSapStageItemWriter compara valores de campos de validación en vez de fecha para decidir reenvío"
```

---

### Task 7: `IntegrationRecord` — exponer iteración de campos (si hace falta)

**Files:**
- Modify (condicional, solo si Task 6 lo requirió): `Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/Integraciones/IntegrationRecord.cs`
- Test: `Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integraciones/IntegrationRecordTests.cs` (crear si no existe)

**Interfaces:**
- Produces: `IReadOnlyDictionary<string, object?> Campos` (o el nombre que ya tenga el
  campo interno) — consumido por Task 6 y Task 8.

- [ ] **Step 1: Leer el archivo real primero**

Antes de cualquier cambio, leer `IntegrationRecord.cs` completo. Si ya expone una forma
de enumerar todas sus claves (ej. implementa `IEnumerable<KeyValuePair<string,
object?>>`, o ya tiene una propiedad pública), esta tarea se reduce a confirmarlo y
saltar al Step 3 sin tocar código.

- [ ] **Step 2: Agregar la propiedad si no existe**

```csharp
public IReadOnlyDictionary<string, object?> Campos => _valores; // nombre real del campo interno
```

- [ ] **Step 3: Test**

```csharp
[Fact]
public void Campos_ExponeTodasLasClavesDelRegistro()
{
    var registro = new IntegrationRecord(new Dictionary<string, object?> { ["A"] = 1, ["B"] = "x" });

    Assert.Equal(2, registro.Campos.Count);
    Assert.Equal(1, registro.Campos["A"]);
}
```

- [ ] **Step 4: Build + test + commit**

```bash
cd "Portal SaaS - Core"
dotnet build PortalSaas.sln --no-restore
dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj --no-build
git add "Portal SaaS - Core/src/PortalSaas.Abstractions/Contratos/Integraciones/IntegrationRecord.cs" "Portal SaaS - Core/tests/PortalSaas.Core.Tests/Integraciones/IntegrationRecordTests.cs"
git commit -m "feat: exponer IntegrationRecord.Campos para iteración dinámica"
```

---

### Task 8: `WmsSapStageItemReader` — volcar `extra_fields`

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageItemReader.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs`

**Interfaces:**
- Consumes: `WmsSapStageItem.ExtraFieldsJson` (Task 4).
- Produces: `IntegrationRecord` con todas las claves de `extra_fields` incluidas —
  consumido por Task 9 (`WmsCloudConnector`).

- [ ] **Step 1: Test primero**

```csharp
[Fact]
public async Task LeerPendientesAsync_ConExtraFields_IncluyeCadaClaveEnElRegistro()
{
    var companyId = Guid.NewGuid();
    await using var contexto = CrearContexto();
    contexto.WmsSapStageItems.Add(new WmsSapStageItem
    {
        CompanyId = companyId, ItemCode = "ITM1", ItemName = "Nombre", BarCode = "123",
        Status = WmsSapStageStatus.Pendiente,
        ExtraFieldsJson = """{"brand_code":"NIKE","putaway_type":"A"}""",
    });
    await contexto.SaveChangesAsync();

    var reader = new WmsSapStageItemReader(contexto);
    var resultado = await reader.LeerPendientesAsync(companyId, CancellationToken.None);

    var registro = Assert.Single(resultado);
    Assert.Equal("NIKE", registro.Campos["brand_code"]);
    Assert.Equal("A", registro.Campos["putaway_type"]);
}
```

- [ ] **Step 2: Modificar `LeerPendientesAsync`**

```csharp
public async Task<IReadOnlyList<IntegrationRecord>> LeerPendientesAsync(Guid companyId, CancellationToken cancellationToken)
{
    var filas = await _contexto.WmsSapStageItems
        .Where(f => f.CompanyId == companyId && f.Status == WmsSapStageStatus.Pendiente)
        .ToListAsync(cancellationToken);

    return filas
        .Select(f =>
        {
            var campos = new Dictionary<string, object?>
            {
                ["TipoDocumento"] = "Item",
                ["ItemCode"] = f.ItemCode,
                ["ItemName"] = f.ItemName,
                ["BarCode"] = f.BarCode,
                ["_StagingLineIds"] = new List<long> { f.LineId },
            };

            if (!string.IsNullOrEmpty(f.ExtraFieldsJson))
            {
                var extra = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(f.ExtraFieldsJson)!;
                foreach (var (clave, valor) in extra)
                {
                    campos[clave] = valor;
                }
            }

            return new IntegrationRecord(campos);
        })
        .ToList();
}
```

`MarcarProcesadoAsync` no cambia.

- [ ] **Step 3: Tests + build**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter "FullyQualifiedName~WmsSapStageItemReaderTests"
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj
```

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsSapStageItemReader.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsSapStageItemReaderTests.cs"
git commit -m "feat: WmsSapStageItemReader vuelca extra_fields al IntegrationRecord"
```

---

### Task 9: `WmsCloudConnector.ArmarXmlLote` — caso `"Item"` dinámico

**Files:**
- Modify: `Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsCloudConnector.cs`
- Modify: `Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/WmsCloudConnectorTests.cs` (o el archivo de test existente, revisar nombre real)

**Interfaces:**
- Consumes: `IntegrationRecord.Campos` (Task 7).
- Produces: XML con un `XElement` por cada clave del registro (excepto internas), en
  vez de las 3 líneas fijas actuales.

- [ ] **Step 1: Test primero**

```csharp
[Fact]
public void ArmarXmlLote_TipoItemConCamposExtra_GeneraUnNodoPorCadaCampo()
{
    var registro = new IntegrationRecord(new Dictionary<string, object?>
    {
        ["TipoDocumento"] = "Item",
        ["ItemCode"] = "ITM1",
        ["ItemName"] = "Nombre",
        ["BarCode"] = "123",
        ["brand_code"] = "NIKE",
        ["putaway_type"] = "A",
        ["_StagingLineIds"] = new List<long> { 1 },
    });
    var config = new WmsCloudConfigDeTest(/* ... */); // usar el mismo record/fixture que ya use el archivo de test existente

    var xml = InvocarArmarXmlLote(new[] { registro }, config); // método reflection o hacer el método internal+InternalsVisibleTo si hoy es private static y no hay forma limpia de testearlo directo -- revisar cómo lo prueba el test existente para los otros TipoDocumento, seguir el mismo mecanismo

    var nodoItem = xml.Descendants("item").Single();
    Assert.Equal("NIKE", nodoItem.Element("brand_code")?.Value);
    Assert.Equal("A", nodoItem.Element("putaway_type")?.Value);
    Assert.Equal("ITM1", nodoItem.Element("itemcode")?.Value); // ver Step 2, nota sobre nombre de clave vs. nombre de nodo XML
}
```

**Nota importante antes de escribir el Step 2:** hoy `WmsSapStageItemReader` (Task 8)
puebla el registro con las claves `ItemCode`/`ItemName`/`BarCode` (PascalCase, ver Step
2 de Task 8) para los 3 campos identidad, pero `SqlDirectConnector`/staging usa
`item_alternate_code`/`description`/`barcode` como nombres de columna reales del lado
SAP. Decidir en este Step 1 (antes de escribir el test) cuál es el nombre de clave
canónico que debe llegar al XML para esos 3 campos -- lo más simple y consistente con
"todo el resto es dinámico" es que **Task 8 use los mismos nombres que ya usa
`extra_fields`** (`item_alternate_code`, `description`, `barcode`) en vez de
`ItemCode`/`ItemName`/`BarCode`, para que el Paso 2 de esta tarea no necesite ningún
caso especial. Si se sigue esa decisión, ajustar el Step 2 de Task 8 antes de continuar
acá (son la misma pieza de trabajo vista desde dos tareas, resolver una vez).

- [ ] **Step 2: Reemplazar el caso `"Item"`**

```csharp
var nodos = lote.Select(registro => tipoDocumento switch
{
    "Item" => ArmarNodoItemDinamico(registro, mapeos),
    "Store" => new XElement(nombreItem,
        CampoXml(mapeos, "SAPWMS_STORE", "code", registro, registro["CardCode"]),
        CampoXml(mapeos, "SAPWMS_STORE", "name", registro, registro["CardName"]),
        CampoXml(mapeos, "SAPWMS_STORE", "parent_company_id", registro, config.ParentCompanyCode)),
    "IbShipment" => ArmarNodoIbShipment(registro, mapeos),
    "Order" => ArmarNodoOrder(registro, mapeos),
    _ => throw new InvalidOperationException($"TipoDocumento '{tipoDocumento}' no soportado en WmsCloudConnector."),
});
```

```csharp
/// <summary>
/// Item pasa a ser el único caso dinámico -- itera TODAS las claves del registro salvo
/// las internas del motor genérico, generando un XElement por cada una. Reemplaza las 3
/// líneas fijas (item_alternate_code/description/barcode) que existían antes de la
/// ronda de ingesta SQL directa (ver spec 2026-08-21-ingesta-sql-directa-staging-items-design.md).
/// El mecanismo de wms_oracle_field_mappings (override de plantilla) se mantiene: se
/// aplica por cada clave dinámica bajo el MapperKey "SAPWMS_ITEM", no solo sobre las 3
/// de antes.
/// </summary>
private static readonly HashSet<string> ClavesInternasExcluidas = new(StringComparer.OrdinalIgnoreCase)
{
    "TipoDocumento", "_StagingLineIds",
};

private static XElement ArmarNodoItemDinamico(IntegrationRecord registro, IReadOnlyDictionary<(string MapperKey, string FieldName), string> mapeos)
{
    var elementos = registro.Campos
        .Where(kvp => !ClavesInternasExcluidas.Contains(kvp.Key))
        .Select(kvp => CampoXml(mapeos, "SAPWMS_ITEM", kvp.Key.ToLowerInvariant(), registro, kvp.Value));

    return new XElement("item", elementos);
}
```

- [ ] **Step 3: Correr el test nuevo, confirmar que pasa**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj --filter "FullyQualifiedName~WmsCloudConnector"
```

- [ ] **Step 4: Correr TODA la suite del plugin — este cambio toca un `switch` compartido con Store/IbShipment/Order, confirmar que esos 3 casos NO se rompieron**

```bash
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj
```

- [ ] **Step 5: Commit**

```bash
git add "Portal SaaS - Plugins/Modulo.Wms/src/Modulo.Wms/Services/WmsCloudConnector.cs" "Portal SaaS - Plugins/Modulo.Wms/tests/Modulo.Wms.Tests/Services/"
git commit -m "feat: WmsCloudConnector arma el nodo XML de Item dinámicamente a partir de todas las claves del registro"
```

---

### Task 10: UI — dropdown "Sql" + campo Query editable

**Files:**
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml`
- Modify: `Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml.cs`

**Interfaces:**
- Consumes: `IntegrationConectorTipo.Sql` (Task 3).
- Produces: UI para crear/editar una `IntegrationDefinition` con `ConectorTipo=Sql`.

- [ ] **Step 1: `InputModel` — agregar campo `Query`**

En `Nuevo.cshtml.cs`, junto a los campos existentes del `InputModel` (`Filtro`,
`PageSize` del bloque Sap):

```csharp
[Display(Name = "Query SQL (HANA/SQL Server, con :cursor en el WHERE si aplica)")]
public string? Query { get; set; }
```

Y en `ArmarConfigCifradaAsync`/`OnGetAsync` (revisar los nombres reales de esos
métodos), agregar la rama para `ConectorTipo == IntegrationConectorTipo.Sql` que
serializa `{ Query = Input.Query }` — mismo patrón que ya existe para `Sap`
(`SapConfigInput`)/`WmsCloud`.

- [ ] **Step 2: `Nuevo.cshtml` — bloque de configuración condicional**

Junto al bloque `bloque-config-sap` existente (visible/oculto por JS según
`ConectorTipo` seleccionado, ver el fix de esta sesión sobre `Html.GetEnumSelectList`),
agregar `bloque-config-sql` con un `<textarea>` grande para `Input.Query`, mismo criterio
de ayuda visual que ya tiene el filtro OData (mostrar `DescribirConsulta` como preview
si es viable, o al menos un placeholder con la query de referencia del spec como
ejemplo).

- [ ] **Step 3: Verificación manual en navegador**

Levantar el Host (`build-all.ps1`), entrar a `/Admin/Integraciones/Nuevo`, confirmar que
al elegir "Sql" en el dropdown de Tipo de conector aparece el campo Query y desaparecen
los bloques de Sap/WmsCloud. Guardar una definición de prueba y confirmar que persiste
correctamente (revisar en Bitácora que `DescribirConsulta` la muestra bien).

- [ ] **Step 4: Commit**

```bash
git add "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml" "Portal SaaS - Core/src/PortalSaas.Host/Pages/Admin/Integraciones/Nuevo.cshtml.cs"
git commit -m "feat: UI para configurar IntegrationDefinition con ConectorTipo=Sql"
```

---

### Task 11: Verificación end-to-end contra SAP/WMS reales

**Files:** ninguno (solo verificación manual, mismo patrón que el resto de la sesión).

- [ ] **Step 1: Rebuild + redeploy completos**

```bash
cd "Portal SaaS - Plugins/Modulo.Wms"
dotnet test tests/Modulo.Wms.Tests/Modulo.Wms.Tests.csproj
powershell -File publish-dist.ps1
cd "../../Portal SaaS - Core"
dotnet test tests/PortalSaas.Core.Tests/PortalSaas.Core.Tests.csproj
cd ..
.\build-all.ps1
```

- [ ] **Step 2: Crear la `IntegrationDefinition` de Bajada real**

Vía UI (`/Admin/Integraciones/Nuevo`): "WMS - Items (Bajada SQL)", `ConectorTipo=Sql`,
`Direccion=Bajada`, `EntidadNegocio=SapWms.Item`, `Query` = la query de referencia
verificada en Task 1 (ajustada si Task 1 encontró diferencias).

- [ ] **Step 3: Disparar "Ejecutar ahora" y verificar en Bitácora**

Confirmar `Resultado=Exito`, `RegistrosProcesados` coherente con la cantidad real de
items que matchean el filtro, y `DetalleConsulta` mostrando la query con el cursor
resuelto.

- [ ] **Step 4: Verificar en la BD que `extra_fields` quedó poblado**

Vía scratch script (mismo patrón `wms-check5` de toda la sesión): `SELECT item_code,
extra_fields FROM wms_sap_stage_item LIMIT 5` — confirmar que trae `brand_code`,
`putaway_type`, etc., no solo los 3 campos de antes.

- [ ] **Step 5: Modificar un artículo real en SAP (campo de validación, ej. brand) y confirmar que SÍ dispara `Pendiente`**

Repetir "Ejecutar ahora" en Bajada, confirmar en BD que ese artículo específico pasó a
`Status=Pendiente` y que otro artículo sin cambios reales en campos de validación
**no** cambió de estado aunque su fila se haya vuelto a tocar (actualizar
`ExtraFieldsJson` con los mismos valores, sin flip de status).

- [ ] **Step 6: Correr Subida y confirmar el XML completo llega a WMS Cloud**

Disparar "Ejecutar ahora" en "WMS - Items (Subida WMS)", confirmar en Bitácora
`Resultado=Exito` y (si el ambiente de prueba de Oracle WMS Cloud lo permite) confirmar
del lado de LogFire que el documento recibido trae los campos nuevos (`brand_code`,
`putaway_type`, etc.), no solo los 3 de antes.

- [ ] **Step 7: Reportar resultado al usuario, sin commitear nada de esta tarea (es solo verificación)**
