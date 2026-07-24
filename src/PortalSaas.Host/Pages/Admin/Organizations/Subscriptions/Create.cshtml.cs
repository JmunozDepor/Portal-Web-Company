using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PortalSaas.Data;
using PortalSaas.Data.Entities;

namespace PortalSaas.Host.Pages.Admin.Organizations.Subscriptions;

[Authorize(AuthenticationSchemes = "PlatformAdmin")]
public class CreateModel : PageModel
{
    private readonly PortalSaasDbContext _db;

    public CreateModel(PortalSaasDbContext db)
    {
        _db = db;
    }

    public Organization Organization { get; private set; } = null!;
    public List<Plan> ActivePlans { get; private set; } = [];

    public IEnumerable<SelectListItem> PlanOptions =>
        ActivePlans.Select(p => new SelectListItem($"{p.Name} ({p.Code})", p.Id.ToString()));

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid organizationId)
    {
        var organization = await _db.Organizations.FindAsync(organizationId);
        if (organization is null)
        {
            return NotFound();
        }

        Organization = organization;
        ActivePlans = await _db.Plans.Where(p => p.IsActive).OrderBy(p => p.Code).ToListAsync();

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
        ActivePlans = await _db.Plans.Where(p => p.IsActive).OrderBy(p => p.Code).ToListAsync();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        _db.Subscriptions.Add(new Subscription
        {
            OrganizationId = organizationId,
            PlanId = Input.PlanId,
            Status = Input.Status,
            PaymentProvider = string.IsNullOrWhiteSpace(Input.PaymentProvider) ? null : Input.PaymentProvider.Trim(),
            ExternalPaymentReference = string.IsNullOrWhiteSpace(Input.ExternalPaymentReference) ? null : Input.ExternalPaymentReference.Trim(),
        });

        await _db.SaveChangesAsync();

        return RedirectToPage("/Admin/Organizations/Subscriptions/Index", new { organizationId });
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Elige un plan.")]
        [Display(Name = "Plan")]
        public long PlanId { get; set; }

        [Required]
        [Display(Name = "Estado")]
        public string Status { get; set; } = SubscriptionStatus.Trial;

        [Display(Name = "Proveedor de pago")]
        public string? PaymentProvider { get; set; }

        [Display(Name = "Referencia de pago externa")]
        public string? ExternalPaymentReference { get; set; }
    }
}
