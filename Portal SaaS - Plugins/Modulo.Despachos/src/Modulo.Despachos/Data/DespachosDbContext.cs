using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Modulo.Despachos.Models;

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
/// company_id en toda tabla de negocio.
/// </summary>
public class DespachosDbContext : DbContext
{
    public DespachosDbContext(DbContextOptions<DespachosDbContext> options)
        : base(options)
    {
    }

    public DbSet<Carrier> Carriers => Set<Carrier>();
    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Commune> Communes => Set<Commune>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<ZoneCommune> ZoneCommunes => Set<ZoneCommune>();
    public DbSet<Origin> Origins => Set<Origin>();
    public DbSet<RejectionReason> RejectionReasons => Set<RejectionReason>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateSurcharge> RateSurcharges => Set<RateSurcharge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Carrier>(e =>
        {
            Base(e, "carriers");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(12);
            e.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_carriers_company_id_code");
        });

        modelBuilder.Entity<VehicleType>(e =>
        {
            Base(e, "vehicle_types");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.CapacityM3).HasColumnName("capacity_m3").HasPrecision(9, 2);
            e.Property(x => x.CapacityKg).HasColumnName("capacity_kg").HasPrecision(9, 2);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_vehicle_types_company_id_code");
        });

        modelBuilder.Entity<Vehicle>(e =>
        {
            Base(e, "vehicles");
            e.Property(x => x.Plate).HasColumnName("plate").HasMaxLength(10).IsRequired();
            e.Property(x => x.VehicleTypeId).HasColumnName("vehicle_type_id");
            e.Property(x => x.CarrierId).HasColumnName("carrier_id");
            e.Property(x => x.CapacityM3).HasColumnName("capacity_m3").HasPrecision(9, 2);
            e.Property(x => x.IsTrailer).HasColumnName("is_trailer");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Plate }).IsUnique().HasDatabaseName("uq_vehicles_company_id_plate");
            e.HasIndex(x => x.VehicleTypeId).HasDatabaseName("ix_vehicles_vehicle_type_id");
            e.HasIndex(x => x.CarrierId).HasDatabaseName("ix_vehicles_carrier_id");
            e.HasOne<VehicleType>().WithMany().HasForeignKey(x => x.VehicleTypeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_vehicles_vehicle_types");
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_vehicles_carriers");
        });

        modelBuilder.Entity<Driver>(e =>
        {
            Base(e, "drivers");
            e.Property(x => x.TaxId).HasColumnName("tax_id").HasMaxLength(12).IsRequired();
            e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(150).IsRequired();
            e.Property(x => x.CarrierId).HasColumnName("carrier_id");
            e.Property(x => x.LicenseClass).HasColumnName("license_class").HasMaxLength(10);
            e.Property(x => x.LicenseExpiresAt).HasColumnName("license_expires_at");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.TaxId }).IsUnique().HasDatabaseName("uq_drivers_company_id_tax_id");
            e.HasIndex(x => x.CarrierId).HasDatabaseName("ix_drivers_carrier_id");
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_drivers_carriers");
        });

        modelBuilder.Entity<Shift>(e =>
        {
            Base(e, "shifts");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_shifts_company_id_code");
        });

        modelBuilder.Entity<Commune>(e =>
        {
            Base(e, "communes");
            e.Property(x => x.CutCode).HasColumnName("cut_code").HasMaxLength(5).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.RegionCode).HasColumnName("region_code").HasMaxLength(2).IsRequired();
            e.Property(x => x.RegionName).HasColumnName("region_name").HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.CompanyId, x.CutCode }).IsUnique().HasDatabaseName("uq_communes_company_id_cut_code");
        });

        modelBuilder.Entity<Zone>(e =>
        {
            Base(e, "zones");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_zones_company_id_code");
            e.HasMany(x => x.Communes).WithOne().HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_zone_communes_zones");
        });

        modelBuilder.Entity<ZoneCommune>(e =>
        {
            e.ToTable("zone_communes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ZoneId).HasColumnName("zone_id");
            e.Property(x => x.CommuneId).HasColumnName("commune_id");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_zone_communes_company_id");
            e.HasIndex(x => new { x.ZoneId, x.CommuneId }).IsUnique().HasDatabaseName("uq_zone_communes_zone_id_commune_id");
            e.HasIndex(x => x.CommuneId).HasDatabaseName("ix_zone_communes_commune_id");
            e.HasOne<Commune>().WithMany().HasForeignKey(x => x.CommuneId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_zone_communes_communes");
        });

        modelBuilder.Entity<Origin>(e =>
        {
            Base(e, "origins");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.Address).HasColumnName("address").HasMaxLength(70);
            e.Property(x => x.CommuneId).HasColumnName("commune_id");
            e.Property(x => x.SapWarehouseCode).HasColumnName("sap_warehouse_code").HasMaxLength(20);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_origins_company_id_code");
            e.HasIndex(x => x.CommuneId).HasDatabaseName("ix_origins_commune_id");
            e.HasOne<Commune>().WithMany().HasForeignKey(x => x.CommuneId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_origins_communes");
        });

        modelBuilder.Entity<RejectionReason>(e =>
        {
            Base(e, "rejection_reasons");
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(x => x.AppliesTo).HasColumnName("applies_to").HasMaxLength(20).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("uq_rejection_reasons_company_id_code");
        });

        modelBuilder.Entity<Holiday>(e =>
        {
            Base(e, "holidays");
            e.Property(x => x.Date).HasColumnName("date");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.CompanyId, x.Date }).IsUnique().HasDatabaseName("uq_holidays_company_id_date");
        });

        modelBuilder.Entity<NotificationRecipient>(e =>
        {
            Base(e, "notification_recipients");
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.UserId }).IsUnique().HasDatabaseName("uq_notification_recipients_company_id_user_id");
        });

        modelBuilder.Entity<RateCard>(e =>
        {
            Base(e, "rate_cards");
            e.Property(x => x.CarrierId).HasColumnName("carrier_id");
            e.Property(x => x.ShiftId).HasColumnName("shift_id");
            e.Property(x => x.RegionCode).HasColumnName("region_code").HasMaxLength(2);
            e.Property(x => x.ZoneId).HasColumnName("zone_id");
            e.Property(x => x.CommuneId).HasColumnName("commune_id");
            e.Property(x => x.VehicleTypeId).HasColumnName("vehicle_type_id");
            e.Property(x => x.M3From).HasColumnName("m3_from").HasPrecision(9, 2);
            e.Property(x => x.M3To).HasColumnName("m3_to").HasPrecision(9, 2);
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.ValidTo).HasColumnName("valid_to");
            e.Property(x => x.CalculationMethod).HasColumnName("calculation_method").HasMaxLength(30).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
            e.Property(x => x.MinAmount).HasColumnName("min_amount").HasPrecision(18, 2);
            e.Property(x => x.ParametersJson).HasColumnName("parameters_json");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => x.CarrierId).HasDatabaseName("ix_rate_cards_carrier_id");
            e.HasIndex(x => x.ShiftId).HasDatabaseName("ix_rate_cards_shift_id");
            e.HasIndex(x => x.ZoneId).HasDatabaseName("ix_rate_cards_zone_id");
            e.HasIndex(x => x.CommuneId).HasDatabaseName("ix_rate_cards_commune_id");
            e.HasIndex(x => x.VehicleTypeId).HasDatabaseName("ix_rate_cards_vehicle_type_id");
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_rate_cards_carriers");
            e.HasOne<Shift>().WithMany().HasForeignKey(x => x.ShiftId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_rate_cards_shifts");
            e.HasOne<Zone>().WithMany().HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_rate_cards_zones");
            e.HasOne<Commune>().WithMany().HasForeignKey(x => x.CommuneId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_rate_cards_communes");
            e.HasOne<VehicleType>().WithMany().HasForeignKey(x => x.VehicleTypeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_rate_cards_vehicle_types");
        });

        modelBuilder.Entity<RateSurcharge>(e =>
        {
            Base(e, "rate_surcharges");
            e.Property(x => x.CarrierId).HasColumnName("carrier_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(150).IsRequired();
            e.Property(x => x.ValidFrom).HasColumnName("valid_from");
            e.Property(x => x.ValidTo).HasColumnName("valid_to");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.ConditionType).HasColumnName("condition_type").HasMaxLength(40).IsRequired();
            e.Property(x => x.ConditionParametersJson).HasColumnName("condition_parameters_json");
            e.Property(x => x.EffectType).HasColumnName("effect_type").HasMaxLength(30).IsRequired();
            e.Property(x => x.Value).HasColumnName("value").HasPrecision(18, 2);
            e.HasIndex(x => x.CarrierId).HasDatabaseName("ix_rate_surcharges_carrier_id");
            e.HasOne<Carrier>().WithMany().HasForeignKey(x => x.CarrierId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_rate_surcharges_carriers");
        });
    }

    /// <summary>Tabla, id, company_id (+ índice) y auditoría comunes a los maestros.</summary>
    private static void Base<T>(EntityTypeBuilder<T> e, string table) where T : AuditedEntity
    {
        e.ToTable(table);
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
        e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.CreatedBy).HasColumnName("created_by");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        e.HasIndex(x => x.CompanyId).HasDatabaseName($"ix_{table}_company_id");
    }
}
