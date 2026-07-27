using Microsoft.Extensions.DependencyInjection;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;

namespace Modulo.ImportacionGenerica;

/// <summary>
/// Importador masivo de documentos Venta/Compra/Inventario desde un archivo Excel
/// estándar por columnas -- portado de Modulo.ImportacionGenerica en
/// referencia-original/PortalSAP_v2. Solo administrador de organización (mismo gate
/// que Modulo.Administracion) -- self-service, acotado a la organización actual, no al
/// operador de plataforma.
/// </summary>
public sealed class ModuloImportacionGenerica : IModuloPortal
{
    public string ModuleCode => "ImportacionGenerica";
    public string Name => "Importación Genérica";
    public string Version => "1.0.0";

    public IEnumerable<MenuItemDefinition> GetMenu()
    {
        yield return new MenuItemDefinition { Code = "raiz", ParentCode = null, Name = "Importación Genérica", Icon = "bi bi-file-earmark-arrow-up", PageRoute = null, Order = 850 };
        yield return new MenuItemDefinition { Code = "importar", ParentCode = "raiz", Name = "Importar documentos", Icon = "bi bi-upload", PageRoute = "/importacion-generica/importar", Order = 1 };
        yield return new MenuItemDefinition { Code = "configuracion", ParentCode = "raiz", Name = "Configuración", Icon = "bi bi-sliders", PageRoute = "/importacion-generica/configuracion", Order = 2 };
        yield return new MenuItemDefinition { Code = "campos-usuario", ParentCode = "raiz", Name = "Campos de usuario", Icon = "bi bi-tags", PageRoute = "/importacion-generica/campos-usuario", Order = 3 };
    }

    public void RegisterServices(IServiceCollection services)
    {
        // Los servicios (IGenericImportService/IGenericImportConfigService/
        // IGenericImportUserFieldService/IGenericImportProgressStore) ya los registra
        // el Host (son del Core) -- este plugin no trae servicios propios.
    }
}
