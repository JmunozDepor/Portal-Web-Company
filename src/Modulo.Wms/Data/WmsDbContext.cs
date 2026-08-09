using Microsoft.EntityFrameworkCore;
using Modulo.Wms.Models;

namespace Modulo.Wms.Data;

/// <summary>
/// Base de datos propia del módulo, ajena al SAP de la organización, resuelta
/// self-service vía IExternalDatabaseConnectionService (PortalSaas.Abstractions) --
/// mismo patrón que RendicionesDbContext. MOTOR DUAL: ninguna columna usa
/// HasColumnType con un tipo específico de un solo motor.
///
/// Alcance Fase 1 (ver ARQUITECTURA.md de este repo): solo las 3 tablas de
/// administración ya confirmadas como config real contra el DDL de WMS_Suite
/// (mapeo de campos, config operativa del servicio, heartbeat). El maestro SAP
/// neutral y las tablas wms_oracle_export_*/wms_oracle_inbound_*/wms_oracle_stage_*
/// quedan para Fase 2.
///
/// Nombres de tabla/columna siguen la convención obligatoria de la plataforma
/// (docs/01-CONVENCION-NOMBRES-BD.md): inglés, plural, snake_case, "id" surrogate,
/// company_id en toda tabla de negocio.
/// </summary>
public class WmsDbContext : DbContext
{
    public WmsDbContext(DbContextOptions<WmsDbContext> options)
        : base(options)
    {
    }

    public DbSet<WmsFieldMapping> FieldMappings => Set<WmsFieldMapping>();
    public DbSet<WmsServiceConfig> ServiceConfigs => Set<WmsServiceConfig>();
    public DbSet<WmsServiceHeartbeat> ServiceHeartbeats => Set<WmsServiceHeartbeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WmsFieldMapping>(e =>
        {
            e.ToTable("wms_oracle_field_mappings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.MapperKey).HasColumnName("mapper_key").HasMaxLength(50).IsRequired();
            e.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(100).IsRequired();
            e.Property(x => x.ValueTemplate).HasColumnName("value_template").HasMaxLength(500).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(50);
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_wms_oracle_field_mappings_company_id");
            e.HasIndex(x => new { x.CompanyId, x.MapperKey, x.FieldName })
                .IsUnique()
                .HasDatabaseName("uk_wms_oracle_field_mappings_key");
        });

        modelBuilder.Entity<WmsServiceConfig>(e =>
        {
            e.ToTable("wms_oracle_service_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ConfigKey).HasColumnName("config_key").HasMaxLength(150).IsRequired();
            e.Property(x => x.ConfigValue).HasColumnName("config_value").HasMaxLength(500);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by").HasMaxLength(50);
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_wms_oracle_service_configs_company_id");
            e.HasIndex(x => new { x.CompanyId, x.ConfigKey })
                .IsUnique()
                .HasDatabaseName("uk_wms_oracle_service_configs_key");
        });

        modelBuilder.Entity<WmsServiceHeartbeat>(e =>
        {
            e.ToTable("wms_oracle_service_heartbeats");
            e.HasKey(x => new { x.CompanyId, x.ProcessorKey });
            e.Property(x => x.CompanyId).HasColumnName("company_id");
            e.Property(x => x.ProcessorKey).HasColumnName("processor_key").HasMaxLength(100);
            e.Property(x => x.LastRunAt).HasColumnName("last_run_at");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20);
            e.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
            e.Property(x => x.ConfigSourceEffective).HasColumnName("config_source_effective").HasMaxLength(20);
        });
    }
}
