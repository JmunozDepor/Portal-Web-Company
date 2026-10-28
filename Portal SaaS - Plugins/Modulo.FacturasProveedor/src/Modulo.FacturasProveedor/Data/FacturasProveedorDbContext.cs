using System.Text;
using Microsoft.EntityFrameworkCore;
using Modulo.FacturasProveedor.Models;

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

    public DbSet<FpSettings> Settings => Set<FpSettings>();
    public DbSet<FpRuleSetting> RuleSettings => Set<FpRuleSetting>();
    public DbSet<FpConfigEvent> ConfigEvents => Set<FpConfigEvent>();
    public DbSet<FpSupplierProfile> SupplierProfiles => Set<FpSupplierProfile>();
    public DbSet<FpSupplierRuleOverride> SupplierRuleOverrides => Set<FpSupplierRuleOverride>();
    public DbSet<FpApproverGroup> ApproverGroups => Set<FpApproverGroup>();
    public DbSet<FpApproverGroupMember> ApproverGroupMembers => Set<FpApproverGroupMember>();
    public DbSet<FpInvoiceReview> InvoiceReviews => Set<FpInvoiceReview>();
    public DbSet<FpReviewRuleResult> ReviewRuleResults => Set<FpReviewRuleResult>();
    public DbSet<FpReviewEvent> ReviewEvents => Set<FpReviewEvent>();
    public DbSet<FpSapQuery> SapQueries => Set<FpSapQuery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FpSettings>(e =>
        {
            e.ToTable("fp_settings");
            e.HasIndex(x => x.CompanyId).IsUnique().HasDatabaseName("uq_fp_settings_company_id");
            e.Property(x => x.FinalActionWhenNoObjection).HasMaxLength(30);
            e.Property(x => x.Mode).HasMaxLength(20);
        });

        modelBuilder.Entity<FpRuleSetting>(e =>
        {
            e.ToTable("fp_rule_settings");
            e.HasIndex(x => new { x.CompanyId, x.RuleCode }).IsUnique().HasDatabaseName("uq_fp_rule_settings_company_id_rule_code");
            e.Property(x => x.RuleCode).HasMaxLength(50);
            e.Property(x => x.Action).HasMaxLength(30);
        });

        modelBuilder.Entity<FpConfigEvent>(e =>
        {
            e.ToTable("fp_config_events");
            e.HasIndex(x => new { x.CompanyId, x.OccurredAt });
            e.Property(x => x.EntityType).HasMaxLength(30);
            e.Property(x => x.EntityKey).HasMaxLength(80);
        });

        modelBuilder.Entity<FpSupplierProfile>(e =>
        {
            e.ToTable("fp_supplier_profiles");
            e.HasIndex(x => new { x.CompanyId, x.TaxId }).IsUnique().HasDatabaseName("uq_fp_supplier_profiles_company_id_tax_id");
            e.Property(x => x.CardCode).HasMaxLength(50);
            e.Property(x => x.TaxId).HasMaxLength(12);
            e.Property(x => x.SupplierName).HasMaxLength(100);
            e.Property(x => x.Condition).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasMany(x => x.Overrides).WithOne().HasForeignKey(o => o.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FpSupplierRuleOverride>(e =>
        {
            e.ToTable("fp_supplier_rule_overrides");
            e.HasIndex(x => new { x.ProfileId, x.RuleCode }).IsUnique().HasDatabaseName("uq_fp_supplier_rule_overrides_profile_id_rule_code");
            e.Property(x => x.RuleCode).HasMaxLength(50);
            e.Property(x => x.Mode).HasMaxLength(20);
            e.Property(x => x.Action).HasMaxLength(30);
        });

        modelBuilder.Entity<FpApproverGroup>(e =>
        {
            e.ToTable("fp_approver_groups");
            e.HasIndex(x => x.CompanyId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FpApproverGroupMember>(e =>
        {
            e.ToTable("fp_approver_group_members");
            e.HasKey(x => new { x.GroupId, x.UserId });
        });

        modelBuilder.Entity<FpInvoiceReview>(e =>
        {
            e.ToTable("fp_invoice_reviews");
            e.HasIndex(x => new { x.CompanyId, x.ReceivedId }).IsUnique().HasDatabaseName("uq_fp_invoice_reviews_company_id_received_id");
            e.HasIndex(x => new { x.CompanyId, x.Status });
            e.Property(x => x.IssuerTaxId).HasMaxLength(12);
            e.Property(x => x.IssuerName).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ProposedAction).HasMaxLength(30);
            e.Property(x => x.RecommendedAction).HasMaxLength(30);
            e.Property(x => x.DecidedAction).HasMaxLength(30);
            e.Property(x => x.DecisionSource).HasMaxLength(60);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ClaimType).HasMaxLength(30);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.Property(x => x.ConcurrencyStamp).IsConcurrencyToken();
        });

        modelBuilder.Entity<FpReviewRuleResult>(e =>
        {
            e.ToTable("fp_review_rule_results");
            e.HasIndex(x => x.ReviewId);
            e.Property(x => x.RuleCode).HasMaxLength(50);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Action).HasMaxLength(30);
            e.Property(x => x.Message).HasMaxLength(500);
            e.HasOne<FpInvoiceReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FpReviewEvent>(e =>
        {
            e.ToTable("fp_review_events");
            e.HasIndex(x => x.ReviewId);
            e.Property(x => x.EventType).HasMaxLength(40);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.HasOne<FpInvoiceReview>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FpSapQuery>(e =>
        {
            e.ToTable("fp_sap_queries");
            e.HasIndex(x => new { x.CompanyId, x.QueryKind }).IsUnique().HasDatabaseName("uq_fp_sap_queries_company_id_query_kind");
            e.Property(x => x.QueryKind).HasMaxLength(40);
        });

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
