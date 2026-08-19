using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Servicios.Common.Logging;
using Servicios.Common.Seguridad;
using Servicios.TransferenciaAutomatica;

var builder = Host.CreateApplicationBuilder(args);

// Nombre de servicio Windows con el prefijo NX_DEP_ acordado para toda la familia
// Servicios SAP (ver public/Install-NX_DEP_TransferenciaAutomatica.ps1) -- debe
// coincidir EXACTO con el que usan los scripts de alta/baja del servicio.
builder.Services.AddWindowsService(options => options.ServiceName = "NX_DEP_TransferenciaAutomatica");

builder.Services.Configure<List<SociedadSetting>>(builder.Configuration.GetSection("Sociedades"));
builder.Services.Configure<LogSinkOptions>(builder.Configuration.GetSection(LogSinkOptions.SeccionConfiguracion));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SeccionConfiguracion));

builder.Services.AddSingleton<ISecretoCifradoService, SecretoCifradoService>();
builder.Services.AddSingleton<ICompanyProvider, AppSettingsCompanyProvider>();
builder.Services.AddSingleton<ILogSink>(sp =>
    LogSinkFactory.Create(sp.GetRequiredService<IOptions<LogSinkOptions>>(), prefijoServicio: "transferencia"));
builder.Services.AddSingleton<LogRetentionPurger>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<RetentionPurgeHostedService>();

var host = builder.Build();
host.Run();
