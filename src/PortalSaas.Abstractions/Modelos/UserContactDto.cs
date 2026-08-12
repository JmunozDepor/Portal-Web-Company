namespace PortalSaas.Abstractions.Modelos;

/// <summary>Datos de contacto mínimos de un usuario -- ver IUserContactLookupService.</summary>
public sealed record UserContactDto(Guid OrganizationId, string Email, bool EmailNotificationsEnabled);
