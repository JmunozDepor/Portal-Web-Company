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
    public DbSet<CommuneAlias> CommuneAliases => Set<CommuneAlias>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<ZoneCommune> ZoneCommunes => Set<ZoneCommune>();
    public DbSet<Origin> Origins => Set<Origin>();
    public DbSet<RejectionReason> RejectionReasons => Set<RejectionReason>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<NotificationRecipient> NotificationRecipients => Set<NotificationRecipient>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateSurcharge> RateSurcharges => Set<RateSurcharge>();
    public DbSet<SapDocumentQuery> SapDocumentQueries => Set<SapDocumentQuery>();
    public DbSet<DespachosSetting> Settings => Set<DespachosSetting>();
    public DbSet<Manifest> Manifests => Set<Manifest>();
    public DbSet<ManifestDocument> ManifestDocuments => Set<ManifestDocument>();
    public DbSet<ManifestCostLine> ManifestCostLines => Set<ManifestCostLine>();
    public DbSet<ManifestEvent> ManifestEvents => Set<ManifestEvent>();
    public DbSet<ManifestExtraCharge> ManifestExtraCharges => Set<ManifestExtraCharge>();
    public DbSet<MonthlyClosure> MonthlyClosures => Set<MonthlyClosure>();
    public DbSet<TransportGuide> TransportGuides => Set<TransportGuide>();
    public DbSet<TransportGuideDocument> TransportGuideDocuments => Set<TransportGuideDocument>();

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

        modelBuilder.Entity<CommuneAlias>(e =>
        {
            Base(e, "commune_aliases");
            e.Property(x => x.Text).HasColumnName("text").HasMaxLength(60).IsRequired();
            e.Property(x => x.NormalizedText).HasColumnName("normalized_text").HasMaxLength(60).IsRequired();
            e.Property(x => x.CommuneId).HasColumnName("commune_id");
            e.HasIndex(x => new { x.CompanyId, x.NormalizedText }).IsUnique().HasDatabaseName("uq_commune_aliases_company_id_normalized_text");
            e.HasIndex(x => x.CommuneId).HasDatabaseName("ix_commune_aliases_commune_id");
            e.HasOne<Commune>().WithMany().HasForeignKey(x => x.CommuneId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_commune_aliases_communes");
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

        modelBuilder.Entity<SapDocumentQuery>(e =>
        {
            Base(e, "sap_document_queries");
            e.Property(x => x.DocumentKind).HasColumnName("document_kind").HasMaxLength(30).IsRequired();
            e.Property(x => x.SqlText).HasColumnName("sql_text").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.ValidatedAt).HasColumnName("validated_at");
            e.HasIndex(x => new { x.CompanyId, x.DocumentKind }).IsUnique()
                .HasDatabaseName("uq_sap_document_queries_company_id_document_kind");
        });

        modelBuilder.Entity<DespachosSetting>(e =>
        {
            Base(e, "despachos_settings");
            e.Property(x => x.IssuerTaxId).HasColumnName("issuer_tax_id").HasMaxLength(12);
            e.Property(x => x.GuidesEnabled).HasColumnName("guides_enabled");
            e.Property(x => x.TransferIndicator).HasColumnName("transfer_indicator").HasDefaultValue(1);
            e.Property(x => x.DispatchType).HasColumnName("dispatch_type");
            e.Property(x => x.GuideLineGrouping).HasColumnName("guide_line_grouping").HasMaxLength(10)
                .IsRequired().HasDefaultValue(GuideLineGroupings.Sku);
            e.Property(x => x.GuideItemCodePlacement).HasColumnName("guide_item_code_placement").HasMaxLength(10)
                .IsRequired().HasDefaultValue(GuideItemCodePlacements.Column);
            e.Property(x => x.GuideMaxLines).HasColumnName("guide_max_lines").HasDefaultValue(60);
            e.HasIndex(x => x.CompanyId).IsUnique().HasDatabaseName("uq_despachos_settings_company_id");
        });

        modelBuilder.Entity<Manifest>(e =>
        {
            e.ToTable("manifests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.Number).HasColumnName("number");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.DispatchDate).HasColumnName("dispatch_date");
            e.Property(x => x.ShiftId).HasColumnName("shift_id");
            e.Property(x => x.OriginId).HasColumnName("origin_id");
            e.Property(x => x.CarrierId).HasColumnName("carrier_id");
            e.Property(x => x.VehicleId).HasColumnName("vehicle_id");
            e.Property(x => x.TrailerId).HasColumnName("trailer_id");
            e.Property(x => x.DriverId).HasColumnName("driver_id");
            e.Property(x => x.CarrierTaxId).HasColumnName("carrier_tax_id").HasMaxLength(12);
            e.Property(x => x.DriverTaxId).HasColumnName("driver_tax_id").HasMaxLength(12);
            e.Property(x => x.DriverName).HasColumnName("driver_name").HasMaxLength(150);
            e.Property(x => x.Plate).HasColumnName("plate").HasMaxLength(10);
            e.Property(x => x.TrailerPlate).HasColumnName("trailer_plate").HasMaxLength(10);
            e.Property(x => x.OriginAddress).HasColumnName("origin_address").HasMaxLength(250);
            e.Property(x => x.OriginCommune).HasColumnName("origin_commune").HasMaxLength(60);
            e.Property(x => x.DepartedAt).HasColumnName("departed_at");
            e.Property(x => x.PlannedDepartureTime).HasColumnName("planned_departure_time");
            e.Property(x => x.EstimatedArrivalDate).HasColumnName("estimated_arrival_date");
            e.Property(x => x.EstimatedArrivalTime).HasColumnName("estimated_arrival_time");
            e.Property(x => x.ReturnedAt).HasColumnName("returned_at");
            e.Property(x => x.OdometerStart).HasColumnName("odometer_start");
            e.Property(x => x.OdometerEnd).HasColumnName("odometer_end");
            e.Property(x => x.ServiceOrder).HasColumnName("service_order").HasMaxLength(60);
            e.Property(x => x.Remarks).HasColumnName("remarks").HasMaxLength(500);
            e.Property(x => x.CostCalculated).HasColumnName("cost_calculated").HasPrecision(18, 2);
            e.Property(x => x.RateCardId).HasColumnName("rate_card_id");
            e.Property(x => x.CostOverride).HasColumnName("cost_override").HasPrecision(18, 2);
            e.Property(x => x.CostOverrideReason).HasColumnName("cost_override_reason").HasMaxLength(500);
            e.Property(x => x.CostFinal).HasColumnName("cost_final").HasPrecision(18, 2);
            e.Property(x => x.CostFrozenAt).HasColumnName("cost_frozen_at");
            e.Property(x => x.CostStatus).HasColumnName("cost_status").HasMaxLength(20).IsRequired();
            e.Property(x => x.TotalDocuments).HasColumnName("total_documents");
            e.Property(x => x.TotalUnits).HasColumnName("total_units").HasPrecision(18, 2);
            e.Property(x => x.TotalPackages).HasColumnName("total_packages");
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_manifests_company_id");
            e.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique().HasDatabaseName("uq_manifests_company_id_number");
            e.HasIndex(x => new { x.CompanyId, x.DispatchDate }).HasDatabaseName("ix_manifests_company_id_dispatch_date");
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(d => d.ManifestId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.CostLines).WithOne().HasForeignKey(l => l.ManifestId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Events).WithOne().HasForeignKey(l => l.ManifestId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.ExtraCharges).WithOne().HasForeignKey(l => l.ManifestId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransportGuide>(e =>
        {
            e.ToTable("transport_guides");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ManifestId).HasColumnName("manifest_id");
            e.Property(x => x.Sequence).HasColumnName("sequence");
            e.Property(x => x.EmissionKey).HasColumnName("emission_key");
            e.Property(x => x.ReceiverTaxId).HasColumnName("receiver_tax_id").HasMaxLength(12).IsRequired();
            e.Property(x => x.ReceiverName).HasColumnName("receiver_name").HasMaxLength(150).IsRequired();
            e.Property(x => x.DestinationAddress).HasColumnName("destination_address").HasMaxLength(250).IsRequired();
            e.Property(x => x.DestinationCommune).HasColumnName("destination_commune").HasMaxLength(60).IsRequired();
            e.Property(x => x.DestinationCity).HasColumnName("destination_city").HasMaxLength(60);
            e.Property(x => x.TransferIndicator).HasColumnName("transfer_indicator");
            e.Property(x => x.LineCount).HasColumnName("line_count");
            e.Property(x => x.TotalQuantity).HasColumnName("total_quantity").HasPrecision(18, 4);
            e.Property(x => x.WeightKg).HasColumnName("weight_kg").HasPrecision(18, 3);
            e.Property(x => x.DocumentJson).HasColumnName("document_json").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.Folio).HasColumnName("folio");
            e.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(1000);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.IssuedAt).HasColumnName("issued_at");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_transport_guides_company_id");
            e.HasIndex(x => new { x.ManifestId, x.Sequence }).IsUnique().HasDatabaseName("uq_transport_guides_manifest_id_sequence");
            e.HasIndex(x => x.EmissionKey).IsUnique().HasDatabaseName("uq_transport_guides_emission_key");
            e.HasOne<Manifest>().WithMany().HasForeignKey(x => x.ManifestId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_transport_guides_manifests");
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(d => d.TransportGuideId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_transport_guide_documents_transport_guides");
        });

        modelBuilder.Entity<TransportGuideDocument>(e =>
        {
            e.ToTable("transport_guide_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.TransportGuideId).HasColumnName("transport_guide_id");
            e.Property(x => x.ManifestDocumentId).HasColumnName("manifest_document_id");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_transport_guide_documents_company_id");
            e.HasIndex(x => x.ManifestDocumentId).HasDatabaseName("ix_transport_guide_documents_manifest_document_id");
            e.HasIndex(x => new { x.TransportGuideId, x.ManifestDocumentId }).IsUnique()
                .HasDatabaseName("uq_transport_guide_documents_guide_id_document_id");
            e.HasOne<ManifestDocument>().WithMany().HasForeignKey(x => x.ManifestDocumentId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_transport_guide_documents_manifest_documents");
        });

        modelBuilder.Entity<ManifestExtraCharge>(e =>
        {
            e.ToTable("manifest_extra_charges");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ManifestId).HasColumnName("manifest_id");
            e.Property(x => x.RateSurchargeId).HasColumnName("rate_surcharge_id");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_manifest_extra_charges_company_id");
            e.HasIndex(x => new { x.ManifestId, x.RateSurchargeId }).IsUnique()
                .HasDatabaseName("uq_manifest_extra_charges_manifest_id_rate_surcharge_id");
            e.HasOne<RateSurcharge>().WithMany().HasForeignKey(x => x.RateSurchargeId)
                .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_manifest_extra_charges_rate_surcharges");
        });

        modelBuilder.Entity<ManifestDocument>(e =>
        {
            e.ToTable("manifest_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ManifestId).HasColumnName("manifest_id");
            e.Property(x => x.DocumentKind).HasColumnName("document_kind").HasMaxLength(30).IsRequired();
            e.Property(x => x.SiiDocumentType).HasColumnName("sii_document_type");
            e.Property(x => x.Folio).HasColumnName("folio");
            e.Property(x => x.SapDocEntry).HasColumnName("sap_doc_entry");
            e.Property(x => x.SapDocNum).HasColumnName("sap_doc_num");
            e.Property(x => x.DocumentDate).HasColumnName("document_date");
            e.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(18, 2);
            e.Property(x => x.CustomerCode).HasColumnName("customer_code").HasMaxLength(50).IsRequired();
            e.Property(x => x.CustomerName).HasColumnName("customer_name").HasMaxLength(200).IsRequired();
            e.Property(x => x.CustomerTaxId).HasColumnName("customer_tax_id").HasMaxLength(12);
            e.Property(x => x.DestinationAddress).HasColumnName("destination_address").HasMaxLength(250);
            e.Property(x => x.DestinationCommuneName).HasColumnName("destination_commune_name").HasMaxLength(60);
            e.Property(x => x.DestinationCommuneId).HasColumnName("destination_commune_id");
            e.Property(x => x.SapBaseOrderEntry).HasColumnName("sap_base_order_entry");
            e.Property(x => x.CustomerPo).HasColumnName("customer_po").HasMaxLength(60);
            e.Property(x => x.Units).HasColumnName("units").HasPrecision(18, 2);
            e.Property(x => x.Packages).HasColumnName("packages");
            e.Property(x => x.WeightKg).HasColumnName("weight_kg").HasPrecision(18, 2);
            e.Property(x => x.VolumeM3).HasColumnName("volume_m3").HasPrecision(9, 2);
            e.Property(x => x.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(25).IsRequired();
            e.Property(x => x.RejectionReasonId).HasColumnName("rejection_reason_id");
            e.Property(x => x.Comments).HasColumnName("comments").HasMaxLength(500);
            e.Property(x => x.ReturnedBy).HasColumnName("returned_by");
            e.Property(x => x.ReturnedAt).HasColumnName("returned_at");
            e.Property(x => x.CaptureMethod).HasColumnName("capture_method").HasMaxLength(10).IsRequired();
            e.Property(x => x.AddedAt).HasColumnName("added_at");
            e.Property(x => x.AddedBy).HasColumnName("added_by");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_manifest_documents_company_id");
            e.HasIndex(x => x.ManifestId).HasDatabaseName("ix_manifest_documents_manifest_id");
            e.HasIndex(x => new { x.CompanyId, x.DocumentKind, x.SiiDocumentType, x.Folio })
                .HasDatabaseName("ix_manifest_documents_company_id_kind_sii_folio");
        });

        modelBuilder.Entity<ManifestCostLine>(e =>
        {
            e.ToTable("manifest_cost_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ManifestId).HasColumnName("manifest_id");
            e.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(20).IsRequired();
            e.Property(x => x.CommuneId).HasColumnName("commune_id");
            e.Property(x => x.RateCardId).HasColumnName("rate_card_id");
            e.Property(x => x.RateSurchargeId).HasColumnName("rate_surcharge_id");
            e.Property(x => x.Amount).HasColumnName("amount").HasPrecision(18, 2);
            e.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(500).IsRequired();
            e.Property(x => x.Documents).HasColumnName("documents");
            e.Property(x => x.Units).HasColumnName("units").HasPrecision(18, 2);
            e.Property(x => x.Packages).HasColumnName("packages");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_manifest_cost_lines_company_id");
            e.HasIndex(x => x.ManifestId).HasDatabaseName("ix_manifest_cost_lines_manifest_id");
        });

        modelBuilder.Entity<ManifestEvent>(e =>
        {
            e.ToTable("manifest_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ManifestId).HasColumnName("manifest_id");
            e.Property(x => x.ManifestDocumentId).HasColumnName("manifest_document_id");
            e.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(30).IsRequired();
            e.Property(x => x.FromStatus).HasColumnName("from_status").HasMaxLength(25);
            e.Property(x => x.ToStatus).HasColumnName("to_status").HasMaxLength(25);
            e.Property(x => x.PayloadJson).HasColumnName("payload_json");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_manifest_events_company_id");
            e.HasIndex(x => x.ManifestId).HasDatabaseName("ix_manifest_events_manifest_id");
        });

        modelBuilder.Entity<MonthlyClosure>(e =>
        {
            Base(e, "monthly_closures");
            e.Property(x => x.Period).HasColumnName("period").HasMaxLength(7).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(15).IsRequired();
            e.Property(x => x.ClosedAt).HasColumnName("closed_at");
            e.Property(x => x.ClosedBy).HasColumnName("closed_by");
            e.Property(x => x.ReopenedAt).HasColumnName("reopened_at");
            e.Property(x => x.ReopenedBy).HasColumnName("reopened_by");
            e.Property(x => x.ReopenReason).HasColumnName("reopen_reason").HasMaxLength(500);
            e.Property(x => x.ManifestCount).HasColumnName("manifest_count");
            e.Property(x => x.TotalCost).HasColumnName("total_cost").HasPrecision(18, 2);
            e.HasIndex(x => new { x.CompanyId, x.Period }).IsUnique().HasDatabaseName("uq_monthly_closures_company_id_period");
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
