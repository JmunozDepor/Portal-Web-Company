using PortalSaas.Data;

namespace PortalSaas.Core.Tests.TestHelpers;

/// <summary>
/// Implementación fija de IOrganizationScopeProvider para tests que no dependen de
/// aislamiento por organización -- CurrentOrganizationId siempre null, mismo criterio
/// que el backoffice de plataforma (sin filtro). Ver Task 2 de
/// docs/superpowers/plans/2026-08-11-escalabilidad-horizontal.md.
/// </summary>
public sealed class NullOrganizationScopeProvider : IOrganizationScopeProvider
{
    public Guid? CurrentOrganizationId => null;
}
