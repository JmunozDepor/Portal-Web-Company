using System.ComponentModel.DataAnnotations;

namespace PortalSaas.Abstractions.Modelos;

public enum ExternalConnectionKind
{
    Database,
    HttpApi,
}

/// <summary>Requisito de conexión externa declarado por un módulo.</summary>
public sealed record ExternalConnectionRequirement(
    string Purpose,
    string DisplayName,
    ExternalConnectionKind Kind,
    bool Required);

/// <summary>Vista de lectura de una conexión del catálogo. NUNCA incluye el secreto.</summary>
public sealed record ExternalConnectionDto(
    long Id,
    Guid CompanyId,
    string Nombre,
    string Tipo,
    string? Host,
    string? BaseUrl,
    int? Port,
    string? DatabaseName,
    string? TechnicalUsername,
    string? ConfiguracionExtra,
    bool IsActive);

/// <summary>Entrada de alta/edición. Secreto write-only: null o vacío en edición = conservar.</summary>
public sealed class ExternalConnectionEditModel
{
    [Required]
    [StringLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    public string Tipo { get; set; } = ExternalConnectionType.DbSqlServer;

    [StringLength(200)]
    public string? Host { get; set; }

    [StringLength(500)]
    public string? BaseUrl { get; set; }

    [Range(1, 65535)]
    public int? Port { get; set; }

    [StringLength(100)]
    public string? DatabaseName { get; set; }

    [StringLength(100)]
    public string? TechnicalUsername { get; set; }
    public string? TechnicalSecretKey { get; set; }
    public string? ConfiguracionExtra { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Una fila de la grilla de bindings módulo→conexión de una compañía.</summary>
public sealed record ModuleConnectionBindingDto(
    string ModuleCode,
    string ModuleName,
    string Purpose,
    string PurposeDisplayName,
    ExternalConnectionKind Kind,
    bool Required,
    long? ConnectionId,
    string? ConnectionNombre);

public sealed record ConnectionTestResultDto(bool Ok, string? Error, long? ElapsedMs);
