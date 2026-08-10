using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Companies.ExternalConnections;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class IndexModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ISecretoCifradoService _secretoCifradoService;

    public IndexModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
    }

    public Company Company { get; private set; } = null!;
    public List<ModuleExternalConnection> Connections { get; private set; } = [];

    public long? TestedConnectionId { get; private set; }
    public bool? TestSuccess { get; private set; }
    public string? TestError { get; private set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid companyId)
    {
        var company = await _db.Companies.FindAsync(companyId);
        if (company is null)
        {
            return NotFound();
        }

        Company = company;
        Connections = await _db.ModuleExternalConnections
            .Where(c => c.CompanyId == companyId)
            .OrderBy(c => c.ModuleCode)
            .ToListAsync();

        return Page();
    }

    /// <summary>
    /// Prueba la conexión REAL de una fila puntual, sin pasar por
    /// IExternalDatabaseConnectionService.ResolveConnectionAsync a propósito -- ese método
    /// filtra por IsActive y por (moduleCode, companyId), no por Id, así que no serviría
    /// para probar una fila concreta que el admin todavía no activó. Mismo criterio que
    /// ISapConnectionTestService (Companies/Index): el mensaje de error real SÍ se muestra
    /// tal cual, esta pantalla es admin-only.
    /// </summary>
    public async Task<IActionResult> OnPostTestConnectionAsync(Guid companyId, long id)
    {
        var getResult = await OnGetAsync(companyId);
        if (getResult is not PageResult)
        {
            return getResult;
        }

        var connection = Connections.FirstOrDefault(c => c.Id == id);
        if (connection is null)
        {
            return NotFound();
        }

        TestedConnectionId = id;

        var password = _secretoCifradoService.Decrypt(connection.TechnicalSecretKey);

        try
        {
            switch (connection.EngineType)
            {
                case ModuleExternalConnectionEngineType.Postgres:
                    {
                        var connectionString = new NpgsqlConnectionStringBuilder
                        {
                            Host = connection.Host,
                            Port = connection.Port,
                            Database = connection.DatabaseName,
                            Username = connection.TechnicalUsername,
                            Password = password,
                            Timeout = 5,
                        }.ConnectionString;
                        await using var npgsqlConnection = new NpgsqlConnection(connectionString);
                        await npgsqlConnection.OpenAsync();
                        break;
                    }
                case ModuleExternalConnectionEngineType.SqlServer:
                    {
                        var connectionString = new SqlConnectionStringBuilder
                        {
                            DataSource = $"{connection.Host},{connection.Port}",
                            InitialCatalog = connection.DatabaseName,
                            UserID = connection.TechnicalUsername,
                            Password = password,
                            TrustServerCertificate = true,
                            ConnectTimeout = 5,
                        }.ConnectionString;
                        await using var sqlConnection = new SqlConnection(connectionString);
                        await sqlConnection.OpenAsync();
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Motor de base de datos externa no soportado: '{connection.EngineType}'.");
            }

            TestSuccess = true;
        }
        catch (Exception ex)
        {
            TestSuccess = false;
            TestError = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid companyId, long id)
    {
        var connection = await _db.ModuleExternalConnections.FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId);
        if (connection is not null)
        {
            _db.ModuleExternalConnections.Remove(connection);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { companyId });
    }
}
