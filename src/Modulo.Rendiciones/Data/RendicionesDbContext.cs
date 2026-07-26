using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Data;

/// <summary>
/// Base de datos propia del módulo (SQL Server, ajena al SAP de la organización),
/// resuelta self-service vía el contrato de conexión externa de la plataforma (ver
/// PENDIENTE.md -- ISqlServerService/equivalente todavía no existe en
/// PortalSaas.Abstractions, hueco pendiente antes de que este DbContext se pueda
/// registrar de verdad en ModuloRendiciones.RegisterServices).
///
/// Nombres de tabla/columna siguen la convención obligatoria de la plataforma
/// (docs/01-CONVENCION-NOMBRES-BD.md) aunque esta base sea propia del plugin: inglés,
/// plural, snake_case, "id" surrogate siempre, organization_id/company_id en toda
/// tabla de negocio -- ver docs/09-GUIA-DESARROLLO-PLUGINS.md §6.
/// </summary>
public class RendicionesDbContext : DbContext
{
    public RendicionesDbContext(DbContextOptions<RendicionesDbContext> options)
        : base(options)
    {
    }

    public DbSet<ExpenseFund> ExpenseFunds => Set<ExpenseFund>();
    public DbSet<ExpenseType> ExpenseTypes => Set<ExpenseType>();
    public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();
    public DbSet<ExpenseReportLine> ExpenseReportLines => Set<ExpenseReportLine>();
    public DbSet<ExpenseReportAction> ExpenseReportActions => Set<ExpenseReportAction>();
    public DbSet<ExpenseReceipt> ExpenseReceipts => Set<ExpenseReceipt>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<ExpensePolicy> ExpensePolicies => Set<ExpensePolicy>();
    public DbSet<UserCostCenter> UserCostCenters => Set<UserCostCenter>();
    public DbSet<ExpenseApprovalGroup> ExpenseApprovalGroups => Set<ExpenseApprovalGroup>();
    public DbSet<ExpenseApprovalGroupLevel> ExpenseApprovalGroupLevels => Set<ExpenseApprovalGroupLevel>();
    public DbSet<ExpenseApprovalGroupMember> ExpenseApprovalGroupMembers => Set<ExpenseApprovalGroupMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExpenseFund>(e =>
        {
            e.ToTable("expense_funds");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.CostCenterCode).HasColumnName("cost_center_code").HasMaxLength(50);
            e.Property(x => x.CostCenterName).HasColumnName("cost_center_name").HasMaxLength(200);
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");
            e.Property(x => x.DeliveredAt).HasColumnName("delivered_at");
            e.Property(x => x.SettlementDueAt).HasColumnName("settlement_due_at");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_expense_funds_company_id");
        });

        modelBuilder.Entity<ExpenseType>(e =>
        {
            e.ToTable("expense_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.SapGlAccount).HasColumnName("sap_gl_account").HasMaxLength(30);
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.IsMileage).HasColumnName("is_mileage");
            e.Property(x => x.RatePerKm).HasColumnName("rate_per_km").HasColumnType("decimal(18,2)");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_expense_types_company_id");
        });

        modelBuilder.Entity<DocumentType>(e =>
        {
            e.ToTable("document_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.AppliesTax).HasColumnName("applies_tax");
            e.Property(x => x.TaxPercentage).HasColumnName("tax_percentage").HasColumnType("decimal(5,2)");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_document_types_company_id");
        });

        modelBuilder.Entity<UserCostCenter>(e =>
        {
            e.ToTable("user_cost_centers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.CostCenterCode).HasColumnName("cost_center_code").HasMaxLength(50).IsRequired();
            e.Property(x => x.CostCenterName).HasColumnName("cost_center_name").HasMaxLength(200);
            e.HasIndex(x => new { x.CompanyId, x.UserId }).HasDatabaseName("ix_user_cost_centers_company_id_user_id");
        });

        modelBuilder.Entity<ExpensePolicy>(e =>
        {
            e.ToTable("expense_policies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.ExpenseTypeId).HasColumnName("expense_type_id").IsRequired();
            e.Property(x => x.MaxAmount).HasColumnName("max_amount").HasColumnType("decimal(18,2)");
            e.Property(x => x.IsBlocking).HasColumnName("is_blocking");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.HasOne(x => x.ExpenseType)
                .WithMany()
                .HasForeignKey(x => x.ExpenseTypeId)
                .HasConstraintName("fk_expense_policies_expense_types")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseApprovalGroup>(e =>
        {
            e.ToTable("expense_approval_groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<ExpenseApprovalGroupLevel>(e =>
        {
            e.ToTable("expense_approval_group_levels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.ExpenseApprovalGroupId).HasColumnName("expense_approval_group_id").IsRequired();
            e.Property(x => x.Level).HasColumnName("level").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.HasOne(x => x.ExpenseApprovalGroup)
                .WithMany()
                .HasForeignKey(x => x.ExpenseApprovalGroupId)
                .HasConstraintName("fk_expense_approval_group_levels_expense_approval_groups")
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ExpenseApprovalGroupId, x.Level })
                .IsUnique()
                .HasDatabaseName("uq_expense_approval_group_levels_group_id_level");
        });

        modelBuilder.Entity<ExpenseApprovalGroupMember>(e =>
        {
            e.ToTable("expense_approval_group_members");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.ExpenseApprovalGroupId).HasColumnName("expense_approval_group_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.HasOne(x => x.ExpenseApprovalGroup)
                .WithMany()
                .HasForeignKey(x => x.ExpenseApprovalGroupId)
                .HasConstraintName("fk_expense_approval_group_members_expense_approval_groups")
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ExpenseApprovalGroupId, x.UserId })
                .IsUnique()
                .HasDatabaseName("uq_expense_approval_group_members_group_id_user_id");
        });

        modelBuilder.Entity<ExpenseReceipt>(e =>
        {
            e.ToTable("expense_receipts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(260).IsRequired();
            e.Property(x => x.MimeType).HasColumnName("mime_type").HasMaxLength(100).IsRequired();
            e.Property(x => x.SizeBytes).HasColumnName("size_bytes");
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
        });

        modelBuilder.Entity<ExpenseReport>(e =>
        {
            e.ToTable("expense_reports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.ExpenseFundId).HasColumnName("expense_fund_id");
            e.Property(x => x.CostCenterCode).HasColumnName("cost_center_code").HasMaxLength(50);
            e.Property(x => x.CostCenterName).HasColumnName("cost_center_name").HasMaxLength(200);
            e.Property(x => x.Round).HasColumnName("round");
            e.Property(x => x.ExpenseApprovalGroupId).HasColumnName("expense_approval_group_id");
            e.Property(x => x.CurrentLevel).HasColumnName("current_level");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
            e.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
            e.HasOne<ExpenseFund>()
                .WithMany()
                .HasForeignKey(x => x.ExpenseFundId)
                .HasConstraintName("fk_expense_reports_expense_funds")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ExpenseApprovalGroup>()
                .WithMany()
                .HasForeignKey(x => x.ExpenseApprovalGroupId)
                .HasConstraintName("fk_expense_reports_expense_approval_groups")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_expense_reports_company_id");
            e.HasMany(x => x.Lines)
                .WithOne(x => x.ExpenseReport)
                .HasForeignKey(x => x.ExpenseReportId)
                .HasConstraintName("fk_expense_report_lines_expense_reports")
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExpenseReportLine>(e =>
        {
            e.ToTable("expense_report_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(x => x.ExpenseReportId).HasColumnName("expense_report_id");
            e.Property(x => x.ExpenseTypeId).HasColumnName("expense_type_id");
            e.Property(x => x.DocumentTypeId).HasColumnName("document_type_id");
            e.Property(x => x.Date).HasColumnName("expense_date");
            e.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(18,2)");
            e.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasColumnType("decimal(18,2)");
            e.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(50);
            e.Property(x => x.SupplierTaxId).HasColumnName("supplier_tax_id").HasMaxLength(20);
            e.Property(x => x.SupplierName).HasColumnName("supplier_name").HasMaxLength(200);
            e.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(500);
            e.Property(x => x.ExpenseReceiptId).HasColumnName("expense_receipt_id");
            e.Property(x => x.Origin).HasColumnName("origin").HasMaxLength(300);
            e.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(300);
            e.Property(x => x.DistanceKm).HasColumnName("distance_km").HasColumnType("decimal(10,2)");
            e.Property(x => x.AppliedRatePerKm).HasColumnName("applied_rate_per_km").HasColumnType("decimal(18,2)");
            e.HasOne(x => x.ExpenseType)
                .WithMany()
                .HasForeignKey(x => x.ExpenseTypeId)
                .HasConstraintName("fk_expense_report_lines_expense_types")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DocumentType)
                .WithMany()
                .HasForeignKey(x => x.DocumentTypeId)
                .HasConstraintName("fk_expense_report_lines_document_types")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ExpenseReceipt)
                .WithMany()
                .HasForeignKey(x => x.ExpenseReceiptId)
                .HasConstraintName("fk_expense_report_lines_expense_receipts")
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CompanyId).HasDatabaseName("ix_expense_report_lines_company_id");
        });

        modelBuilder.Entity<ExpenseReportAction>(e =>
        {
            e.ToTable("expense_report_actions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.ExpenseReportId).HasColumnName("expense_report_id").IsRequired();
            e.Property(x => x.Level).HasColumnName("level").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Decision).HasColumnName("decision").HasMaxLength(20).IsRequired();
            e.Property(x => x.Comment).HasColumnName("comment").HasMaxLength(500);
            e.Property(x => x.OccurredAt).HasColumnName("occurred_at");
            e.HasOne<ExpenseReport>()
                .WithMany()
                .HasForeignKey(x => x.ExpenseReportId)
                .HasConstraintName("fk_expense_report_actions_expense_reports")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
