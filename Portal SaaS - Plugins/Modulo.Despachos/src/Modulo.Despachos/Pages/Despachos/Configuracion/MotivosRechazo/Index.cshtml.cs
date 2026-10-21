using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.MotivosRechazo;

/// <summary>Mantenedor de motivos de rechazo. Menú "motivos-rechazo" (permisos: Despachos.motivos-rechazo).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "motivos-rechazo";

    public IReadOnlyList<RejectionReason> Items { get; private set; } = Array.Empty<RejectionReason>();

    [BindProperty]
    public MotivoRechazoInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListRejectionReasonsAsync(includeInactive: true);
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveRejectionReasonAsync(new RejectionReason
            {
                Id = Input.Id,
                Code = Input.Code ?? "",
                Name = Input.Name ?? "",
                AppliesTo = Input.AppliesTo ?? "",
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Motivo creado." : "Motivo actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteRejectionReasonAsync(id), "Motivo eliminado.");

    public sealed class MotivoRechazoInput
    {
        public long Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? AppliesTo { get; set; }
        public bool IsActive { get; set; }
    }
}
