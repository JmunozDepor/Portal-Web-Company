using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Administracion;
using PortalSaas.Core.Catalogos;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Compras;
using PortalSaas.Core.Correo;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Core.Inventario;
using PortalSaas.Core.Sap;
using PortalSaas.Core.Seguridad;
using PortalSaas.Core.Usuarios;
using PortalSaas.Core.Ventas;
using PortalSaas.Data;
using PortalSaas.Host.Comandos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Motor dual (Postgres/SQL Server) -- ver docs/02-ARQUITECTURA-BASE-DE-DATOS.md §1 y
// §7. El Host NO aplica migraciones en el arranque a propósito -- eso es un paso
// explícito y manual (ver docs/05-RUNBOOK-PRODUCCION.md), no algo que corra solo al
// desplegar.
var databaseProvider = builder.Configuration["Database:Provider"] ?? "postgresql";
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");

builder.Services.AddDbContext<PortalSaasDbContext>(options =>
{
    switch (databaseProvider)
    {
        case "sqlserver":
            options.UseSqlServer(connectionString);
            break;
        case "postgresql":
            options.UseNpgsql(connectionString);
            break;
        default:
            throw new InvalidOperationException(
                $"Database:Provider '{databaseProvider}' no reconocido. Valores válidos: 'postgresql' | 'sqlserver'.");
    }

    options.UseSnakeCaseNamingConvention();
});

builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISecretoCifradoService, SecretoCifradoService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IPlatformAdminAuthenticationService, PlatformAdminAuthenticationService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IUserPreferenceService, UserPreferenceService>();
builder.Services.AddScoped<IEmailSenderService, EmailSenderService>();
builder.Services.AddScoped<IContractLimitService, ContractLimitService>();
builder.Services.AddScoped<IOrganizationAccessGateService, OrganizationAccessGateService>();

// Consumido por Modulo.Administracion (plugin) -- self-service de usuarios de la
// propia organización, ver ITenantUserAdminService.
builder.Services.AddScoped<ITenantUserAdminService, TenantUserAdminService>();

// Árbol de menús visible del sidebar (Pages/Shared/_Layout.cshtml vía
// SidebarMenuViewComponent) -- ver IMenuNavigationService.
builder.Services.AddScoped<IMenuNavigationService, MenuNavigationService>();

// Conector SAP -- ver ARCHITECTURE.md §6 paso 5, portado de PortalSAP_v2. Scoped salvo
// ISapSessionCache (Singleton, cachea la sesión de Service Layer por Company.Id, ver su
// doc-comment). HanaService/SapConnectionProvider dependen de ICurrentCompanyAccessor
// (claims fijados en el login de tenant, ver Pages/Account/Login.cshtml.cs) -- NUNCA
// Singleton, congelaría la conexión a la primera compañía resuelta (bug real que ya tuvo
// PortalSAP_v2 con el servicio equivalente).
builder.Services.AddScoped<ICurrentCompanyAccessor, CurrentCompanyAccessor>();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<IHanaService, HanaService>();
builder.Services.AddScoped<ISapConnectionProvider, SapConnectionProvider>();
builder.Services.AddSingleton<ISapSessionCache, SapSessionCache>();
// A diferencia de los tres de arriba, no depende de ICurrentCompanyAccessor -- lo usa
// el backoffice de administrador de plataforma (botón "Probar conexión" en Companies),
// que no tiene sesión de tenant.
builder.Services.AddScoped<ISapConnectionTestService, SapConnectionTestService>();

// Consumido por Modulo.Ventas (plugin) -- motor genérico de documentos de venta, ver
// ISalesDocumentService/SalesDocumentTypeCatalog y CLAUDE.md para el alcance de esta
// entrega (Fase 1 del motor genérico Venta/Compra/Inventario).
builder.Services.AddScoped<ICustomerCatalogService, CustomerCatalogService>();
builder.Services.AddScoped<IItemCatalogService, ItemCatalogService>();
builder.Services.AddScoped<IWarehouseCatalogService, WarehouseCatalogService>();
builder.Services.AddScoped<ISalesEmployeeCatalogService, SalesEmployeeCatalogService>();
builder.Services.AddScoped<ISalesDocumentService, SalesDocumentService>();

// Consumido por Modulo.Inventario (plugin) -- motor genérico de documentos de
// inventario, ver IInventoryDocumentService/InventoryDocumentTypeCatalog. Reusa los
// catálogos de Artículo/Almacén ya registrados arriba, no trae catálogos propios.
builder.Services.AddScoped<IInventoryDocumentService, InventoryDocumentService>();

// Consumido por Modulo.Compras (plugin) -- motor genérico de documentos de compra, ver
// IPurchaseDocumentService/PurchaseDocumentTypeCatalog. Reusa Artículo/Almacén ya
// registrados arriba, solo agrega el catálogo de Proveedor (lado OCRD que no usa Venta).
builder.Services.AddScoped<ISupplierCatalogService, SupplierCatalogService>();
builder.Services.AddScoped<IPurchaseDocumentService, PurchaseDocumentService>();

// Esquema default = tenant (sin cambios de comportamiento en /Account, /Home). El
// esquema "PlatformAdmin" es una sesión totalmente aparte -- ver
// Pages/Admin/Login.cshtml.cs -- para que un administrador de plataforma nunca
// comparta cookie/sesión con un usuario de una organización.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddCookie("PlatformAdmin", options =>
    {
        options.LoginPath = "/Admin/Login";
        options.AccessDeniedPath = "/Admin/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// --- Carga de plugins + sync de menú: ANTES de builder.Build() -----------------------
// IModuloPortal.RegisterServices(IServiceCollection) de un plugin (ej. un AddDbContext
// propio) solo puede agregar descriptors mientras builder.Services siga mutable --
// queda de solo lectura apenas se llama Build() (bug real de PortalSAP_v2, ver su
// PluginManager.cs/CLAUDE.md: el primero en usar RegisterServices para algo más que un
// no-op tumbaba el arranque con "The service collection cannot be modified because it
// is read-only"). Por eso todo esto corre acá, no después.
//
// ApplicationPartManager se busca dentro de builder.Services en vez de resolverse por
// DI: AddRazorPages() de más arriba ya lo registró (TryAddSingleton con instancia) --
// mismo mecanismo que expone ASP.NET Core para módulos que lo necesitan antes de Build().
var partManager = builder.Services
    .Where(d => d.ServiceType == typeof(ApplicationPartManager))
    .Select(d => d.ImplementationInstance)
    .OfType<ApplicationPartManager>()
    .FirstOrDefault()
    ?? throw new InvalidOperationException(
        "No se pudo resolver ApplicationPartManager antes de Build() -- ¿se registró AddRazorPages() más arriba?");

using var startupLoggerFactory = LoggerFactory.Create(logging =>
{
    logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    logging.AddConsole();
});

var pluginManager = new PluginManager(startupLoggerFactory.CreateLogger<PluginManager>());
var pluginsFolder = builder.Configuration["Plugins:ArtifactsFolder"] is { Length: > 0 } configuredFolder
    ? (Path.IsPathRooted(configuredFolder) ? configuredFolder : Path.Combine(AppContext.BaseDirectory, configuredFolder))
    : Path.Combine(AppContext.BaseDirectory, "artifacts", "plugins");
pluginManager.DiscoverAndLoad(pluginsFolder, partManager, builder.Services);

// Se registra la instancia ya cargada (no el tipo) por si algo más adelante necesita
// inyectar PluginManager y ver los módulos realmente cargados, no una instancia vacía.
builder.Services.AddSingleton(pluginManager);

// MenuSyncService necesita PortalSaasDbContext, que recién es resoluble con un
// IServiceProvider construido a partir de builder.Services -- todavía no existe
// "el" IServiceProvider real (ese lo crea Build() más abajo), pero se puede construir
// uno transitorio solo para esta operación puntual de arranque (se descarta apenas
// termina; no comparte estado con el provider real de la app).
#pragma warning disable ASP0000 // intencional -- provider transitorio de un solo uso, ver el comentario arriba.
using (var startupProvider = builder.Services.BuildServiceProvider())
#pragma warning restore ASP0000
using (var startupScope = startupProvider.CreateScope())
{
    var startupDb = startupScope.ServiceProvider.GetRequiredService<PortalSaasDbContext>();
    await new MenuSyncService(startupDb).SyncAsync(pluginManager.ModulosCargados);
}

var app = builder.Build();

// Bootstrap de la primera cuenta de administrador de plataforma -- no hay
// autoregistro (ver PlatformAdminSeeder). No levanta Kestrel.
if (args is ["seed-admin", var seedAdminEmail])
{
    await PlatformAdminSeeder.RunAsync(app.Services, seedAdminEmail);
    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();
