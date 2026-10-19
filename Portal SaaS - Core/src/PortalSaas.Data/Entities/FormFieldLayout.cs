namespace PortalSaas.Data.Entities;

/// <summary>
/// Campos ocultos de un formulario genérico de documento (Venta/Compra/Inventario) --
/// "formulario personalizado" (2026-10-19). Por compañía (regla dura: personalización
/// por CompanyId, ej. Marca/Tipo de gasto son dimensiones propias de cada SAP):
/// UserId null = base que define el admin de la organización para todos; UserId con
/// valor = versión propia del usuario, que reemplaza a la base entera (sin fila propia
/// el usuario ve la base). Resuelve organization_id vía Company.
/// </summary>
public sealed class FormFieldLayout
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Null = base de la compañía (admin).</summary>
    public Guid? UserId { get; set; }
    public User? User { get; set; }

    /// <summary>Ver DocumentFormFieldCatalog (Abstractions), ej. "ventas.documento".</summary>
    public string FormKey { get; set; } = null!;

    /// <summary>Claves de campo ocultas separadas por coma -- siempre filtradas contra el catálogo.</summary>
    public string HiddenFields { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
