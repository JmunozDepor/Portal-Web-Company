using Microsoft.EntityFrameworkCore;
using PortalSaas.Data.Entities;
using PortalSaas.Data.Entities.Integraciones;

namespace PortalSaas.Data;

/// <summary>
/// Base propia de la plataforma (equivalente a PORTALWEB de PortalSAP_v2). Motor dual
/// -- PostgreSQL (SaaS / instalaciones nuevas) o SQL Server (instalaciones on-premise
/// que ya tienen SQL Server, ej. Comercial Depor en sqlsap.cdepor.cl) -- ver
/// docs/02-ARQUITECTURA-BASE-DE-DATOS.md §7. Por eso este archivo NO usa nada
/// específico de un solo proveedor (nada de gen_random_uuid()/NEWID(), nada de
/// UseIdentityAlwaysColumn de Npgsql): los valores por defecto (Guid, timestamps) se
/// generan en C#, en las entidades -- ver Entities/.
///
/// NO modela la conexión hacia el SAP de cada cliente (HANA/SQL Server) -- eso sigue
/// siendo un motor totalmente aparte, portado tal cual desde PortalSAP.Core.Sap (ver
/// ARCHITECTURE.md §2). Este DbContext es solo para la base propia de la plataforma.
/// </summary>
public sealed class PortalSaasDbContext : DbContext
{
    public PortalSaasDbContext(DbContextOptions<PortalSaasDbContext> options) : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlatformModule> PlatformModules => Set<PlatformModule>();
    public DbSet<PlanModule> PlanModules => Set<PlanModule>();
    public DbSet<OrganizationModule> OrganizationModules => Set<OrganizationModule>();
    public DbSet<OrganizationDocumentPermission> OrganizationDocumentPermissions => Set<OrganizationDocumentPermission>();
    public DbSet<GenericImportUserField> GenericImportUserFields => Set<GenericImportUserField>();
    public DbSet<GenericImportConfig> GenericImportConfigs => Set<GenericImportConfig>();
    public DbSet<GenericImportConfigField> GenericImportConfigFields => Set<GenericImportConfigField>();
    public DbSet<GenericImportValidationRuleAssignment> GenericImportValidationRuleAssignments => Set<GenericImportValidationRuleAssignment>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<OnPremiseLicense> OnPremiseLicenses => Set<OnPremiseLicense>();
    public DbSet<OnPremiseLicenseConflict> OnPremiseLicenseConflicts => Set<OnPremiseLicenseConflict>();
    public DbSet<Instance> Instances => Set<Instance>();
    public DbSet<ModuleExternalConnection> ModuleExternalConnections => Set<ModuleExternalConnection>();
    public DbSet<CompanyExternalConnection> CompanyExternalConnections => Set<CompanyExternalConnection>();
    public DbSet<CompanyModuleConnection> CompanyModuleConnections => Set<CompanyModuleConnection>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<EmailSettings> EmailSettings => Set<EmailSettings>();
    public DbSet<UsageMetric> UsageMetrics => Set<UsageMetric>();
    public DbSet<MenuGroup> MenuGroups => Set<MenuGroup>();
    public DbSet<OrganizationModuleVisibility> OrganizationModuleVisibilities => Set<OrganizationModuleVisibility>();
    public DbSet<OrganizationMenuOverride> OrganizationMenuOverrides => Set<OrganizationMenuOverride>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<PermissionAction> Actions => Set<PermissionAction>();
    public DbSet<ProfileAction> ProfileActions => Set<ProfileAction>();
    public DbSet<MenuGroupItem> MenuGroupItems => Set<MenuGroupItem>();
    public DbSet<UserMenuGroup> UserMenuGroups => Set<UserMenuGroup>();
    public DbSet<UserMenuProfile> UserMenuProfiles => Set<UserMenuProfile>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<UserHomeShortcut> UserHomeShortcuts => Set<UserHomeShortcut>();
    public DbSet<IntegrationDefinition> IntegrationDefinitions => Set<IntegrationDefinition>();
    public DbSet<IntegrationFieldMapping> IntegrationFieldMappings => Set<IntegrationFieldMapping>();
    public DbSet<IntegrationRunLog> IntegrationRunLogs => Set<IntegrationRunLog>();
    public DbSet<ApiClientCredential> ApiClientCredentials => Set<ApiClientCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Nombre de tabla explícito en plural en TODAS las entidades -- el default de
        // EF Core/EFCore.NamingConventions cae a singular (CLR type name), lo que
        // contradice docs/01-CONVENCION-NOMBRES-BD.md §2. No confiar en la convención
        // para esto, fijarlo siempre a mano. Largos de columna (HasMaxLength) calzan
        // con los varchar(N) de docs/03-MODELO-CORE-COMERCIAL.md §4 -- portable entre
        // Postgres (varchar(N)) y SQL Server (nvarchar(N)), EF Core lo resuelve solo.
        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("organizations", t =>
            {
                t.HasCheckConstraint("ck_organizations_mode",
                    $"mode in ('{OrganizationMode.Saas}', '{OrganizationMode.OnPremise}')");
                t.HasCheckConstraint("ck_organizations_status",
                    $"status in ('{OrganizationStatus.Trial}', '{OrganizationStatus.Active}', '{OrganizationStatus.Suspended}', '{OrganizationStatus.Cancelled}')");
            });
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.Property(e => e.LegalName).HasMaxLength(200);
            entity.Property(e => e.Slug).HasMaxLength(50);
            entity.Property(e => e.TaxId).HasMaxLength(20);
            entity.Property(e => e.Country).HasMaxLength(10);
            entity.Property(e => e.Mode).HasMaxLength(20);
            entity.Property(e => e.Status).HasMaxLength(20);
        });

        modelBuilder.Entity<PlatformAdmin>(entity =>
        {
            entity.ToTable("platform_admins");
            // Sin OrganizationId que lo scope -- a diferencia de User.Email, acá el
            // correo es único GLOBAL (ver Entities/PlatformAdmin.cs).
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(150).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(300);
            entity.Property(e => e.PasswordSalt).HasMaxLength(100);
        });

        modelBuilder.Entity<Plan>(entity =>
        {
            entity.ToTable("plans");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(30);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.MonthlyPrice).HasPrecision(12, 2);
            entity.Property(e => e.Currency).HasMaxLength(3).HasDefaultValue("CLP");
        });

        modelBuilder.Entity<PlatformModule>(entity =>
        {
            entity.ToTable("platform_modules");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.HasOne(e => e.ExclusiveOrganization).WithMany().HasForeignKey(e => e.ExclusiveOrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlanModule>(entity =>
        {
            entity.ToTable("plan_modules");
            entity.HasKey(e => new { e.PlanId, e.ModuleId });
            entity.HasOne(e => e.Plan).WithMany(p => p.PlanModules).HasForeignKey(e => e.PlanId);
            entity.HasOne(e => e.Module).WithMany(m => m.PlanModules).HasForeignKey(e => e.ModuleId);
        });

        modelBuilder.Entity<OrganizationModule>(entity =>
        {
            entity.ToTable("organization_modules");
            entity.HasKey(e => new { e.OrganizationId, e.ModuleId });
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Module).WithMany(m => m.OrganizationModules).HasForeignKey(e => e.ModuleId);
        });

        modelBuilder.Entity<OrganizationDocumentPermission>(entity =>
        {
            entity.ToTable("organization_document_permissions");
            entity.HasIndex(e => new { e.OrganizationId, e.Engine, e.DocumentType }).IsUnique();
            entity.Property(e => e.Engine).HasMaxLength(20);
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
        });

        modelBuilder.Entity<GenericImportUserField>(entity =>
        {
            entity.ToTable("generic_import_user_fields");
            entity.Property(e => e.Module).HasMaxLength(20);
            entity.Property(e => e.Level).HasMaxLength(10);
            entity.Property(e => e.Label).HasMaxLength(100);
            entity.Property(e => e.SapFieldName).HasMaxLength(100);
            entity.Property(e => e.DataType).HasMaxLength(10);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId);
        });

        modelBuilder.Entity<GenericImportConfig>(entity =>
        {
            entity.ToTable("generic_import_configs");
            entity.HasIndex(e => new { e.CompanyId, e.Module, e.DocumentType, e.LineType, e.BusinessPartnerCardCode }).IsUnique();
            entity.Property(e => e.Module).HasMaxLength(20);
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.Property(e => e.LineType).HasMaxLength(10);
            entity.Property(e => e.BusinessPartnerCardCode).HasMaxLength(15);
            entity.Property(e => e.GroupingColumn).HasMaxLength(10);
            entity.Property(e => e.Alias).HasMaxLength(100);
            entity.Property(e => e.PriceSource).HasMaxLength(20);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId);
        });

        modelBuilder.Entity<GenericImportConfigField>(entity =>
        {
            entity.ToTable("generic_import_config_fields");
            entity.Property(e => e.LogicalField).HasMaxLength(30);
            entity.Property(e => e.ExcelColumn).HasMaxLength(10);
            entity.Property(e => e.FixedValue).HasMaxLength(200);
            entity.HasOne(e => e.Config).WithMany(c => c.Fields).HasForeignKey(e => e.ConfigId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.UserField).WithMany().HasForeignKey(e => e.UserFieldId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GenericImportValidationRuleAssignment>(entity =>
        {
            entity.ToTable("generic_import_validation_rule_assignments");
            entity.HasIndex(e => new { e.ConfigId, e.RuleType }).IsUnique();
            entity.Property(e => e.RuleType).HasMaxLength(30);
            entity.Property(e => e.Severity).HasMaxLength(10);
            entity.HasOne(e => e.Config).WithMany(c => c.ValidationRules).HasForeignKey(e => e.ConfigId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("subscriptions", t => t.HasCheckConstraint("ck_subscriptions_status",
                $"status in ('{SubscriptionStatus.Trial}', '{SubscriptionStatus.Active}', '{SubscriptionStatus.PastDue}', '{SubscriptionStatus.Cancelled}')"));
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.PaymentProvider).HasMaxLength(30);
            entity.Property(e => e.ExternalPaymentReference).HasMaxLength(100);
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Plan).WithMany(p => p.Subscriptions).HasForeignKey(e => e.PlanId);
        });

        modelBuilder.Entity<OnPremiseLicense>(entity =>
        {
            entity.ToTable("on_premise_licenses", t => t.HasCheckConstraint("ck_on_premise_licenses_status",
                $"status in ('{OnPremiseLicenseStatus.Active}', '{OnPremiseLicenseStatus.Revoked}', '{OnPremiseLicenseStatus.Expired}')"));
            entity.HasIndex(e => e.ActivationKey).IsUnique();
            entity.Property(e => e.ActivationKey).HasMaxLength(100);
            entity.Property(e => e.InstallationFingerprint).HasMaxLength(200);
            entity.Property(e => e.Status).HasMaxLength(20);
            // Sin HasMaxLength -- es un payload JSON + firma base64, largo variable
            // (nvarchar(max)/text según motor, EF Core lo resuelve solo).
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Plan).WithMany(p => p.OnPremiseLicenses).HasForeignKey(e => e.PlanId);
        });

        modelBuilder.Entity<OnPremiseLicenseConflict>(entity =>
        {
            entity.ToTable("on_premise_license_conflicts");
            entity.Property(e => e.ReportedFingerprint).HasMaxLength(200);
            entity.Property(e => e.ReportedIp).HasMaxLength(50);
            entity.Property(e => e.ResolvedByAdminEmail).HasMaxLength(200);
            entity.HasOne(e => e.OnPremiseLicense).WithMany(l => l.Conflicts).HasForeignKey(e => e.OnPremiseLicenseId);
        });

        modelBuilder.Entity<Instance>(entity =>
        {
            entity.ToTable("instances", t => t.HasCheckConstraint("ck_instances_engine_type",
                $"engine_type in ('{InstanceEngineType.Hana}', '{InstanceEngineType.SqlServer}')"));
            entity.HasIndex(e => new { e.OrganizationId, e.Name }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(50);
            entity.Property(e => e.Host).HasMaxLength(200);
            entity.Property(e => e.EngineType).HasMaxLength(20);
            entity.Property(e => e.TechnicalUsername).HasMaxLength(100);
            entity.Property(e => e.TechnicalSecretKey).HasMaxLength(200);
            entity.HasOne(e => e.Organization).WithMany(o => o.Instances).HasForeignKey(e => e.OrganizationId);
        });

        modelBuilder.Entity<ModuleExternalConnection>(entity =>
        {
            entity.ToTable("module_external_connections", t => t.HasCheckConstraint(
                "ck_module_external_connections_engine_type",
                $"engine_type in ('{ModuleExternalConnectionEngineType.Postgres}', '{ModuleExternalConnectionEngineType.SqlServer}')"));
            // CompanyId siempre obligatorio, sin fila global de organización (regla
            // dura, ver ModuleExternalConnection) -- único índice real: no puede haber
            // dos filas para la misma (CompanyId, ModuleCode).
            entity.HasIndex(e => new { e.CompanyId, e.ModuleCode })
                .IsUnique()
                .HasDatabaseName("uq_module_external_connections_company_module");
            entity.Property(e => e.ModuleCode).HasMaxLength(50);
            entity.Property(e => e.EngineType).HasMaxLength(20);
            entity.Property(e => e.Host).HasMaxLength(200);
            entity.Property(e => e.DatabaseName).HasMaxLength(100);
            entity.Property(e => e.TechnicalUsername).HasMaxLength(100);
            entity.Property(e => e.TechnicalSecretKey).HasMaxLength(200);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId);
        });

        modelBuilder.Entity<CompanyExternalConnection>(entity =>
        {
            entity.ToTable("company_external_connections", t => t.HasCheckConstraint(
                "ck_company_external_connections_tipo",
                "tipo in ('db_postgres', 'db_sqlserver', 'db_hana', 'http_api')"));
            entity.HasIndex(e => new { e.CompanyId, e.Nombre })
                .IsUnique()
                .HasDatabaseName("uq_company_external_connections_company_nombre");
            entity.Property(e => e.Nombre).HasMaxLength(100);
            entity.Property(e => e.Tipo).HasMaxLength(20);
            entity.Property(e => e.Host).HasMaxLength(200);
            entity.Property(e => e.BaseUrl).HasMaxLength(500);
            entity.Property(e => e.DatabaseName).HasMaxLength(100);
            entity.Property(e => e.TechnicalUsername).HasMaxLength(100);
            entity.Property(e => e.TechnicalSecretKey).HasMaxLength(500);
            // Restrict, no Cascade -- Company ya es alcanzable en cascada vía
            // Instance; un segundo camino en cascada rompe SQL Server (1785). El
            // borrado de una compañía con conexiones se bloquea con un chequeo
            // explícito de dependientes (ver Companies/Index), no en cascada.
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyModuleConnection>(entity =>
        {
            entity.ToTable("company_module_connections");
            entity.HasIndex(e => new { e.CompanyId, e.ModuleCode, e.Purpose })
                .IsUnique()
                .HasDatabaseName("uq_company_module_connections_company_module_purpose");
            entity.HasIndex(e => e.ConnectionId)
                .HasDatabaseName("ix_company_module_connections_connection_id");
            entity.Property(e => e.ModuleCode).HasMaxLength(50);
            entity.Property(e => e.Purpose).HasMaxLength(50);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Connection).WithMany(c => c.ModuleBindings).HasForeignKey(e => e.ConnectionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("companies");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(20);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.DatabaseName).HasMaxLength(50);
            entity.Property(e => e.ServiceLayerUrl).HasMaxLength(300);
            entity.Property(e => e.IntegrationUsername).HasMaxLength(100);
            entity.Property(e => e.IntegrationSecretKey).HasMaxLength(200);
            entity.Property(e => e.Country).HasMaxLength(10);
            entity.Property(e => e.TraceabilityUserUdfName).HasMaxLength(50);
            // Restrict, no Cascade -- Company ya es alcanzable en cascada vía
            // Instance (Organization -> Instance -> Company), así que un segundo
            // camino en cascada acá crea un ciclo. Postgres lo permite en silencio;
            // SQL Server lo rechaza al crear la tabla (1785) -- sin esto, el motor
            // dual deja de generar el mismo esquema en los dos proveedores.
            entity.HasOne(e => e.Organization).WithMany(o => o.Companies).HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Instance).WithMany(i => i.Companies).HasForeignKey(e => e.InstanceId);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasIndex(e => new { e.OrganizationId, e.Username }).IsUnique();
            // Email obligatorio y único dentro de la organización -- toda cuenta
            // queda siempre asociada a un correo real (ver Entities/User.cs).
            entity.HasIndex(e => new { e.OrganizationId, e.Email }).IsUnique();
            entity.Property(e => e.Username).HasMaxLength(100);
            entity.Property(e => e.Email).HasMaxLength(150).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(300);
            entity.Property(e => e.PasswordSalt).HasMaxLength(100);
            entity.Property(e => e.TwoFactorMethod).HasMaxLength(20);
            entity.Property(e => e.TwoFactorSecret).HasMaxLength(200);
            entity.HasOne(e => e.Organization).WithMany(o => o.Users).HasForeignKey(e => e.OrganizationId);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.Property(e => e.TokenHash).HasMaxLength(300);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.OrganizationId, e.IsRevoked });
            entity.Property(e => e.TokenHash).HasMaxLength(300);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.UserAgent).HasMaxLength(300);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            // Restrict, no Cascade -- mismo motivo exacto que Company.Organization más
            // arriba: UserSession ya es alcanzable en cascada vía User (Organization ->
            // User -> UserSession), un segundo camino directo acá crea el mismo ciclo
            // que SQL Server rechaza (error 1785).
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            // Valores revisados 2026-08-19 (consolidación de preferencias, ver
            // Entities/UserPreference.cs) -- antes light/dark/system, ninguno de
            // los 3 aplicado de verdad en ningún lado; ahora los 4 temas reales
            // que site.css/sidebar.css implementan.
            entity.ToTable("user_preferences", t => t.HasCheckConstraint("ck_user_preferences_theme",
                $"theme in ('{UserThemePreference.Claro}', '{UserThemePreference.Oscuro}', '{UserThemePreference.Teal}', '{UserThemePreference.Violeta}')"));
            // 1:1 con User -- comparte PK, no tiene id propio (ver Entities/UserPreference.cs).
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Locale).HasMaxLength(10);
            entity.Property(e => e.Timezone).HasMaxLength(50);
            entity.Property(e => e.Theme).HasMaxLength(20);
            entity.HasOne(e => e.User).WithOne(u => u.Preference).HasForeignKey<UserPreference>(e => e.UserId);
            // ClientSetNull, no SetNull -- SqlServer rechaza SetNull acá con "may cause
            // cycles or multiple cascade paths" (bug real, 2026-08-07: la ruta
            // Company->UserPreference compite con otras rutas de cascada ya existentes
            // desde Company). ClientSetNull logra el mismo resultado (borrar una Company
            // no debe bloquear ni arrastrar el borrado de la preferencia del usuario,
            // solo "olvidar" el default) pero resuelto del lado de EF -- la FK en la base
            // queda NO ACTION, sin el conflicto de múltiples rutas.
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.DefaultCompanyId)
                .OnDelete(DeleteBehavior.ClientSetNull);
        });

        modelBuilder.Entity<EmailSettings>(entity =>
        {
            entity.ToTable("email_settings", t => t.HasCheckConstraint("ck_email_settings_provider",
                $"provider in ('{EmailProviderType.GoogleWorkspace}', '{EmailProviderType.Microsoft365}')"));
            // 1:1 con Organization -- comparte PK, no tiene id propio.
            entity.HasKey(e => e.OrganizationId);
            entity.Property(e => e.Provider).HasMaxLength(30);
            entity.Property(e => e.SenderEmail).HasMaxLength(150);
            entity.Property(e => e.SenderDisplayName).HasMaxLength(150);
            entity.HasOne(e => e.Organization).WithOne().HasForeignKey<EmailSettings>(e => e.OrganizationId);
        });

        modelBuilder.Entity<UsageMetric>(entity =>
        {
            entity.ToTable("usage_metrics");
            entity.HasIndex(e => new { e.OrganizationId, e.Period, e.MetricName })
                .HasDatabaseName("ix_usage_metrics_period");
            entity.Property(e => e.MetricName).HasMaxLength(50);
            entity.Property(e => e.Value).HasPrecision(18, 2);
            entity.Property(e => e.Period).HasMaxLength(6).IsFixedLength();
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).IsRequired(false);
        });

        // ---------------------------------------------------------------------------
        // Núcleo heredado de PORTALWEB (menú/perfiles/acciones/auditoría) -- ver
        // docs/03-...md §3. GLOBAL a la plataforma (sin organization_id propio): el
        // scope real por organización lo dan UserMenuGroup/UserMenuProfile (usuario +
        // compañía, que sí resuelven a organizations). Portado de PortalSAP_v2
        // (GRUPO_MENU/MENU/PERFIL/ACCION/...), ver docs/01-...md §12 para la
        // equivalencia de nombres español->inglés.
        // ---------------------------------------------------------------------------
        modelBuilder.Entity<MenuGroup>(entity =>
        {
            entity.ToTable("menu_groups");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(300);
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            // Restrict -- Organization ya alcanza esta tabla en cascada vía OrganizationId
            // directo; un segundo camino en cascada vía CompanyId (Organization ->
            // Instance -> Company) es el mismo conflicto de rutas múltiples que
            // Company.Organization/UserMenuGroup.Company, ver ahí.
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Menu>(entity =>
        {
            entity.ToTable("menus");
            entity.HasIndex(e => new { e.OriginModule, e.Code }).IsUnique();
            entity.HasIndex(e => e.PagePath);
            entity.Property(e => e.OriginModule).HasMaxLength(100);
            entity.Property(e => e.Code).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Icon).HasMaxLength(50);
            entity.Property(e => e.PagePath).HasMaxLength(300);
            // Restrict -- SQL Server rechaza directamente un FK autorreferencial con
            // cascada (mismo error de fondo que Company.Organization, ver ahí).
            entity.HasOne(e => e.ParentMenu).WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentMenuId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Profile>(entity =>
        {
            entity.ToTable("profiles");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(300);
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
        });

        modelBuilder.Entity<OrganizationModuleVisibility>(entity =>
        {
            entity.ToTable("organization_module_visibilities");
            entity.HasKey(e => new { e.OrganizationId, e.ModuleId });
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Module).WithMany().HasForeignKey(e => e.ModuleId);
        });

        modelBuilder.Entity<OrganizationMenuOverride>(entity =>
        {
            entity.ToTable("organization_menu_overrides");
            entity.HasKey(e => new { e.OrganizationId, e.MenuId });
            entity.HasOne(e => e.Organization).WithMany().HasForeignKey(e => e.OrganizationId);
            entity.HasOne(e => e.Menu).WithMany().HasForeignKey(e => e.MenuId);
            entity.Property(e => e.CustomLabel).HasMaxLength(200);
        });

        modelBuilder.Entity<PermissionAction>(entity =>
        {
            entity.ToTable("actions");
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(30);
            entity.Property(e => e.Name).HasMaxLength(100);

            // Catálogo fijo -- mismos 6 valores que PortalSaas.Abstractions.Modelos.
            // PortalActions (duplicados acá a propósito, ver Entities/PermissionAction.cs
            // sobre por qué este proyecto no referencia Abstractions). Id explícito
            // porque HasData lo exige para poder diffear entre migraciones.
            entity.HasData(
                new { Id = 1L, Code = "VIEW", Name = "Ver" },
                new { Id = 2L, Code = "CREATE", Name = "Crear" },
                new { Id = 3L, Code = "EDIT", Name = "Editar" },
                new { Id = 4L, Code = "DELETE", Name = "Eliminar" },
                new { Id = 5L, Code = "APPROVE", Name = "Aprobar" },
                new { Id = 6L, Code = "EXPORT", Name = "Exportar" });
        });

        modelBuilder.Entity<ProfileAction>(entity =>
        {
            entity.ToTable("profile_actions");
            entity.HasKey(e => new { e.ProfileId, e.ActionId });
            entity.HasOne(e => e.Profile).WithMany(p => p.ProfileActions).HasForeignKey(e => e.ProfileId);
            entity.HasOne(e => e.Action).WithMany(a => a.ProfileActions).HasForeignKey(e => e.ActionId);
        });

        modelBuilder.Entity<MenuGroupItem>(entity =>
        {
            entity.ToTable("menu_group_items");
            entity.HasKey(e => new { e.MenuGroupId, e.MenuId });
            entity.HasOne(e => e.MenuGroup).WithMany(g => g.MenuGroupItems).HasForeignKey(e => e.MenuGroupId);
            entity.HasOne(e => e.Menu).WithMany(m => m.MenuGroupItems).HasForeignKey(e => e.MenuId);
            // Restrict, no Cascade -- un Profile no debería poder desaparecer en
            // silencio arrastrando el default de un grupo; borrar un Profile en uso
            // como default de grupo debe fallar explícito, no vaciar la columna solo.
            entity.HasOne(e => e.DefaultProfile).WithMany().HasForeignKey(e => e.DefaultProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserMenuGroup>(entity =>
        {
            entity.ToTable("user_menu_groups");
            entity.HasKey(e => new { e.UserId, e.MenuGroupId, e.CompanyId });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.MenuGroup).WithMany(g => g.UserMenuGroups).HasForeignKey(e => e.MenuGroupId);
            // Restrict -- Organization ya alcanza esta tabla en cascada vía User
            // (Organization -> User); un segundo camino en cascada vía Company
            // (Organization -> Instance -> Company) es el mismo conflicto de rutas
            // múltiples que Company.Organization, ver ahí.
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserMenuProfile>(entity =>
        {
            entity.ToTable("user_menu_profiles");
            entity.HasKey(e => new { e.UserId, e.MenuId, e.CompanyId });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Menu).WithMany(m => m.UserMenuProfiles).HasForeignKey(e => e.MenuId);
            entity.HasOne(e => e.Profile).WithMany(p => p.UserMenuProfiles).HasForeignKey(e => e.ProfileId);
            // Restrict -- mismo motivo que UserMenuGroup.Company.
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserHomeShortcut>(entity =>
        {
            entity.ToTable("user_home_shortcuts");
            entity.HasIndex(e => new { e.UserId, e.MenuId }).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Menu).WithMany().HasForeignKey(e => e.MenuId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Event).HasMaxLength(100);
            entity.Property(e => e.Detail).HasMaxLength(2000);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).IsRequired(false);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyId).IsRequired(false);
        });

        modelBuilder.Entity<IntegrationDefinition>(entity =>
        {
            entity.ToTable("integration_definitions", t =>
            {
                t.HasCheckConstraint("ck_integration_definitions_connector_type",
                    "connector_type in ('sap', 'rest', 'file', 'wmscloud', 'sql')");
                t.HasCheckConstraint("ck_integration_definitions_direction",
                    "direction in ('upload', 'download', 'both')");
            });
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Nombre).HasColumnName("name").HasMaxLength(200);
            entity.Property(e => e.ModuloOrigen).HasColumnName("source_module").HasMaxLength(100);
            entity.Property(e => e.EntidadNegocio).HasColumnName("business_entity").HasMaxLength(100);
            entity.Property(e => e.ConectorTipo).HasColumnName("connector_type")
                .HasConversion(v => ConectorTipoAProveedor(v), v => ConectorTipoDesdeProveedor(v))
                .HasMaxLength(20);
            entity.Property(e => e.ConectorConfigCifrado).HasColumnName("encrypted_connector_config");
            entity.Property(e => e.Direccion).HasColumnName("direction")
                .HasConversion(v => DireccionAProveedor(v), v => DireccionDesdeProveedor(v))
                .HasMaxLength(20);
            entity.Property(e => e.Activo).HasColumnName("is_active");
            entity.Property(e => e.ProgramacionCron).HasColumnName("cron_schedule").HasMaxLength(100);
            entity.Property(e => e.IntervaloMinutos).HasColumnName("run_interval_minutes");
            entity.Property(e => e.NextRunAt).HasColumnName("next_run_at");
            entity.Property(e => e.UltimaSincronizacionExitosa).HasColumnName("last_successful_sync_at");
            entity.HasIndex(e => new { e.CompanyId, e.Activo });
        });

        modelBuilder.Entity<IntegrationFieldMapping>(entity =>
        {
            entity.ToTable("integration_field_mappings");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.IntegrationDefinitionId).HasColumnName("integration_definition_id");
            entity.Property(e => e.CampoLocal).HasColumnName("local_field").HasMaxLength(100);
            entity.Property(e => e.CampoExterno).HasColumnName("external_field").HasMaxLength(100);
            entity.Property(e => e.Transformacion).HasColumnName("transformation").HasMaxLength(100);
            entity.Property(e => e.Obligatorio).HasColumnName("is_required");
            entity.HasOne(e => e.IntegrationDefinition)
                .WithMany(d => d.Mapeos)
                .HasForeignKey(e => e.IntegrationDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationRunLog>(entity =>
        {
            entity.ToTable("integration_run_logs", t =>
            {
                t.HasCheckConstraint("ck_integration_run_logs_status",
                    "status in ('success', 'error', 'partial')");
                t.HasCheckConstraint("ck_integration_run_logs_triggered_by",
                    "triggered_by in ('scheduled', 'manual')");
            });
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.IntegrationDefinitionId).HasColumnName("integration_definition_id");
            entity.Property(e => e.IniciadoEn).HasColumnName("started_at");
            entity.Property(e => e.FinalizadoEn).HasColumnName("finished_at");
            entity.Property(e => e.Resultado).HasColumnName("status")
                .HasConversion(v => ResultadoAProveedor(v), v => ResultadoDesdeProveedor(v))
                .HasMaxLength(20);
            entity.Property(e => e.RegistrosProcesados).HasColumnName("records_processed");
            entity.Property(e => e.RegistrosConError).HasColumnName("records_failed");
            entity.Property(e => e.DetalleError).HasColumnName("error_detail");
            entity.Property(e => e.DisparadoPor).HasColumnName("triggered_by")
                .HasConversion(v => DisparadoPorAProveedor(v), v => DisparadoPorDesdeProveedor(v))
                .HasMaxLength(20);
            entity.HasIndex(e => e.IntegrationDefinitionId);
        });

        modelBuilder.Entity<ApiClientCredential>(entity =>
        {
            entity.ToTable("api_client_credentials");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyId).HasColumnName("company_id");
            entity.Property(e => e.Nombre).HasColumnName("name").HasMaxLength(200);
            entity.Property(e => e.ApiKeyHash).HasColumnName("api_key_hash").HasMaxLength(100);
            entity.Property(e => e.Activo).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at");
            entity.HasIndex(e => e.ApiKeyHash).IsUnique();
            entity.HasIndex(e => new { e.CompanyId, e.Activo });
            entity.HasOne<Company>().WithMany().HasForeignKey(e => e.CompanyId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    // Conversores explícitos enum <-> string en inglés minúscula (docs/01-CONVENCION-
    // NOMBRES-BD.md §1) -- NO usar HasConversion<string>() a secas: el valor guardado
    // sería el nombre del miembro del enum en C# (español, PascalCase), lo que viola
    // la convención de la base. Métodos estáticos (no lambdas inline) porque
    // HasConversion espera Expression<Func<>> y una expresión switch no puede vivir
    // dentro de un árbol de expresión -- una referencia a método sí puede.
    private static string ConectorTipoAProveedor(IntegrationConectorTipo v) => v switch
    {
        IntegrationConectorTipo.Sap => "sap",
        IntegrationConectorTipo.Rest => "rest",
        IntegrationConectorTipo.Archivo => "file",
        IntegrationConectorTipo.WmsCloud => "wmscloud",
        IntegrationConectorTipo.Sql => "sql",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static IntegrationConectorTipo ConectorTipoDesdeProveedor(string v) => v switch
    {
        "sap" => IntegrationConectorTipo.Sap,
        "rest" => IntegrationConectorTipo.Rest,
        "file" => IntegrationConectorTipo.Archivo,
        "wmscloud" => IntegrationConectorTipo.WmsCloud,
        "sql" => IntegrationConectorTipo.Sql,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static string DireccionAProveedor(IntegrationDireccion v) => v switch
    {
        IntegrationDireccion.Subida => "upload",
        IntegrationDireccion.Bajada => "download",
        IntegrationDireccion.Ambas => "both",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static IntegrationDireccion DireccionDesdeProveedor(string v) => v switch
    {
        "upload" => IntegrationDireccion.Subida,
        "download" => IntegrationDireccion.Bajada,
        "both" => IntegrationDireccion.Ambas,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static string ResultadoAProveedor(IntegrationRunResultado v) => v switch
    {
        IntegrationRunResultado.Exito => "success",
        IntegrationRunResultado.Error => "error",
        IntegrationRunResultado.Parcial => "partial",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static IntegrationRunResultado ResultadoDesdeProveedor(string v) => v switch
    {
        "success" => IntegrationRunResultado.Exito,
        "error" => IntegrationRunResultado.Error,
        "partial" => IntegrationRunResultado.Parcial,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static string DisparadoPorAProveedor(IntegrationRunDisparadoPor v) => v switch
    {
        IntegrationRunDisparadoPor.Programado => "scheduled",
        IntegrationRunDisparadoPor.Manual => "manual",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static IntegrationRunDisparadoPor DisparadoPorDesdeProveedor(string v) => v switch
    {
        "scheduled" => IntegrationRunDisparadoPor.Programado,
        "manual" => IntegrationRunDisparadoPor.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };
}
