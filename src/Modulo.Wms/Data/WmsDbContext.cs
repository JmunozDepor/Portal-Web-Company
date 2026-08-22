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
    public DbSet<WmsValidationField> ValidationFields => Set<WmsValidationField>();
    public DbSet<WmsServiceConfig> ServiceConfigs => Set<WmsServiceConfig>();
    public DbSet<WmsServiceHeartbeat> ServiceHeartbeats => Set<WmsServiceHeartbeat>();
    public DbSet<WmsOracleInboundStage> WmsOracleInboundStages => Set<WmsOracleInboundStage>();
    public DbSet<WmsOracleStageSlsh> WmsOracleStageSlsh => Set<WmsOracleStageSlsh>();
    public DbSet<WmsOracleStageSvsh> WmsOracleStageSvsh => Set<WmsOracleStageSvsh>();
    public DbSet<WmsSapStageItem> WmsSapStageItems => Set<WmsSapStageItem>();
    public DbSet<WmsSapStageStore> WmsSapStageStores => Set<WmsSapStageStore>();
    public DbSet<WmsSapStageInboundHdr> WmsSapStageInboundHdrs => Set<WmsSapStageInboundHdr>();
    public DbSet<WmsSapStageInboundDtl> WmsSapStageInboundDtls => Set<WmsSapStageInboundDtl>();
    public DbSet<WmsSapStageOrderHdr> WmsSapStageOrderHdrs => Set<WmsSapStageOrderHdr>();
    public DbSet<WmsSapStageOrderDtl> WmsSapStageOrderDtls => Set<WmsSapStageOrderDtl>();
    public DbSet<WmsExportValidation> WmsExportValidations => Set<WmsExportValidation>();

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

        modelBuilder.Entity<WmsValidationField>(e =>
        {
            e.ToTable("wms_validation_fields");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.TipoEntidad).HasColumnName("tipo_entidad").HasMaxLength(50).IsRequired();
            e.Property(x => x.FieldName).HasColumnName("field_name").HasMaxLength(100).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => new { x.CompanyId, x.TipoEntidad, x.FieldName })
                .IsUnique()
                .HasDatabaseName("uk_wms_validation_fields_key");
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

        modelBuilder.Entity<WmsOracleInboundStage>(entity =>
        {
            entity.ToTable("wms_oracle_inbound_stage");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.TipoDoc).HasColumnName("tipo_doc").HasMaxLength(10);
            entity.Property(e => e.Formato).HasColumnName("formato").HasConversion<string>().HasMaxLength(10);
            entity.Property(e => e.NombreArchivo).HasColumnName("nombre_archivo").HasMaxLength(255);
            entity.Property(e => e.HashArchivo).HasColumnName("hash_archivo").HasMaxLength(64);
            entity.Property(e => e.Contenido).HasColumnName("contenido");
            entity.Property(e => e.Estado).HasColumnName("estado").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Intentos).HasColumnName("intentos");
            entity.Property(e => e.MensajeError).HasColumnName("mensaje_error");
            entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry").HasMaxLength(50);
            entity.Property(e => e.InsertedAt).HasColumnName("inserted_at");
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entity.HasIndex(e => new { e.CompanyId, e.HashArchivo }).IsUnique().HasDatabaseName("ix_wms_oracle_inbound_stage_company_hash");
            entity.HasIndex(e => e.Estado).HasDatabaseName("ix_wms_oracle_inbound_stage_estado");
        });

        modelBuilder.Entity<WmsOracleStageSlsh>(entity =>
        {
            entity.ToTable("wms_oracle_stage_slsh");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry");
            entity.Property(e => e.SapObject).HasColumnName("sap_object");
            entity.Property(e => e.DocumentVersion).HasColumnName("document_version").HasMaxLength(50);
            entity.Property(e => e.OriginSystem).HasColumnName("origin_system").HasMaxLength(50);
            entity.Property(e => e.ClientEnvCode).HasColumnName("client_env_code").HasMaxLength(50);
            entity.Property(e => e.ParentCompanyCode).HasColumnName("parent_company_code").HasMaxLength(50);
            entity.Property(e => e.Entity).HasColumnName("entity").HasMaxLength(50);
            entity.Property(e => e.TimeStamp).HasColumnName("time_stamp").HasMaxLength(50);
            entity.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(50);
            entity.Property(e => e.facility_code).HasColumnName("facility_code").HasMaxLength(50);
            entity.Property(e => e.company_code).HasColumnName("company_code").HasMaxLength(50);
            entity.Property(e => e.action_code).HasColumnName("action_code").HasMaxLength(50);
            entity.Property(e => e.load_type).HasColumnName("load_type").HasMaxLength(50);
            entity.Property(e => e.load_manifest_nbr).HasColumnName("load_manifest_nbr").HasMaxLength(50);
            entity.Property(e => e.trailer_nbr).HasColumnName("trailer_nbr").HasMaxLength(50);
            entity.Property(e => e.trailer_type).HasColumnName("trailer_type").HasMaxLength(50);
            entity.Property(e => e.driver).HasColumnName("driver").HasMaxLength(50);
            entity.Property(e => e.seal_nbr).HasColumnName("seal_nbr").HasMaxLength(50);
            entity.Property(e => e.pro_nbr).HasColumnName("pro_nbr").HasMaxLength(50);
            entity.Property(e => e.route_nbr).HasColumnName("route_nbr").HasMaxLength(50);
            entity.Property(e => e.freight_class).HasColumnName("freight_class").HasMaxLength(50);
            entity.Property(e => e.hdr_bol_nbr).HasColumnName("hdr_bol_nbr").HasMaxLength(50);
            entity.Property(e => e.total_nbr_of_oblpns).HasColumnName("total_nbr_of_oblpns").HasMaxLength(50);
            entity.Property(e => e.total_weight).HasColumnName("total_weight").HasMaxLength(50);
            entity.Property(e => e.total_volume).HasColumnName("total_volume").HasMaxLength(50);
            entity.Property(e => e.total_shipping_charge).HasColumnName("total_shipping_charge").HasMaxLength(50);
            entity.Property(e => e.ship_date).HasColumnName("ship_date").HasMaxLength(50);
            entity.Property(e => e.sched_delivery_date).HasColumnName("sched_delivery_date").HasMaxLength(50);
            entity.Property(e => e.carrier_code).HasColumnName("carrier_code").HasMaxLength(50);
            entity.Property(e => e.externally_planned_load_nbr).HasColumnName("externally_planned_load_nbr").HasMaxLength(50);
            entity.Property(e => e.ship_date_time).HasColumnName("ship_date_time").HasMaxLength(50);
            entity.Property(e => e.sched_delivery_date_time).HasColumnName("sched_delivery_date_time").HasMaxLength(50);
            entity.Property(e => e.line_nbr).HasColumnName("line_nbr").HasMaxLength(50);
            entity.Property(e => e.seq_nbr).HasColumnName("seq_nbr").HasMaxLength(50);
            entity.Property(e => e.stop_shipment_nbr).HasColumnName("stop_shipment_nbr").HasMaxLength(50);
            entity.Property(e => e.stop_bol_nbr).HasColumnName("stop_bol_nbr").HasMaxLength(50);
            entity.Property(e => e.stop_nbr_of_oblpns).HasColumnName("stop_nbr_of_oblpns").HasMaxLength(50);
            entity.Property(e => e.stop_weight).HasColumnName("stop_weight").HasMaxLength(50);
            entity.Property(e => e.stop_volume).HasColumnName("stop_volume").HasMaxLength(50);
            entity.Property(e => e.stop_shipping_charge).HasColumnName("stop_shipping_charge").HasMaxLength(50);
            entity.Property(e => e.shipto_facility_code).HasColumnName("shipto_facility_code").HasMaxLength(100);
            entity.Property(e => e.shipto_name).HasColumnName("shipto_name").HasMaxLength(100);
            entity.Property(e => e.shipto_addr).HasColumnName("shipto_addr").HasMaxLength(100);
            entity.Property(e => e.shipto_addr2).HasColumnName("shipto_addr2").HasMaxLength(100);
            entity.Property(e => e.shipto_addr3).HasColumnName("shipto_addr3").HasMaxLength(100);
            entity.Property(e => e.shipto_city).HasColumnName("shipto_city").HasMaxLength(100);
            entity.Property(e => e.shipto_state).HasColumnName("shipto_state").HasMaxLength(100);
            entity.Property(e => e.shipto_zip).HasColumnName("shipto_zip").HasMaxLength(100);
            entity.Property(e => e.shipto_country).HasColumnName("shipto_country").HasMaxLength(100);
            entity.Property(e => e.shipto_phone_nbr).HasColumnName("shipto_phone_nbr").HasMaxLength(100);
            entity.Property(e => e.shipto_email).HasColumnName("shipto_email").HasMaxLength(100);
            entity.Property(e => e.shipto_contact).HasColumnName("shipto_contact").HasMaxLength(100);
            entity.Property(e => e.dest_facility_code).HasColumnName("dest_facility_code").HasMaxLength(100);
            entity.Property(e => e.cust_name).HasColumnName("cust_name").HasMaxLength(100);
            entity.Property(e => e.cust_addr).HasColumnName("cust_addr").HasMaxLength(100);
            entity.Property(e => e.cust_addr2).HasColumnName("cust_addr2").HasMaxLength(100);
            entity.Property(e => e.cust_addr3).HasColumnName("cust_addr3").HasMaxLength(100);
            entity.Property(e => e.cust_city).HasColumnName("cust_city").HasMaxLength(100);
            entity.Property(e => e.cust_state).HasColumnName("cust_state").HasMaxLength(100);
            entity.Property(e => e.cust_zip).HasColumnName("cust_zip").HasMaxLength(100);
            entity.Property(e => e.cust_country).HasColumnName("cust_country").HasMaxLength(100);
            entity.Property(e => e.cust_phone_nbr).HasColumnName("cust_phone_nbr").HasMaxLength(100);
            entity.Property(e => e.cust_email).HasColumnName("cust_email").HasMaxLength(100);
            entity.Property(e => e.cust_contact).HasColumnName("cust_contact").HasMaxLength(100);
            entity.Property(e => e.cust_nbr).HasColumnName("cust_nbr").HasMaxLength(100);
            entity.Property(e => e.order_nbr).HasColumnName("order_nbr").HasMaxLength(50);
            entity.Property(e => e.ord_date).HasColumnName("ord_date").HasMaxLength(50);
            entity.Property(e => e.exp_date).HasColumnName("exp_date").HasMaxLength(50);
            entity.Property(e => e.req_ship_date).HasColumnName("req_ship_date").HasMaxLength(50);
            entity.Property(e => e.start_ship_date).HasColumnName("start_ship_date").HasMaxLength(50);
            entity.Property(e => e.stop_ship_date).HasColumnName("stop_ship_date").HasMaxLength(50);
            entity.Property(e => e.host_allocation_nbr).HasColumnName("host_allocation_nbr").HasMaxLength(50);
            entity.Property(e => e.customer_po_nbr).HasColumnName("customer_po_nbr").HasMaxLength(50);
            entity.Property(e => e.sales_order_nbr).HasColumnName("sales_order_nbr").HasMaxLength(50);
            entity.Property(e => e.sales_channel).HasColumnName("sales_channel").HasMaxLength(50);
            entity.Property(e => e.dest_dept_nbr).HasColumnName("dest_dept_nbr").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_field_1).HasColumnName("order_hdr_cust_field_1").HasMaxLength(200);
            entity.Property(e => e.order_hdr_cust_field_2).HasColumnName("order_hdr_cust_field_2").HasMaxLength(200);
            entity.Property(e => e.order_hdr_cust_field_3).HasColumnName("order_hdr_cust_field_3").HasMaxLength(200);
            entity.Property(e => e.order_hdr_cust_field_4).HasColumnName("order_hdr_cust_field_4").HasMaxLength(200);
            entity.Property(e => e.order_hdr_cust_field_5).HasColumnName("order_hdr_cust_field_5").HasMaxLength(200);
            entity.Property(e => e.order_seq_nbr).HasColumnName("order_seq_nbr").HasMaxLength(200);
            entity.Property(e => e.order_dtl_cust_field_1).HasColumnName("order_dtl_cust_field_1").HasMaxLength(200);
            entity.Property(e => e.order_dtl_cust_field_2).HasColumnName("order_dtl_cust_field_2").HasMaxLength(200);
            entity.Property(e => e.order_dtl_cust_field_3).HasColumnName("order_dtl_cust_field_3").HasMaxLength(200);
            entity.Property(e => e.order_dtl_cust_field_4).HasColumnName("order_dtl_cust_field_4").HasMaxLength(200);
            entity.Property(e => e.order_dtl_cust_field_5).HasColumnName("order_dtl_cust_field_5").HasMaxLength(200);
            entity.Property(e => e.ob_lpn_nbr).HasColumnName("ob_lpn_nbr").HasMaxLength(50);
            entity.Property(e => e.item_alternate_code).HasColumnName("item_alternate_code").HasMaxLength(50);
            entity.Property(e => e.item_part_a).HasColumnName("item_part_a").HasMaxLength(50);
            entity.Property(e => e.item_part_b).HasColumnName("item_part_b").HasMaxLength(50);
            entity.Property(e => e.item_part_c).HasColumnName("item_part_c").HasMaxLength(50);
            entity.Property(e => e.item_part_d).HasColumnName("item_part_d").HasMaxLength(50);
            entity.Property(e => e.item_part_e).HasColumnName("item_part_e").HasMaxLength(50);
            entity.Property(e => e.item_part_f).HasColumnName("item_part_f").HasMaxLength(50);
            entity.Property(e => e.pre_pack_code).HasColumnName("pre_pack_code").HasMaxLength(50);
            entity.Property(e => e.pre_pack_ratio).HasColumnName("pre_pack_ratio").HasMaxLength(50);
            entity.Property(e => e.pre_pack_ratio_seq).HasColumnName("pre_pack_ratio_seq").HasMaxLength(50);
            entity.Property(e => e.pre_pack_total_units).HasColumnName("pre_pack_total_units").HasMaxLength(50);
            entity.Property(e => e.invn_attr_a).HasColumnName("invn_attr_a").HasMaxLength(50);
            entity.Property(e => e.invn_attr_b).HasColumnName("invn_attr_b").HasMaxLength(50);
            entity.Property(e => e.invn_attr_c).HasColumnName("invn_attr_c").HasMaxLength(50);
            entity.Property(e => e.hazmat).HasColumnName("hazmat").HasMaxLength(50);
            entity.Property(e => e.shipped_uom).HasColumnName("shipped_uom").HasMaxLength(50);
            entity.Property(e => e.shipped_qty).HasColumnName("shipped_qty").HasMaxLength(50);
            entity.Property(e => e.pallet_nbr).HasColumnName("pallet_nbr").HasMaxLength(50);
            entity.Property(e => e.dest_company_code).HasColumnName("dest_company_code").HasMaxLength(50);
            entity.Property(e => e.batch_nbr).HasColumnName("batch_nbr").HasMaxLength(50);
            entity.Property(e => e.expiry_date).HasColumnName("expiry_date").HasMaxLength(50);
            entity.Property(e => e.tracking_nbr).HasColumnName("tracking_nbr").HasMaxLength(50);
            entity.Property(e => e.master_tracking_nbr).HasColumnName("master_tracking_nbr").HasMaxLength(50);
            entity.Property(e => e.package_type).HasColumnName("package_type").HasMaxLength(50);
            entity.Property(e => e.payment_method).HasColumnName("payment_method").HasMaxLength(50);
            entity.Property(e => e.carrier_account_nbr).HasColumnName("carrier_account_nbr").HasMaxLength(50);
            entity.Property(e => e.ship_via_code).HasColumnName("ship_via_code").HasMaxLength(50);
            entity.Property(e => e.ob_lpn_weight).HasColumnName("ob_lpn_weight").HasMaxLength(50);
            entity.Property(e => e.ob_lpn_volume).HasColumnName("ob_lpn_volume").HasMaxLength(50);
            entity.Property(e => e.ob_lpn_shipping_charge).HasColumnName("ob_lpn_shipping_charge").HasMaxLength(50);
            entity.Property(e => e.ob_lpn_type).HasColumnName("ob_lpn_type").HasMaxLength(50);
            entity.Property(e => e.ob_lpn_asset_nbr).HasColumnName("ob_lpn_asset_nbr").HasMaxLength(50);
            entity.Property(e => e.ob_lpn_asset_seal_nbr).HasColumnName("ob_lpn_asset_seal_nbr").HasMaxLength(50);
            entity.Property(e => e.serial_nbr).HasColumnName("serial_nbr").HasMaxLength(50);
            entity.Property(e => e.customer_po_type).HasColumnName("customer_po_type").HasMaxLength(50);
            entity.Property(e => e.customer_vendor_code).HasColumnName("customer_vendor_code").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_date_1).HasColumnName("order_hdr_cust_date_1").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_date_2).HasColumnName("order_hdr_cust_date_2").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_date_3).HasColumnName("order_hdr_cust_date_3").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_date_4).HasColumnName("order_hdr_cust_date_4").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_date_5).HasColumnName("order_hdr_cust_date_5").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_number_1).HasColumnName("order_hdr_cust_number_1").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_number_2).HasColumnName("order_hdr_cust_number_2").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_number_3).HasColumnName("order_hdr_cust_number_3").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_number_4).HasColumnName("order_hdr_cust_number_4").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_number_5).HasColumnName("order_hdr_cust_number_5").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_decimal_1).HasColumnName("order_hdr_cust_decimal_1").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_decimal_2).HasColumnName("order_hdr_cust_decimal_2").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_decimal_3).HasColumnName("order_hdr_cust_decimal_3").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_decimal_4).HasColumnName("order_hdr_cust_decimal_4").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_decimal_5).HasColumnName("order_hdr_cust_decimal_5").HasMaxLength(50);
            entity.Property(e => e.order_hdr_cust_short_text_1).HasColumnName("order_hdr_cust_short_text_1").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_2).HasColumnName("order_hdr_cust_short_text_2").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_3).HasColumnName("order_hdr_cust_short_text_3").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_4).HasColumnName("order_hdr_cust_short_text_4").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_5).HasColumnName("order_hdr_cust_short_text_5").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_6).HasColumnName("order_hdr_cust_short_text_6").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_7).HasColumnName("order_hdr_cust_short_text_7").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_8).HasColumnName("order_hdr_cust_short_text_8").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_9).HasColumnName("order_hdr_cust_short_text_9").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_10).HasColumnName("order_hdr_cust_short_text_10").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_11).HasColumnName("order_hdr_cust_short_text_11").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_short_text_12).HasColumnName("order_hdr_cust_short_text_12").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_long_text_1).HasColumnName("order_hdr_cust_long_text_1").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_long_text_2).HasColumnName("order_hdr_cust_long_text_2").HasMaxLength(100);
            entity.Property(e => e.order_hdr_cust_long_text_3).HasColumnName("order_hdr_cust_long_text_3").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_date_1).HasColumnName("order_dtl_cust_date_1").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_date_2).HasColumnName("order_dtl_cust_date_2").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_date_3).HasColumnName("order_dtl_cust_date_3").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_date_4).HasColumnName("order_dtl_cust_date_4").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_date_5).HasColumnName("order_dtl_cust_date_5").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_number_1).HasColumnName("order_dtl_cust_number_1").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_number_2).HasColumnName("order_dtl_cust_number_2").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_number_3).HasColumnName("order_dtl_cust_number_3").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_number_4).HasColumnName("order_dtl_cust_number_4").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_number_5).HasColumnName("order_dtl_cust_number_5").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_decimal_1).HasColumnName("order_dtl_cust_decimal_1").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_decimal_2).HasColumnName("order_dtl_cust_decimal_2").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_decimal_3).HasColumnName("order_dtl_cust_decimal_3").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_decimal_4).HasColumnName("order_dtl_cust_decimal_4").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_decimal_5).HasColumnName("order_dtl_cust_decimal_5").HasMaxLength(50);
            entity.Property(e => e.order_dtl_cust_short_text_1).HasColumnName("order_dtl_cust_short_text_1").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_2).HasColumnName("order_dtl_cust_short_text_2").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_3).HasColumnName("order_dtl_cust_short_text_3").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_4).HasColumnName("order_dtl_cust_short_text_4").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_5).HasColumnName("order_dtl_cust_short_text_5").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_6).HasColumnName("order_dtl_cust_short_text_6").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_7).HasColumnName("order_dtl_cust_short_text_7").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_8).HasColumnName("order_dtl_cust_short_text_8").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_9).HasColumnName("order_dtl_cust_short_text_9").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_10).HasColumnName("order_dtl_cust_short_text_10").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_11).HasColumnName("order_dtl_cust_short_text_11").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_short_text_12).HasColumnName("order_dtl_cust_short_text_12").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_long_text_1).HasColumnName("order_dtl_cust_long_text_1").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_long_text_2).HasColumnName("order_dtl_cust_long_text_2").HasMaxLength(100);
            entity.Property(e => e.order_dtl_cust_long_text_3).HasColumnName("order_dtl_cust_long_text_3").HasMaxLength(100);
            entity.Property(e => e.invn_attr_d).HasColumnName("invn_attr_d").HasMaxLength(100);
            entity.Property(e => e.invn_attr_e).HasColumnName("invn_attr_e").HasMaxLength(100);
            entity.Property(e => e.invn_attr_f).HasColumnName("invn_attr_f").HasMaxLength(100);
            entity.Property(e => e.invn_attr_g).HasColumnName("invn_attr_g").HasMaxLength(100);
            entity.Property(e => e.order_type).HasColumnName("order_type").HasMaxLength(50);
            entity.Property(e => e.rcvd_trailer_nbr).HasColumnName("rcvd_trailer_nbr").HasMaxLength(50);
            entity.Property(e => e.stop_seal_nbr).HasColumnName("stop_seal_nbr").HasMaxLength(50);
            entity.Property(e => e.ship_request_line).HasColumnName("ship_request_line").HasMaxLength(50);
            entity.HasOne<WmsOracleInboundStage>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ParentId).HasDatabaseName("ix_wms_oracle_stage_slsh_parent");
            entity.HasIndex(e => new { e.Status, e.RetryCount }).HasDatabaseName("ix_wms_oracle_stage_slsh_status_retry");
            entity.HasIndex(e => e.ob_lpn_nbr).HasDatabaseName("ix_wms_oracle_stage_slsh_lpn");
        });

        modelBuilder.Entity<WmsOracleStageSvsh>(entity =>
        {
            entity.ToTable("wms_oracle_stage_svsh");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry");
            entity.Property(e => e.SapObject).HasColumnName("sap_object");
            entity.Property(e => e.DocumentVersion).HasColumnName("document_version").HasMaxLength(50);
            entity.Property(e => e.OriginSystem).HasColumnName("origin_system").HasMaxLength(50);
            entity.Property(e => e.ClientEnvCode).HasColumnName("client_env_code").HasMaxLength(50);
            entity.Property(e => e.ParentCompanyCode).HasColumnName("parent_company_code").HasMaxLength(50);
            entity.Property(e => e.Entity).HasColumnName("entity").HasMaxLength(50);
            entity.Property(e => e.TimeStamp).HasColumnName("time_stamp").HasMaxLength(50);
            entity.Property(e => e.MessageId).HasColumnName("message_id").HasMaxLength(50);
            entity.Property(e => e.shipment_nbr).HasColumnName("shipment_nbr").HasMaxLength(50);
            entity.Property(e => e.manifest_nbr).HasColumnName("manifest_nbr").HasMaxLength(50);
            entity.Property(e => e.load_nbr).HasColumnName("load_nbr").HasMaxLength(50);
            entity.Property(e => e.facility_code).HasColumnName("facility_code").HasMaxLength(50);
            entity.Property(e => e.company_code).HasColumnName("company_code").HasMaxLength(50);
            entity.Property(e => e.asn_nbr).HasColumnName("asn_nbr").HasMaxLength(50);
            entity.Property(e => e.carrier_code).HasColumnName("carrier_code").HasMaxLength(50);
            entity.Property(e => e.trailer_nbr).HasColumnName("trailer_nbr").HasMaxLength(50);
            entity.Property(e => e.seal_nbr).HasColumnName("seal_nbr").HasMaxLength(50);
            entity.Property(e => e.rcvd_date).HasColumnName("rcvd_date").HasMaxLength(50);
            entity.Property(e => e.rcvd_date_time).HasColumnName("rcvd_date_time").HasMaxLength(50);
            entity.Property(e => e.cust_nbr).HasColumnName("cust_nbr").HasMaxLength(100);
            entity.Property(e => e.vendor_nbr).HasColumnName("vendor_nbr").HasMaxLength(100);
            entity.Property(e => e.order_nbr).HasColumnName("order_nbr").HasMaxLength(50);
            entity.Property(e => e.customer_po_nbr).HasColumnName("customer_po_nbr").HasMaxLength(50);
            entity.Property(e => e.shipment_dtl_cust_field_1).HasColumnName("shipment_dtl_cust_field_1").HasMaxLength(200);
            entity.Property(e => e.shipment_dtl_cust_field_2).HasColumnName("shipment_dtl_cust_field_2").HasMaxLength(200);
            entity.Property(e => e.shipment_dtl_cust_field_3).HasColumnName("shipment_dtl_cust_field_3").HasMaxLength(200);
            entity.Property(e => e.item_part_a).HasColumnName("item_part_a").HasMaxLength(50);
            entity.Property(e => e.item_part_b).HasColumnName("item_part_b").HasMaxLength(50);
            entity.Property(e => e.item_alternate_code).HasColumnName("item_alternate_code").HasMaxLength(50);
            entity.Property(e => e.received_qty).HasColumnName("received_qty").HasMaxLength(50);
            entity.Property(e => e.shipped_qty).HasColumnName("shipped_qty").HasMaxLength(50);
            entity.Property(e => e.shipped_uom).HasColumnName("shipped_uom").HasMaxLength(50);
            entity.Property(e => e.ib_lpn_nbr).HasColumnName("ib_lpn_nbr").HasMaxLength(50);
            entity.Property(e => e.batch_nbr).HasColumnName("batch_nbr").HasMaxLength(50);
            entity.Property(e => e.expiry_date).HasColumnName("expiry_date").HasMaxLength(50);
            entity.Property(e => e.serial_nbr).HasColumnName("serial_nbr").HasMaxLength(50);
            entity.Property(e => e.line_nbr).HasColumnName("line_nbr").HasMaxLength(50);
            entity.Property(e => e.seq_nbr).HasColumnName("seq_nbr").HasMaxLength(50);
            entity.HasOne<WmsOracleInboundStage>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ParentId).HasDatabaseName("ix_wms_oracle_stage_svsh_parent");
            entity.HasIndex(e => new { e.Status, e.RetryCount }).HasDatabaseName("ix_wms_oracle_stage_svsh_status_retry");
            entity.HasIndex(e => e.shipment_nbr).HasDatabaseName("ix_wms_oracle_stage_svsh_shipment");
        });

        modelBuilder.Entity<WmsSapStageItem>(entity =>
        {
            entity.ToTable("wms_sap_stage_item");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(50);
            entity.Property(e => e.ItemName).HasColumnName("item_name").HasMaxLength(200);
            entity.Property(e => e.BarCode).HasColumnName("bar_code").HasMaxLength(50);
            entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
            entity.Property(e => e.ExtraFieldsJson).HasColumnName("extra_fields");
            entity.HasIndex(e => new { e.CompanyId, e.ItemCode }).IsUnique().HasDatabaseName("ix_wms_sap_stage_item_company_itemcode");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_item_status");
        });

        modelBuilder.Entity<WmsSapStageStore>(entity =>
        {
            entity.ToTable("wms_sap_stage_store");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.CardCode).HasColumnName("card_code").HasMaxLength(50);
            entity.Property(e => e.CardName).HasColumnName("card_name").HasMaxLength(200);
            entity.Property(e => e.Street).HasColumnName("street").HasMaxLength(200);
            entity.Property(e => e.City).HasColumnName("city").HasMaxLength(100);
            entity.Property(e => e.ZipCode).HasColumnName("zip_code").HasMaxLength(20);
            entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
            entity.HasIndex(e => new { e.CompanyId, e.CardCode }).IsUnique().HasDatabaseName("ix_wms_sap_stage_store_company_cardcode");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_store_status");
        });

        modelBuilder.Entity<WmsSapStageInboundHdr>(entity =>
        {
            entity.ToTable("wms_sap_stage_inbound_hdr");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.SapDocEntry).HasColumnName("sap_doc_entry");
            entity.Property(e => e.ShipmentType).HasColumnName("shipment_type").HasMaxLength(50);
            entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
            entity.HasIndex(e => new { e.CompanyId, e.SapDocEntry }).IsUnique().HasDatabaseName("ix_wms_sap_stage_inbound_hdr_company_docentry");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_inbound_hdr_status");
        });

        modelBuilder.Entity<WmsSapStageInboundDtl>(entity =>
        {
            entity.ToTable("wms_sap_stage_inbound_dtl");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(50);
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            entity.Property(e => e.WhsCode).HasColumnName("whs_code").HasMaxLength(20);
            entity.Property(e => e.LineNum).HasColumnName("line_num");
            entity.HasOne<WmsSapStageInboundHdr>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WmsSapStageOrderHdr>(entity =>
        {
            entity.ToTable("wms_sap_stage_order_hdr");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.OrderNbr).HasColumnName("order_nbr").HasMaxLength(50);
            entity.Property(e => e.OrderType).HasColumnName("order_type").HasMaxLength(20);
            entity.Property(e => e.PickListAbsEntry).HasColumnName("pick_list_abs_entry");
            entity.Property(e => e.BaseObjectType).HasColumnName("base_object_type");
            entity.Property(e => e.BaseEntry).HasColumnName("base_entry");
            entity.Property(e => e.CardCode).HasColumnName("card_code").HasMaxLength(50);
            entity.Property(e => e.CardName).HasColumnName("card_name").HasMaxLength(200);
            entity.Property(e => e.CustomerPoNbr).HasColumnName("customer_po_nbr").HasMaxLength(50);
            entity.Property(e => e.OrdDate).HasColumnName("ord_date");
            entity.Property(e => e.ExpDate).HasColumnName("exp_date");
            entity.Property(e => e.ReqShipDate).HasColumnName("req_ship_date");
            entity.Property(e => e.ShipToCode).HasColumnName("ship_to_code").HasMaxLength(20);
            entity.Property(e => e.SourceUpdateDate).HasColumnName("source_update_date");
            entity.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count");
            entity.Property(e => e.ErrorMsg).HasColumnName("error_msg").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.SyncedAt).HasColumnName("synced_at");
            entity.HasIndex(e => new { e.CompanyId, e.OrderNbr }).IsUnique().HasDatabaseName("ix_wms_sap_stage_order_hdr_company_ordernbr");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_wms_sap_stage_order_hdr_status");
        });

        modelBuilder.Entity<WmsSapStageOrderDtl>(entity =>
        {
            entity.ToTable("wms_sap_stage_order_dtl");
            entity.HasKey(e => e.LineId);
            entity.Property(e => e.LineId).HasColumnName("line_id");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");
            entity.Property(e => e.ItemCode).HasColumnName("item_code").HasMaxLength(50);
            entity.Property(e => e.Quantity).HasColumnName("quantity").HasPrecision(18, 4);
            entity.Property(e => e.WhsCode).HasColumnName("whs_code").HasMaxLength(20);
            entity.Property(e => e.LineNum).HasColumnName("line_num");
            entity.Property(e => e.SeqNbr).HasColumnName("seq_nbr");
            entity.HasOne<WmsSapStageOrderHdr>().WithMany().HasForeignKey(e => e.ParentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WmsExportValidation>(entity =>
        {
            entity.ToTable("wms_oracle_export_validations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.TipoDoc).HasColumnName("tipo_doc").HasMaxLength(20);
            entity.Property(e => e.Clave).HasColumnName("clave").HasMaxLength(100);
            entity.Property(e => e.EnviadoEn).HasColumnName("enviado_en");
            entity.Property(e => e.WmsStatusId).HasColumnName("wms_status_id");
            entity.Property(e => e.WmsStatusDesc).HasColumnName("wms_status_desc").HasMaxLength(100);
            entity.Property(e => e.WmsErrorMsg).HasColumnName("wms_error_msg").HasMaxLength(500);
            entity.Property(e => e.ValidadoEn).HasColumnName("validado_en");
            entity.Property(e => e.Intentos).HasColumnName("intentos");
            entity.HasIndex(e => new { e.CompanyId, e.TipoDoc, e.Clave }).IsUnique().HasDatabaseName("ix_wms_oracle_export_validations_company_tipodoc_clave");
        });
    }
}
