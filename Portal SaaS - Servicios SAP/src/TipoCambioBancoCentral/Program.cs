using Microsoft.Extensions.Options;
using Servicios.Common.Configuracion;
using Servicios.Common.Contratos;
using Servicios.Common.Logging;
using Servicios.Common.Seguridad;
using Servicios.TipoCambioBancoCentral;
using Servicios.TipoCambioBancoCentral.BancoCentral;
using Servicios.TipoCambioBancoCentral.Configuracion;
using Servicios.TipoCambioBancoCentral.Contratos;

var builder = Host.CreateApplicationBuilder(args);

// Nombre de servicio Windows con el prefijo NX_DEP_ acordado para toda la familia
// Servicios SAP (ver public/Install-NX_DEP_TipoCambioBancoCentral.ps1) -- debe coincidir
// EXACTO con el que usan los scripts de alta/baja del servicio.
builder.Services.AddWindowsService(options => options.ServiceName = "NX_DEP_TipoCambioBancoCentral");

builder.Services.Configure<List<TipoCambioCompanySetting>>(builder.Configuration.GetSection("Sociedades"));
builder.Services.Configure<BancoCentralOptions>(builder.Configuration.GetSection(BancoCentralOptions.SeccionConfiguracion));
builder.Services.Configure<LogSinkOptions>(builder.Configuration.GetSection(LogSinkOptions.SeccionConfiguracion));
builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection(WorkerOptions.SeccionConfiguracion));

builder.Services.AddSingleton<ISecretoCifradoService, SecretoCifradoService>();
builder.Services.AddSingleton<ITipoCambioCompanyProvider, AppSettingsTipoCambioCompanyProvider>();
builder.Services.AddSingleton<BancoCentralClient>();
builder.Services.AddSingleton<ILogSink>(sp =>
    LogSinkFactory.Create(sp.GetRequiredService<IOptions<LogSinkOptions>>(), prefijoServicio: "tipocambio"));
builder.Services.AddSingleton<LogRetentionPurger>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<RetentionPurgeHostedService>();

var host = builder.Build();
host.Run();
