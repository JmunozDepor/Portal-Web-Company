namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// DTOs del contrato entre PortalSaas.Host (que expone los endpoints HTTP, ver
/// AuditoriaInventarioInboundEndpoints en el Host) y Modulo.AuditoriaInventario (que
/// implementa IAuditoriaInventarioApiService) -- el único punto de contacto entre el
/// portal y la PWA de captura de inventario (repo separado, equipo mobile).
/// </summary>
public sealed record CaptureLoginResult(string Token, DateTimeOffset ExpiresAt, string DisplayName);

/// <summary>Resultado de validar un token opaco -- null (no un record vacío) representa "inválido/expirado" en el llamador.</summary>
public sealed record CaptureTokenInfo(Guid CompanyId, long CaptureUserId);

public sealed record CaptureProductoDto(long Id, string Barcode, string ProductCode, string? Description, string? Brand, string? Line);

/// <summary>Página de maestro -- keyset pagination por Id, HasMore indica si el cliente debe seguir pidiendo con AfterId = Items[^1].Id.</summary>
public sealed record CaptureMaestroPage(IReadOnlyList<CaptureProductoDto> Items, bool HasMore);

public sealed record CaptureSucursalDto(long Id, string BranchCode, string Name);

public sealed record CaptureSectorDto(long Id, string Name);

public sealed record CaptureSesionUpsert(Guid Id, long BranchId, string InventoryNumber, DateTimeOffset StartedAt, string Status, bool ValidateAgainstMaster);

public sealed record CaptureItemDto(Guid Id, Guid SessionId, long SectorId, string Barcode, string? ProductCode, int Quantity, bool? InMaster, DateTimeOffset CapturedAt);
