namespace PortalSaas.Host.Pages.Admin.Organizations;

public sealed class OrganizationSubnavViewModel
{
    public required Guid OrganizationId { get; init; }

    public required string OrganizationName { get; init; }

    public required string Mode { get; init; }

    public required string ActivePage { get; init; }
}
