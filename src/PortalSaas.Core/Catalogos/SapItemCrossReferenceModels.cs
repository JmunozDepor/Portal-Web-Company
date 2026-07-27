namespace PortalSaas.Core.Catalogos;

/// <summary>
/// Wire format de Service Layer para el recurso estándar AlternateCatNum -- nunca se
/// expone fuera de Core. U_GSP_CATALOGDESC es un UDF confirmado contra el ambiente real
/// del cliente en la referencia (prefijo GSP, de otra integración previa, no de este
/// portal -- no renombrar si se reconfirma igual acá).
/// </summary>
internal sealed class SapAlternateCatNum
{
    public string? ItemCode { get; set; }
    public string? CardCode { get; set; }
    public string? Substitute { get; set; }
    public string? Description { get; set; }
    public string? U_GSP_CATALOGDESC { get; set; }
}
