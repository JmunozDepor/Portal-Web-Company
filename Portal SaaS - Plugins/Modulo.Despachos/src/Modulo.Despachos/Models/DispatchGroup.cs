namespace Modulo.Despachos.Models;

/// <summary>
/// Agrupación: transferencias SAP a una bodega sucursal agrupadas para un día de despacho (corte por horario: cada
/// "Generar" crea el grupo siguiente de la sucursal). Guarda la foto de los datos de cada transferencia; no emite
/// documento tributario (la guía sale al despachar el manifiesto).
/// </summary>
public class DispatchGroup : AuditedEntity
{
    /// <summary>Correlativo por compañía; se muestra como AT-000123 (ver Dominio.DispatchGroupCode).</summary>
    public int Number { get; set; }
    public string WarehouseCode { get; set; } = "";
    public string BranchName { get; set; } = "";
    public DateOnly DispatchDate { get; set; }
    /// <summary>N° de grupo del día para la sucursal (1, 2, ...).</summary>
    public int Sequence { get; set; }
    public string Status { get; set; } = DispatchGroupStatuses.Armed;
    public string? AnnulReason { get; set; }
    public List<DispatchGroupTransfer> Transfers { get; set; } = new();
}

/// <summary>Transferencia (OWTR, objeto 67) dentro de una agrupación, con los datos copiados al agruparla.</summary>
public class DispatchGroupTransfer
{
    public long Id { get; set; }
    public Guid CompanyId { get; set; }
    public long DispatchGroupId { get; set; }
    public int SapDocEntry { get; set; }
    public int SapDocNum { get; set; }
    public string? Lpn { get; set; }
    public decimal Units { get; set; }
    public DateOnly DocumentDate { get; set; }
}
