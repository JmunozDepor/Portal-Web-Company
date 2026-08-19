using Microsoft.EntityFrameworkCore;
using Modulo.GestionDistribucionGastos.Models;

namespace Modulo.GestionDistribucionGastos.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<StagingCentralizacion> StagingCentralizacion => Set<StagingCentralizacion>();
    public DbSet<DistribucionFinal> DistribucionFinal => Set<DistribucionFinal>();
    public DbSet<ReglaDistribucion> ReglasDistribucion => Set<ReglaDistribucion>();
    public DbSet<CuentaEnTrabajo> CuentaEnTrabajo => Set<CuentaEnTrabajo>();
    public DbSet<CuentaAprobada> CuentaAprobada => Set<CuentaAprobada>();
    public DbSet<CierreMes> CierreMes => Set<CierreMes>();
    public DbSet<MaestroSucursal> MaestroSucursal => Set<MaestroSucursal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CuentaEnTrabajo>()
            .HasKey(c => new { c.AnioMes, c.NroCuenta });

        modelBuilder.Entity<CuentaAprobada>()
            .HasKey(c => new { c.AnioMes, c.NroCuenta });

        modelBuilder.Entity<CierreMes>()
            .HasKey(c => c.AnioMes);

        modelBuilder.Entity<MaestroSucursal>()
            .HasKey(m => m.CodSucursal);

        base.OnModelCreating(modelBuilder);
    }
}
