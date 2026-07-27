namespace PortalSaas.Abstractions.Modelos;

/// <summary>Fila de "clientes conectados" para el backoffice de plataforma -- ver IUserSessionService.ListActiveAsync.</summary>
public sealed record UserSessionDto(
    Guid SessionId,
    Guid OrganizationId,
    string OrganizationName,
    Guid UserId,
    string Username,
    string Email,
    Guid? CompanyId,
    string? CompanyName,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt);
