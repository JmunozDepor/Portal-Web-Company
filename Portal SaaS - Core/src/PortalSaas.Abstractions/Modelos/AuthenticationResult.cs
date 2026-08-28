namespace PortalSaas.Abstractions.Modelos;

/// <summary>Resultado de un intento de autenticación -- ver IAuthenticationService.</summary>
public sealed record AuthenticationResult(bool IsSuccess, string? Reason, Guid? UserId)
{
    public static AuthenticationResult Success(Guid userId) => new(true, null, userId);
    public static AuthenticationResult Failure(string reason) => new(false, reason, null);
}
