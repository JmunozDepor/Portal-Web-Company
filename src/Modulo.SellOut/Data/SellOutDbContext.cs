using Microsoft.EntityFrameworkCore;
using Modulo.SellOut.Models;

namespace Modulo.SellOut.Data;

/// <summary>
/// Ninguna columna de estas 8 tablas es IDENTITY en CLSELLOUT (confirmado contra el
/// ambiente real, sys.columns.is_identity = 0 en todas) -- los Id* son claves naturales
/// asignadas a mano (por el usuario o por una carga historica), asi que las pantallas de
/// alta piden el Id como campo editable, no autogenerado. Geografia/GrupoRetail/
/// Localizacion tampoco tienen PRIMARY KEY declarada en la base (solo Cliente/
/// ClienteSku/Departamento/Sucursal/CfgClienteLayoutInput la tienen) -- HasKey abajo la
/// fija igual del lado de EF Core para las 8, es la unica forma de que SaveChanges sepa
/// distinguir INSERT de UPDATE.
/// </summary>
public class SellOutDbContext : DbContext
{
    public SellOutDbContext(DbContextOptions<SellOutDbContext> options) : base(options) { }

    public DbSet<Geografia> Geografias => Set<Geografia>();
    public DbSet<Localizacion> Localizaciones => Set<Localizacion>();
    public DbSet<GrupoRetail> GruposRetail => Set<GrupoRetail>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Sucursal> Sucursales => Set<Sucursal>();
    public DbSet<Departamento> Departamentos => Set<Departamento>();
    public DbSet<ClienteSku> ClienteSkus => Set<ClienteSku>();
    public DbSet<CfgClienteLayoutInput> ConfiguracionesLayout => Set<CfgClienteLayoutInput>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Geografia>().HasKey(e => e.IDGeografia);
        modelBuilder.Entity<Localizacion>().HasKey(e => e.IDLocalizacion);
        modelBuilder.Entity<GrupoRetail>().HasKey(e => e.IDGrupoRetail);
        modelBuilder.Entity<Cliente>().HasKey(e => e.IdCliente);
        modelBuilder.Entity<Sucursal>().HasKey(e => new { e.IdCliente, e.IdSucursal });
        modelBuilder.Entity<Departamento>().HasKey(e => new { e.IdCliente, e.ClienteDepartamento });
        modelBuilder.Entity<ClienteSku>().HasKey(e => new { e.IdCliente, e.Sku });
        modelBuilder.Entity<CfgClienteLayoutInput>().HasKey(e => new { e.IdCliente, e.TipoArchivo });

        base.OnModelCreating(modelBuilder);
    }
}
