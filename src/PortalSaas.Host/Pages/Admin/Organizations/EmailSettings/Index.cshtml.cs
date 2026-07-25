using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.EmailSettings;

/// <summary>
/// Alta/edición de `email_settings` -- relación 1:1 con Organization (no hay
/// historial como Subscriptions/Licenses), así que es una sola página que hace
/// upsert en vez de Create/Edit separadas. Mismo patrón write-only que
/// Instances/Companies para el secreto: los campos del proveedor nunca se
/// vuelven a mostrar una vez guardados -- dejarlos en blanco al editar no cambia
/// la configuración existente. Si se completa CUALQUIER campo del proveedor
/// seleccionado, se exigen TODOS los de ese proveedor y se regenera el JSON
/// cifrado entero (no se puede actualizar un campo suelto sin poder descifrar
/// el resto).
/// </summary>
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

    public Organization Organization { get; private set; } = null!;
    public bool EsNuevo { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IEnumerable<string> Providers => EmailProviderType.All;

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;

        var existing = await _db.EmailSettings.FindAsync(organizationId);
        EsNuevo = existing is null;

        if (existing is not null)
        {
            Input = new InputModel
            {
                Provider = existing.Provider,
                SenderEmail = existing.SenderEmail,
                SenderDisplayName = existing.SenderDisplayName,
                IsActive = existing.IsActive,
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;

        var existing = await _db.EmailSettings.FindAsync(organizationId);
        EsNuevo = existing is null;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Cambiar de proveedor exige cargar de nuevo todos los campos del proveedor
        // nuevo -- no hay forma de reusar la config cifrada anterior si el proveedor
        // cambió (es un JSON con forma distinta).
        var cambioDeProveedor = existing is not null && existing.Provider != Input.Provider;

        string? encryptedConfig = null;

        if (Input.Provider == EmailProviderType.GoogleWorkspace)
        {
            var completoAlgunCampo = !string.IsNullOrWhiteSpace(Input.ClientEmail) || !string.IsNullOrWhiteSpace(Input.PrivateKeyPem);
            if (EsNuevo || cambioDeProveedor || completoAlgunCampo)
            {
                if (string.IsNullOrWhiteSpace(Input.ClientEmail))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ClientEmail)}", "Ingresa el correo de la cuenta de servicio.");
                }

                if (string.IsNullOrWhiteSpace(Input.PrivateKeyPem))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.PrivateKeyPem)}", "Ingresa la clave privada (PEM) de la cuenta de servicio.");
                }

                if (!ModelState.IsValid)
                {
                    return Page();
                }

                encryptedConfig = _secretoCifradoService.Encrypt(JsonSerializer.Serialize(
                    new GoogleWorkspaceConfigInput(Input.ClientEmail!.Trim(), Input.PrivateKeyPem!.Trim())));
            }
        }
        else if (Input.Provider == EmailProviderType.Microsoft365)
        {
            var completoAlgunCampo = !string.IsNullOrWhiteSpace(Input.TenantId) || !string.IsNullOrWhiteSpace(Input.ClientId) || !string.IsNullOrWhiteSpace(Input.ClientSecret);
            if (EsNuevo || cambioDeProveedor || completoAlgunCampo)
            {
                if (string.IsNullOrWhiteSpace(Input.TenantId))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.TenantId)}", "Ingresa el Tenant Id.");
                }

                if (string.IsNullOrWhiteSpace(Input.ClientId))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ClientId)}", "Ingresa el Client Id.");
                }

                if (string.IsNullOrWhiteSpace(Input.ClientSecret))
                {
                    ModelState.AddModelError($"{nameof(Input)}.{nameof(Input.ClientSecret)}", "Ingresa el Client Secret.");
                }

                if (!ModelState.IsValid)
                {
                    return Page();
                }

                encryptedConfig = _secretoCifradoService.Encrypt(JsonSerializer.Serialize(
                    new Microsoft365ConfigInput(Input.TenantId!.Trim(), Input.ClientId!.Trim(), Input.ClientSecret!.Trim())));
            }
        }

        var senderEmail = Input.SenderEmail.Trim().ToLowerInvariant();
        var senderDisplayName = string.IsNullOrWhiteSpace(Input.SenderDisplayName) ? null : Input.SenderDisplayName.Trim();

        if (existing is null)
        {
            _db.EmailSettings.Add(new global::PortalSaas.Data.Entities.EmailSettings
            {
                OrganizationId = organizationId,
                Provider = Input.Provider,
                SenderEmail = senderEmail,
                SenderDisplayName = senderDisplayName,
                EncryptedProviderConfig = encryptedConfig!,
                IsActive = Input.IsActive,
            });
        }
        else
        {
            existing.Provider = Input.Provider;
            existing.SenderEmail = senderEmail;
            existing.SenderDisplayName = senderDisplayName;
            existing.IsActive = Input.IsActive;

            if (encryptedConfig is not null)
            {
                existing.EncryptedProviderConfig = encryptedConfig;
            }
        }

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/EmailSettings/Index", new { organizationId });
    }

    public sealed class InputModel
    {
        [Required]
        [Display(Name = "Proveedor")]
        public string Provider { get; set; } = EmailProviderType.GoogleWorkspace;

        [Required(ErrorMessage = "Ingresa la casilla de envío.")]
        [EmailAddress(ErrorMessage = "Correo inválido.")]
        [Display(Name = "Casilla de envío")]
        public string SenderEmail { get; set; } = string.Empty;

        [Display(Name = "Nombre visible del remitente")]
        public string? SenderDisplayName { get; set; }

        [Display(Name = "Activo")]
        public bool IsActive { get; set; } = true;

        // --- Google Workspace (write-only, nunca se vuelven a mostrar) ---
        [Display(Name = "Correo de la cuenta de servicio")]
        public string? ClientEmail { get; set; }

        [DataType(DataType.MultilineText)]
        [Display(Name = "Clave privada (PEM)")]
        public string? PrivateKeyPem { get; set; }

        // --- Microsoft 365 (write-only, nunca se vuelven a mostrar) ---
        [Display(Name = "Tenant Id")]
        public string? TenantId { get; set; }

        [Display(Name = "Client Id")]
        public string? ClientId { get; set; }

        [DataType(DataType.Password)]
        [Display(Name = "Client Secret")]
        public string? ClientSecret { get; set; }
    }

    /// <summary>
    /// Espejo local de GoogleWorkspaceProviderConfig (PortalSaas.Core.Correo, internal
    /// a ese ensamblado) -- mismos nombres camelCase exactos que
    /// EmailSenderService/GoogleWorkspaceEmailSender esperan al descifrar.
    /// </summary>
    private sealed record GoogleWorkspaceConfigInput(
        [property: JsonPropertyName("clientEmail")] string ClientEmail,
        [property: JsonPropertyName("privateKeyPem")] string PrivateKeyPem);

    /// <summary>Espejo local de Microsoft365ProviderConfig, ver GoogleWorkspaceConfigInput.</summary>
    private sealed record Microsoft365ConfigInput(
        [property: JsonPropertyName("tenantId")] string TenantId,
        [property: JsonPropertyName("clientId")] string ClientId,
        [property: JsonPropertyName("clientSecret")] string ClientSecret);
}
