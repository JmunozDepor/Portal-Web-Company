using Microsoft.EntityFrameworkCore;
using Modulo.Despachos.Data;
using Modulo.Despachos.Models;

namespace Modulo.Despachos.Servicios;

/// <summary>
/// Siembra el catálogo oficial de comunas (código CUT) de una compañía desde el recurso
/// embebido Sql/Seeds/comunas.csv (cut_code;name;region_code;region_name). Idempotente:
/// solo inserta los códigos CUT que la compañía todavía no tiene.
/// </summary>
public static class CommuneSeeder
{
    private const string ResourceSuffix = "comunas.csv";

    public static async Task EnsureSeededAsync(DespachosDbContext db, Guid companyId, CancellationToken ct = default)
    {
        var existing = (await db.Communes
                .Where(c => c.CompanyId == companyId)
                .Select(c => c.CutCode)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        var added = 0;
        foreach (var row in ReadRows())
        {
            if (existing.Contains(row.CutCode)) continue;
            db.Communes.Add(new Commune
            {
                CompanyId = companyId,
                CutCode = row.CutCode,
                Name = row.Name,
                RegionCode = row.RegionCode,
                RegionName = row.RegionName,
                CreatedAt = now,
            });
            added++;
        }

        if (added > 0) await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<(string CutCode, string Name, string RegionCode, string RegionName)> ReadRows()
    {
        var assembly = typeof(CommuneSeeder).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        reader.ReadLine(); // encabezado
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var p = line.Split(';');
            if (p.Length < 4) throw new InvalidDataException($"Fila inválida en {ResourceSuffix}: '{line}'.");
            yield return (p[0].Trim(), p[1].Trim(), p[2].Trim(), p[3].Trim());
        }
    }
}
