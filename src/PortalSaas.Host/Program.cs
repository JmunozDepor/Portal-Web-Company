using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Core.Comercial;
using PortalSaas.Core.Correo;
using PortalSaas.Core.Infraestructura;
using PortalSaas.Core.Seguridad;
using PortalSaas.Core.Usuarios;
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
builder.Services.AddScoped<ISecretoCifradoService, SecretoCifradoService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IPlatformAdminAuthenticationService, PlatformAdminAuthenticationService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IUserPreferenceService, UserPreferenceService>();
builder.Services.AddScoped<IEmailSenderService, EmailSenderService>();
builder.Services.AddScoped<IContractLimitService, ContractLimitService>();
builder.Services.AddScoped<IOrganizationAccessGateService, OrganizationAccessGateService>();

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
