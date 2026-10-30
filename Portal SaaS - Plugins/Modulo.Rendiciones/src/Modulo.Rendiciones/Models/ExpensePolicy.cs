namespace Modulo.Rendiciones.Models;

/// <summary>
/// Tope de monto por tipo de gasto, opcionalmente acotado a una ciudad y/o a un perfil de
/// política (Task 5, ver Ciudades/PerfilesPolitica). Puede haber varias filas por
/// (CompanyId, ExpenseTypeId) -- una "default" sin ciudad/perfil y otras más específicas --
/// la base impide repetir la misma combinación exacta (ver RendicionesDbContext, índice
/// único sobre columnas computadas). Cuál de las filas aplica a un gasto concreto lo
/// resuelve el motor de resolución de Task 6; este modelo es puramente de datos, no decide
/// nada. Ya NO es bloqueante: superar el tope siempre es solo una advertencia (removido
/// 2026-10-29, ver ExpensePolicyService.ValidateAsync). Portado de PoliticaGasto
/// (PortalSAP_v2).
/// </summary>
public class ExpensePolicy
{
    public long Id { get; set; }

    public required Guid CompanyId { get; set; }

    public required long ExpenseTypeId { get; set; }

    public ExpenseType? ExpenseType { get; set; }

    /// <summary>Null = aplica a cualquier ciudad ("default" para el tipo de gasto). Siempre
    /// null cuando Period == ExpensePolicyPeriod.Weekly.</summary>
    public long? CityId { get; set; }

    public City? City { get; set; }

    /// <summary>Null = aplica a cualquier perfil (incluidos usuarios sin perfil asignado).</summary>
    public long? PolicyProfileId { get; set; }

    public PolicyProfile? PolicyProfile { get; set; }

    /// <summary>ExpensePolicyPeriod.PerDocument (tope por gasto) o .Weekly (tope acumulado por semana).</summary>
    public required string Period { get; set; }

    /// <summary>Null = sin tope de monto.</summary>
    public decimal? MaxAmount { get; set; }

    public bool IsActive { get; set; } = true;
}
