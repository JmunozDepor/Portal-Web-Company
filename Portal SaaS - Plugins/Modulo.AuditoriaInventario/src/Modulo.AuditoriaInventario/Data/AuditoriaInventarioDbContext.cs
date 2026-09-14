using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Models;

namespace Modulo.AuditoriaInventario.Data;

/// <summary>
/// Base de datos propia del módulo, ajena a la base compartida de la plataforma y a
/// cualquier otra base de otro plugin, resuelta self-service vía
/// IExternalDatabaseConnectionService (PortalSaas.Abstractions) -- registrada en
/// AuditoriaInventarioModule.RegisterServices, que decide en runtime si usa
/// UseNpgsql o UseSqlServer según lo que devuelva ese servicio (ver
/// ExternalDatabaseConnection.EngineType). MOTOR DUAL: ninguna columna usa
/// HasColumnType con un tipo SQL específico de un solo motor -- HasPrecision en vez
/// de HasColumnType("decimal(...)"), igual que RendicionesDbContext.
///
/// Nombres de tabla/columna siguen la convención obligatoria de la plataforma
/// (docs/01-CONVENCION-NOMBRES-BD.md): inglés, plural, snake_case, "id" surrogate
/// siempre, company_id en toda tabla de negocio.
/// </summary>
public class AuditoriaInventarioDbContext : DbContext
{
    public AuditoriaInventarioDbContext(DbContextOptions<AuditoriaInventarioDbContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<InventorySector> InventorySectors => Set<InventorySector>();
    public DbSet<CaptureUser> CaptureUsers => Set<CaptureUser>();
    public DbSet<InventorySession> InventorySessions => Set<InventorySession>();
    public DbSet<InventoryCapture> InventoryCaptures => Set<InventoryCapture>();
    public DbSet<FrozenInventorySnapshot> FrozenInventorySnapshots => Set<FrozenInventorySnapshot>();
    public DbSet<FrozenInventoryLine> FrozenInventoryLines => Set<FrozenInventoryLine>();
    public DbSet<InventoryDifference> InventoryDifferences => Set<InventoryDifference>();
    public DbSet<InventoryAdjustment> InventoryAdjustments => Set<InventoryAdjustment>();
    public DbSet<SapAdjustmentQueueItem> SapAdjustmentQueueItems => Set<SapAdjustmentQueueItem>();
    public DbSet<CaptureAuthToken> CaptureAuthTokens => Set<CaptureAuthToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(50).IsRequired();
            e.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(300);
            e.Property(x => x.Brand).HasColumnName("brand").HasMaxLength(100);
            e.Property(x => x.Line).HasColumnName("line").HasMaxLength(100);
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired();
            e.Property(x => x.LoadedAt).HasColumnName("loaded_at").IsRequired();
            e.Property(x => x.SapMaterialCode).HasColumnName("sap_material_code").HasMaxLength(50);
            // Búsqueda por código de barra es la ruta caliente de la PWA (150K+ filas) --
            // único por compañía, nunca dos productos con el mismo barcode en la misma Company.
            e.HasIndex(x => new { x.CompanyId, x.Barcode }).IsUnique().HasDatabaseName("uq_products_company_id_barcode");
            e.HasIndex(x => new { x.CompanyId, x.ProductCode }).HasDatabaseName("ix_products_company_id_product_code");
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("branches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.BranchCode).HasColumnName("branch_code").HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.SapCompanyCode).HasColumnName("sap_company_code").HasMaxLength(20);
            e.Property(x => x.SapWarehouseCode).HasColumnName("sap_warehouse_code").HasMaxLength(20);
            e.HasIndex(x => new { x.CompanyId, x.BranchCode }).IsUnique().HasDatabaseName("uq_branches_company_id_branch_code");
        });

        modelBuilder.Entity<InventorySector>(e =>
        {
            e.ToTable("inventory_sectors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.BranchId).HasColumnName("branch_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId)
                .HasConstraintName("fk_inventory_sectors_branches").OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_inventory_sectors_company_id");
        });

        modelBuilder.Entity<CaptureUser>(e =>
        {
            e.ToTable("capture_users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(50).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").IsRequired();
            e.Property(x => x.PasswordSalt).HasColumnName("password_salt").HasMaxLength(200).IsRequired();
            e.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200);
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.HasIndex(x => new { x.CompanyId, x.Username }).IsUnique().HasDatabaseName("uq_capture_users_company_id_username");
        });

        modelBuilder.Entity<InventorySession>(e =>
        {
            e.ToTable("inventory_sessions");
            e.HasKey(x => x.Id);
            // Sin ValueGeneratedOnAdd: el Id lo genera el cliente (PWA) offline, ver
            // el comentario en InventorySession.cs.
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            e.Property(x => x.InventoryNumber).HasColumnName("inventory_number").HasMaxLength(50).IsRequired();
            e.Property(x => x.ResponsibleUserId).HasColumnName("responsible_user_id").IsRequired();
            e.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
            e.Property(x => x.ClosedAt).HasColumnName("closed_at");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ValidateAgainstMaster).HasColumnName("validate_against_master").IsRequired();
            e.Property(x => x.MasterSnapshotAt).HasColumnName("master_snapshot_at");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId)
                .HasConstraintName("fk_inventory_sessions_branches").OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CaptureUser>().WithMany().HasForeignKey(x => x.ResponsibleUserId)
                .HasConstraintName("fk_inventory_sessions_capture_users").OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompanyId, x.BranchId, x.Status }).HasDatabaseName("ix_inventory_sessions_company_branch_status");
        });

        modelBuilder.Entity<InventoryCapture>(e =>
        {
            e.ToTable("inventory_captures");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            e.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
            e.Property(x => x.SectorId).HasColumnName("sector_id").IsRequired();
            e.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(50).IsRequired();
            e.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(50);
            e.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
            e.Property(x => x.InMaster).HasColumnName("in_master");
            e.Property(x => x.CapturedByUserId).HasColumnName("captured_by_user_id").IsRequired();
            e.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
            e.Property(x => x.SyncedAt).HasColumnName("synced_at");
            e.HasOne<InventorySession>().WithMany().HasForeignKey(x => x.SessionId)
                .HasConstraintName("fk_inventory_captures_inventory_sessions").OnDelete(DeleteBehavior.Cascade);
            e.HasOne<InventorySector>().WithMany().HasForeignKey(x => x.SectorId)
                .HasConstraintName("fk_inventory_captures_inventory_sectors").OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CaptureUser>().WithMany().HasForeignKey(x => x.CapturedByUserId)
                .HasConstraintName("fk_inventory_captures_capture_users").OnDelete(DeleteBehavior.Restrict);
            // Índice compuesto = la lógica de acumulación (sumar cantidad si se repite el
            // mismo código en el mismo sector de la misma sesión, equivalente a
            // GrabarCapturaAsync de la versión MAUI).
            e.HasIndex(x => new { x.SessionId, x.SectorId, x.Barcode }).HasDatabaseName("ix_inventory_captures_session_sector_barcode");
        });

        modelBuilder.Entity<FrozenInventorySnapshot>(e =>
        {
            e.ToTable("frozen_inventory_snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.BranchId).HasColumnName("branch_id").IsRequired();
            e.Property(x => x.InventoryNumber).HasColumnName("inventory_number").HasMaxLength(50).IsRequired();
            e.Property(x => x.LoadedAt).HasColumnName("loaded_at").IsRequired();
            e.Property(x => x.LoadedByUserId).HasColumnName("loaded_by_user_id").IsRequired();
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(260).IsRequired();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId)
                .HasConstraintName("fk_frozen_inventory_snapshots_branches").OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CompanyId, x.BranchId, x.InventoryNumber }).HasDatabaseName("ix_frozen_snapshots_company_branch_inventory_number");
        });

        modelBuilder.Entity<FrozenInventoryLine>(e =>
        {
            e.ToTable("frozen_inventory_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.SnapshotId).HasColumnName("snapshot_id").IsRequired();
            e.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(50).IsRequired();
            e.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(50);
            e.Property(x => x.Quantity).HasColumnName("quantity").IsRequired();
            e.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 4);
            e.HasOne<FrozenInventorySnapshot>().WithMany().HasForeignKey(x => x.SnapshotId)
                .HasConstraintName("fk_frozen_inventory_lines_frozen_inventory_snapshots").OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SnapshotId, x.Barcode }).HasDatabaseName("ix_frozen_inventory_lines_snapshot_barcode");
        });

        modelBuilder.Entity<InventoryDifference>(e =>
        {
            e.ToTable("inventory_differences");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
            e.Property(x => x.SnapshotId).HasColumnName("snapshot_id").IsRequired();
            e.Property(x => x.SectorId).HasColumnName("sector_id");
            e.Property(x => x.Barcode).HasColumnName("barcode").HasMaxLength(50).IsRequired();
            e.Property(x => x.CapturedQuantity).HasColumnName("captured_quantity").IsRequired();
            e.Property(x => x.FrozenQuantity).HasColumnName("frozen_quantity").IsRequired();
            e.Property(x => x.QuantityDiff).HasColumnName("quantity_diff").IsRequired();
            e.Property(x => x.AmountDiff).HasColumnName("amount_diff").HasPrecision(18, 2).IsRequired();
            e.Property(x => x.CalculatedAt).HasColumnName("calculated_at").IsRequired();
            e.HasOne<InventorySession>().WithMany().HasForeignKey(x => x.SessionId)
                .HasConstraintName("fk_inventory_differences_inventory_sessions").OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FrozenInventorySnapshot>().WithMany().HasForeignKey(x => x.SnapshotId)
                .HasConstraintName("fk_inventory_differences_frozen_inventory_snapshots").OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SessionId).HasDatabaseName("ix_inventory_differences_session_id");
        });

        modelBuilder.Entity<InventoryAdjustment>(e =>
        {
            e.ToTable("inventory_adjustments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.DifferenceId).HasColumnName("difference_id").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ProposedByUserId).HasColumnName("proposed_by_user_id").IsRequired();
            e.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
            e.Property(x => x.ProposedAt).HasColumnName("proposed_at").IsRequired();
            e.Property(x => x.ApprovedAt).HasColumnName("approved_at");
            e.HasOne<InventoryDifference>().WithMany().HasForeignKey(x => x.DifferenceId)
                .HasConstraintName("fk_inventory_adjustments_inventory_differences").OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status).HasDatabaseName("ix_inventory_adjustments_status");
        });

        modelBuilder.Entity<SapAdjustmentQueueItem>(e =>
        {
            e.ToTable("sap_adjustment_queue_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.AdjustmentId).HasColumnName("adjustment_id").IsRequired();
            e.Property(x => x.SapCompanyCode).HasColumnName("sap_company_code").HasMaxLength(20).IsRequired();
            e.Property(x => x.SapWarehouseCode).HasColumnName("sap_warehouse_code").HasMaxLength(20).IsRequired();
            e.Property(x => x.SapMaterialCode).HasColumnName("sap_material_code").HasMaxLength(50).IsRequired();
            e.Property(x => x.Quantity).HasColumnName("quantity").HasPrecision(18, 3).IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.GeneratedAt).HasColumnName("generated_at").IsRequired();
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.Property(x => x.SapDocumentNumber).HasColumnName("sap_document_number").HasMaxLength(50);
            e.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(500);
            e.HasOne<InventoryAdjustment>().WithMany().HasForeignKey(x => x.AdjustmentId)
                .HasConstraintName("fk_sap_adjustment_queue_items_inventory_adjustments").OnDelete(DeleteBehavior.Restrict);
            // El proceso que consume la cola filtra por Status='READY' -- índice para ese polling.
            e.HasIndex(x => x.Status).HasDatabaseName("ix_sap_adjustment_queue_items_status");
        });

        modelBuilder.Entity<CaptureAuthToken>(e =>
        {
            e.ToTable("capture_auth_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.CaptureUserId).HasColumnName("capture_user_id").IsRequired();
            e.Property(x => x.Token).HasColumnName("token").HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            e.HasOne<CaptureUser>().WithMany().HasForeignKey(x => x.CaptureUserId)
                .HasConstraintName("fk_capture_auth_tokens_capture_users").OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Token).IsUnique().HasDatabaseName("uq_capture_auth_tokens_token");
        });
    }
}
