using Microsoft.AspNetCore.Mvc;
using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Contratos;
using Modulo.Despachos.Servicios;

namespace Modulo.Despachos.Pages.Despachos.Configuracion.Feriados;

/// <summary>Mantenedor de feriados. Menú "feriados" (permisos: Despachos.feriados).</summary>
public sealed class IndexModel : MaestroPageModelBase
{
    private readonly IMasterDataService _data;

    public IndexModel(IMasterDataService data, ICurrentUserContext currentUser) : base(currentUser)
    {
        _data = data;
    }

    protected override string MenuCode => "feriados";

    public IReadOnlyList<Holiday> Items { get; private set; } = Array.Empty<Holiday>();

    [BindProperty]
    public FeriadoInput Input { get; set; } = new();

    public Task<IActionResult> OnGetAsync() => LoadAsync(async () =>
    {
        Items = await _data.ListHolidaysAsync();
    });

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync(Input.Id, async () =>
        await _data.SaveHolidayAsync(new Holiday
            {
                Id = Input.Id,
                Date = ParseDate(Input.Date, "La fecha") ?? throw new InvalidOperationException("La fecha es obligatoria."),
                Name = Input.Name ?? "",
            }),
        Input.Id == 0 ? "Feriado creado." : "Feriado actualizado.");

    public Task<IActionResult> OnPostDeleteAsync(long id) => DeleteCoreAsync(() => _data.DeleteHolidayAsync(id), "Feriado eliminado.");

    public sealed class FeriadoInput
    {
        public long Id { get; set; }
        public string? Date { get; set; }
        public string? Name { get; set; }
    }
}
