using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
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
    private readonly IEmailSenderService _emailSender;

    public IndexModel(PortalSaasDbContext db, ISecretoCifradoService secretoCifradoService, IEmailSenderService emailSender)
    {
        _db = db;
        _secretoCifradoService = secretoCifradoService;
        _emailSender = emailSender;
    }

    public Organization Organization { get; private set; } = null!;
    public bool EsNuevo { get; private set; }

    /// <summary>Resultado del último "Enviar correo de prueba" -- se muestra una sola vez, no se persiste.</summary>
    public string? TestResultMessage { get; private set; }
    public bool TestResultOk { get; private set; }

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
                    new GoogleWorkspaceConfigInput(Input.ClientEmail!.Trim(), NormalizePemLineBreaks(Input.PrivateKeyPem!.Trim()))));
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

    /// <summary>
    /// "Enviar correo de prueba" -- responde directamente la pregunta "¿está operativa
    /// esta configuración?" mandando un correo real con el proveedor guardado (Google
    /// Workspace o M365), en vez de solo validar formato de campos. Va al correo del
    /// platform admin que hizo click (ClaimTypes.Email, seteado en Admin/Login.cshtml.cs),
    /// no a un campo nuevo -- evita pedir un destinatario que el admin va a tipear su
    /// propio correo igual. No guarda nada (no hay Input.Recordar acá): solo ejercita
    /// IEmailSenderService.SendAsync contra la config YA persistida, así que primero hay
    /// que Guardar -- no tiene sentido probar credenciales que todavía no se grabaron.
    /// </summary>
    public async Task<IActionResult> OnPostTestAsync(Guid organizationId)
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

        if (existing is null)
        {
            TestResultOk = false;
            TestResultMessage = "Todavía no hay nada guardado -- completá los campos y hacé clic en \"Guardar\" antes de probar.";
            return Page();
        }

        var adminEmail = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            TestResultOk = false;
            TestResultMessage = "No se pudo determinar tu correo de administrador para enviar la prueba.";
            return Page();
        }

        try
        {
            await _emailSender.SendAsync(organizationId, new EmailMessage(
                adminEmail,
                "Correo de prueba -- Portal SaaS",
                $"<p>Este es un correo de prueba de la configuración de <strong>{organization.LegalName}</strong> ({existing.Provider}).</p><p>Si lo recibiste, la casilla está operativa.</p>"));

            TestResultOk = true;
            TestResultMessage = $"Correo de prueba enviado a {adminEmail} -- revisá la bandeja de entrada (y spam) para confirmar que llegó.";
        }
        catch (Exception ex)
        {
            // Mostrar el mensaje real, no un genérico -- es justo lo que hace falta
            // para diagnosticar "credencial vencida" vs "permiso faltante" vs "casilla
            // mal escrita" sin tener que ir a mirar logs del servidor.
            TestResultOk = false;
            TestResultMessage = $"No se pudo enviar: {ex.Message}";
        }

        return Page();
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

    /// <summary>
    /// El JSON de cuenta de servicio que Google Cloud da para descargar escapa los saltos
    /// de línea de "private_key" como "\n" LITERAL (dos caracteres, backslash+n) --
    /// formato correcto para un valor de JSON, pero no es un PEM válido tal cual: el
    /// parser de RSA.ImportFromPem (ver GoogleServiceAccountJwtBuilder) exige saltos de
    /// línea reales entre "-----BEGIN PRIVATE KEY-----", el cuerpo en base64 y
    /// "-----END PRIVATE KEY-----". Un usuario que copia el campo "private_key" directo
    /// del .json (en vez de extraer el PEM ya con saltos reales) pega justamente esa
    /// secuencia -- bug real, 2026-08-08: "No supported key formats were found" al
    /// intentar enviar. Normalizar acá, antes de cifrar y persistir, para que ambas
    /// formas de pegar la clave funcionen igual.
    /// </summary>
    private static string NormalizePemLineBreaks(string pem) =>
        pem.Replace("\\r\\n", "\n").Replace("\\n", "\n");
}
