using PortalSaas.Abstractions.Modelos;

namespace PortalSaas.Abstractions.Contratos;

/// <summary>
/// Resuelve el contacto (organización, email, preferencia de notificaciones) de
/// CUALQUIER usuario de la plataforma por su Id -- a diferencia de
/// ICurrentUserContext (solo el usuario del request actual) o ITenantUserAdminService
/// (acotado a la organización del usuario logueado en sesión), este contrato no
/// depende de haber una sesión HTTP activa: lo usan procesos como notificaciones de
/// aprobación o background jobs que necesitan el email de un tercero (ej. el
/// aprobador de un nivel), o que corren sin ningún usuario logueado (ej. un
/// recordatorio diario programado). userId siempre proviene de datos ya resueltos y
/// de confianza del propio plugin (ej. ExpenseApprovalGroupLevel.UserId), nunca de un
/// valor recibido directo de la UI sin validar antes.
/// </summary>
public interface IUserContactLookupService
{
    Task<UserContactDto?> GetContactAsync(Guid userId, CancellationToken ct = default);
}
