namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// DTOs de ITenantUserAdminService -- self-service de usuarios de la propia
/// organización desde Modulo.Administracion (plugin). Todo acotado por
/// ICurrentUserContext.OrganizationId, nunca por un id recibido de la UI.
/// </summary>
public sealed class TenantUserDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required bool IsAdmin { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsLocked { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}

public sealed class TenantUserDetailDto
{
    public required Guid Id { get; init; }
    public required string Username { get; init; }
    public required string Email { get; init; }
    public required bool IsAdmin { get; init; }
    public required bool IsActive { get; init; }
    public required bool IsLocked { get; init; }

    /// <summary>Compañía que SelectCompany preselecciona al loguearse -- ver UserPreference.DefaultCompanyId. Null = sin default, el usuario elige como siempre.</summary>
    public Guid? DefaultCompanyId { get; init; }
}

/// <summary>Resultado de una operación de escritura -- mismo estilo IsSuccess/Reason que AuthenticationResult/LimitCheckResult.</summary>
public sealed class TenantUserOperationResult
{
    public required bool IsSuccess { get; init; }
    public string? Reason { get; init; }
    public Guid? UserId { get; init; }

    public static TenantUserOperationResult Success(Guid? userId = null) => new() { IsSuccess = true, UserId = userId };
    public static TenantUserOperationResult Failure(string reason) => new() { IsSuccess = false, Reason = reason };
}

/// <summary>Resultado de generar una contraseña nueva -- igual que TenantUserOperationResult, más el valor en texto plano para que el llamador decida cómo entregarla (correo o pantalla).</summary>
public sealed class TenantUserPasswordResult
{
    public required bool IsSuccess { get; init; }
    public string? Reason { get; init; }
    public string? NewPassword { get; init; }

    public static TenantUserPasswordResult Success(string newPassword) => new() { IsSuccess = true, NewPassword = newPassword };
    public static TenantUserPasswordResult Failure(string reason) => new() { IsSuccess = false, Reason = reason };
}

public sealed class CompanyOptionDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
}

public sealed class MenuGroupOptionDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
}

public sealed class ProfileOptionDto
{
    public required long Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>Nodo de menú final (hoja, con PageRoute) -- lista plana, sin árbol jerárquico (alcance reducido de este plugin).</summary>
public sealed class LeafMenuDto
{
    public required long Id { get; init; }
    public required string OriginModule { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
}

public sealed class UserPermissionsDto
{
    public required IReadOnlyList<long> MenuGroupIds { get; init; }
    public required IReadOnlyDictionary<long, long?> ProfileByMenu { get; init; }
}
