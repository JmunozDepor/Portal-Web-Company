using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using PortalSaas.Abstractions.Modelos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.DestinatariosAvisos;

/// <summary>Mantenedor de destinatarios de avisos. Menú "destinatarios-avisos" (permisos: Despachos.destinatarios-avisos).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;
    private readonly ITenantUserAdminService _users;

    public IndexModel(IMasterDataService data, ITenantUserAdminService users, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
        _users = users;
    }

    protected override string MenuCode => "destinatarios-avisos";

    public IReadOnlyList<NotificationRecipient> Items { get; private set; } = Array.Empty<NotificationRecipient>();
    public IReadOnlyList<TenantUserDto> Users { get; private set; } = Array.Empty<TenantUserDto>();

    public string UserName(Guid id) => Users.FirstOrDefault(u => u.Id == id)?.Username ?? "(usuario no disponible)";
    public string UserEmail(Guid id) => Users.FirstOrDefault(u => u.Id == id)?.Email ?? "—";

    [BindProperty]
    public DestinatarioInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListNotificationRecipientsAsync(includeInactive: true);
        Users = (await _users.ListAsync()).OrderBy(u => u.Username).ToList();
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveNotificationRecipientAsync(new NotificationRecipient
            {
                Id = Input.Id,
                UserId = Input.UserId,
                IsActive = Input.IsActive,
            }),
        Input.Id == 0 ? "Destinatario creado." : "Destinatario actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteNotificationRecipientAsync(id), "Destinatario eliminado.");

    public sealed class DestinatarioInput
    {
        public long Id { get; set; }
        public Guid UserId { get; set; }
        public bool IsActive { get; set; }
    }
}
