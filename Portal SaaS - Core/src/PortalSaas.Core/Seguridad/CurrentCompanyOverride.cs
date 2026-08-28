using PortalSaas.Abstractions.Contratos;

namespace PortalSaas.Core.Seguridad;

public sealed class CurrentCompanyOverride : ICurrentCompanyOverride
{
    public Guid? CompanyId { get; private set; }

    public void Set(Guid companyId) => CompanyId = companyId;

    public void Clear() => CompanyId = null;
}
