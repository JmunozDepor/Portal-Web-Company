using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Modulo.FacturasProveedor.Data;

/// <summary>
/// Base de datos propia del módulo, resuelta self-service vía IExternalDatabaseConnectionService (registrada en
/// ModuloFacturasProveedor.RegisterServices, que decide en runtime UseNpgsql o UseSqlServer). MOTOR DUAL: ninguna
/// columna usa un tipo SQL de un solo motor (HasPrecision para decimales, JSON como texto).
///
/// Nombres de tabla/columna según docs/01-CONVENCION-NOMBRES-BD.md (inglés, plural, snake_case, "id", company_id).
/// Las tablas se nombran explícitamente; las columnas se derivan de la propiedad en snake_case (ApplySnakeCaseColumns)
/// en vez de mapear una por una como Despachos: mismo resultado, sin repetir cada nombre.
/// </summary>
public class FacturasProveedorDbContext : DbContext
{
    public FacturasProveedorDbContext(DbContextOptions<FacturasProveedorDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplySnakeCaseColumns(modelBuilder);
    }

    /// <summary>Columna = nombre de la propiedad en snake_case, salvo que ya tenga uno explícito.</summary>
    private static void ApplySnakeCaseColumns(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.GetColumnName() == property.Name) property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
