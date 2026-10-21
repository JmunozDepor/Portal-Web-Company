using Microsoft.EntityFrameworkCore;

namespace Modulo.Despachos.Data;

/// <summary>
/// Base de datos propia del módulo, ajena al SAP de la organización, resuelta
/// self-service vía IExternalDatabaseConnectionService (PortalSaas.Abstractions) --
/// registrada en ModuloDespachos.RegisterServices, que decide en runtime si usa
/// UseNpgsql o UseSqlServer. MOTOR DUAL: ninguna columna usa HasColumnType con un tipo
/// SQL de un solo motor (usar HasPrecision para decimales).
///
/// Nombres de tabla/columna siguen la convención obligatoria de la plataforma
/// (docs/01-CONVENCION-NOMBRES-BD.md): inglés, plural, snake_case, "id" surrogate,
/// company_id en toda tabla de negocio. Las entidades se agregan en las tareas siguientes.
/// </summary>
public class DespachosDbContext : DbContext
{
    public DespachosDbContext(DbContextOptions<DespachosDbContext> options)
        : base(options)
    {
    }
}
