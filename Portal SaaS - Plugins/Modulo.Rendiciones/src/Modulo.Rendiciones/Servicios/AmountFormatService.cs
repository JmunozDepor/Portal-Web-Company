using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Modulo.Rendiciones.Data;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Scoped -- memoiza la preferencia en <see cref="_showDecimals"/> para no pegarle a la
/// base una vez por cada monto de la pantalla (un informe con 10 líneas llama a
/// FormatAsync varias veces por línea).
/// </summary>
public sealed class AmountFormatService : IAmountFormatService
{
    private readonly RendicionesDbContext _db;
    private readonly ICurrentCompanyAccessor _currentCompany;
    private bool? _showDecimals;

    public AmountFormatService(RendicionesDbContext db, ICurrentCompanyAccessor currentCompany)
    {
        _db = db;
        _currentCompany = currentCompany;
    }

    public async Task<bool> ShowDecimalsAsync(CancellationToken ct = default)
    {
        if (_showDecimals is { } cached)
            return cached;

        // Sin fila de settings todavía (compañía que nunca entró a guardar nada) =>
        // default "sin decimales", mismo criterio que RendicionesSettings.ShowAmountDecimals.
        var value = await _db.RendicionesSettings.AsNoTracking()
            .Where(x => x.CompanyId == _currentCompany.CompanyId)
            .Select(x => (bool?)x.ShowAmountDecimals)
            .FirstOrDefaultAsync(ct) ?? false;

        _showDecimals = value;
        return value;
    }

    // es-CL: "." como separador de miles y "," como decimal (ej. "297.024,00") -- es
    // el formato que ya se usaba antes en todas las pantallas, InvariantCulture da
    // "297,024.00" (estilo US) y hubiera sido un cambio visual no pedido.
    private static readonly CultureInfo Chile = CultureInfo.GetCultureInfo("es-CL");

    public async Task<string> FormatAsync(decimal amount, CancellationToken ct = default)
    {
        var showDecimals = await ShowDecimalsAsync(ct);
        return amount.ToString(showDecimals ? "N2" : "N0", Chile);
    }
}
