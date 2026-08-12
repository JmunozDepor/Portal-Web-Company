using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Subscriptions;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class EditModel : PageModel
{
    private readonly PortalSaasDbContext _db;
    private readonly ICacheService _cache;

    public EditModel(PortalSaasDbContext db, ICacheService cache)
    {
        _db = db;
        _cache = cache;
    }

    public Organization Organization { get; private set; } = null!;
    public string PlanLabel { get; private set; } = string.Empty;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .Include(s => s.Organization)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (subscription is null)
        {
            return NotFound();
        }

        Organization = subscription.Organization;
        PlanLabel = $"{subscription.Plan.Name} ({subscription.Plan.Code})";
        Input = new InputModel
        {
            Id = subscription.Id,
            OrganizationId = subscription.OrganizationId,
            Status = subscription.Status,
            EndedAt = subscription.EndedAt?.ToString("yyyy-MM-dd"),
            PaymentProvider = subscription.PaymentProvider,
            ExternalPaymentReference = subscription.ExternalPaymentReference,
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var subscription = await _db.Subscriptions
            .Include(s => s.Plan)
            .Include(s => s.Organization)
            .FirstOrDefaultAsync(s => s.Id == Input.Id);

        if (subscription is null)
        {
            return NotFound();
        }

        Organization = subscription.Organization;
        PlanLabel = $"{subscription.Plan.Name} ({subscription.Plan.Code})";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        subscription.Status = Input.Status;
        // DateOnly + offset UTC explícito -- DateTimeOffset.Parse toma el offset de la
        // zona horaria del servidor (ej. Chile, -04:00), y Postgres timestamptz vía
        // Npgsql solo acepta escribir con offset 0 (ver docs/02-...md §7).
        subscription.EndedAt = string.IsNullOrWhiteSpace(Input.EndedAt)
            ? null
            : new DateTimeOffset(DateOnly.Parse(Input.EndedAt).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        subscription.PaymentProvider = string.IsNullOrWhiteSpace(Input.PaymentProvider) ? null : Input.PaymentProvider.Trim();
        subscription.ExternalPaymentReference = string.IsNullOrWhiteSpace(Input.ExternalPaymentReference) ? null : Input.ExternalPaymentReference.Trim();

        await _db.SaveChangesAsync();

        // Cambia el estado/vigencia de la suscripción de esta organización -- ver
        // ICacheService/Task 6.
        _cache.RemoveByPrefix($"modulos-contratados:{subscription.OrganizationId}");

        return RedirectToPage("/Admin/Organizations/Subscriptions/Index", new { organizationId = Input.OrganizationId });
    }

    public sealed class InputModel
    {
        public long Id { get; set; }
        public Guid OrganizationId { get; set; }

        [Required]
        [Display(Name = "Estado")]
        public string Status { get; set; } = SubscriptionStatus.Trial;

        [Display(Name = "Fecha de fin")]
        public string? EndedAt { get; set; }

        [Display(Name = "Proveedor de pago")]
        public string? PaymentProvider { get; set; }

        [Display(Name = "Referencia de pago externa")]
        public string? ExternalPaymentReference { get; set; }
    }
}
