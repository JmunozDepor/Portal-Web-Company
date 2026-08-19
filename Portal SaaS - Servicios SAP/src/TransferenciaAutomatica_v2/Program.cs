using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Servicios.Common.Logging;
using Servicios.Common.Seguridad;
using Servicios.TransferenciaAutomatica_v2;
using Servicios.TransferenciaAutomatica_v2.Domain;
using Servicios.TransferenciaAutomatica_v2.Estado;

var builder = Host.CreateApplicationBuilder(args);

// Nombre de servicio Windows con el prefijo NX_DEP_ acordado para toda la familia
// Servicios SAP (ver public/NX_DEP_TransferenciaAutomatica_v2-Install.ps1) -- debe
// coincidir EXACTO con el que usan los scripts de alta/baja del servicio. Convive con
// NX_DEP_TransferenciaAutomatica (v1) como servicio Windows separado -- no comparten
// proceso ni configuración.
builder.Services.AddWindowsService(options => options.ServiceName = "NX_DEP_TransferenciaAutomatica_v2");

builder.Services.Configure<List<SociedadSetting>>(builder.Configuration.GetSection("Sociedades"));
builder.Services.Configure<LogSinkOptions>(builder.Configuration.GetSection(LogSinkOptions.SeccionConfiguracion));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SeccionConfiguracion));
builder.Services.Configure<AsignacionParcialOptions>(builder.Configuration.GetSection(AsignacionParcialOptions.SeccionConfiguracion));

builder.Services.AddSingleton<ISecretoCifradoService, SecretoCifradoService>();
builder.Services.AddSingleton<ICompanyProvider, AppSettingsCompanyProvider>();
builder.Services.AddSingleton<ILogSink>(sp =>
    LogSinkFactory.Create(sp.GetRequiredService<IOptions<LogSinkOptions>>(), prefijoServicio: "transferencia_v2"));
builder.Services.AddSingleton<LogRetentionPurger>();
builder.Services.AddSingleton(sp =>
    new IntentosAsignacionStore(sp.GetRequiredService<IOptions<AsignacionParcialOptions>>().Value.Folder));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<RetentionPurgeHostedService>();

var host = builder.Build();
host.Run();
