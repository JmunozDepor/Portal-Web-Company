using PortalSaas.Abstractions.Contratos;

namespace Modulo.FacturasProveedor.Tests.Fakes;

public sealed class FakeCurrentCompanyAccessor : ICurrentCompanyAccessor
{
    public Guid CompanyId { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "TEST";
    public string Database { get; set; } = "TEST_DB";
    public string ServiceLayerUrl { get; set; } = string.Empty;
    public string Country { get; set; } = "CL";
    public bool HasCompany { get; set; } = true;
}
