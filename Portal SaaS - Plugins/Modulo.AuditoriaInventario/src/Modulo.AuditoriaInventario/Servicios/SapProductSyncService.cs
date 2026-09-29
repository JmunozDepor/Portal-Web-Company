using Microsoft.EntityFrameworkCore;
using Modulo.AuditoriaInventario.Data;
using Modulo.AuditoriaInventario.Models;
using PortalSaas.Abstractions.Contratos;

namespace Modulo.AuditoriaInventario.Servicios;

/// <summary>
/// Fila de OITM traída para la carga masiva del maestro -- non-positional, mismo
/// motivo que ItemDto/ItemMasterDetailDto (mapeo por reflection de
/// IHanaService.QueryAsync).
/// </summary>
public sealed record SapProductMasterRow
{
    public string ItemCode { get; init; } = null!;
    public string ItemName { get; init; } = null!;
    public string? CodeBars { get; init; }
}

public sealed record SapProductSyncResult(int Creados, int Actualizados, int SinBarcode, int TotalSap);

public interface ISapProductSyncService
{
    Task<SapProductSyncResult> SincronizarAsync(Guid companyId, CancellationToken ct = default);
}

/// <summary>
/// Carga masiva del maestro de productos propio de este módulo (Products) desde el
/// OITM del SAP B1 de la compañía activa, vía IHanaService (PortalSaas.Abstractions
/// -- contrato de plataforma, no código de Modulo.Inventario; usarlo respeta la
/// autocontención del módulo, ver AuditoriaInventarioModule). Paginado porque OITM
/// puede tener cientos de miles de filas (mismo comentario que IItemCatalogService).
/// Marca/Línea quedan sin mapear a propósito: no existe hoy ningún campo de SAP
/// mapeado en el repo para eso (ni UDF identificado) -- decisión explícita del
/// dueño del proyecto, 2026-09-28.
/// </summary>
public sealed class SapProductSyncService : ISapProductSyncService
{
    private const int TamanoLote = 500;

    private readonly IHanaService _hana;
    private readonly AuditoriaInventarioDbContext _db;

    public SapProductSyncService(IHanaService hana, AuditoriaInventarioDbContext db)
    {
        _hana = hana;
        _db = db;
    }

    public async Task<SapProductSyncResult> SincronizarAsync(Guid companyId, CancellationToken ct = default)
    {
        var creados = 0;
        var actualizados = 0;
        var sinBarcode = 0;
        var totalSap = 0;
        var offset = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            const string sql = """
                SELECT T0."ItemCode" AS "ItemCode", T0."ItemName" AS "ItemName", T0."CodeBars" AS "CodeBars"
                FROM "OITM" T0
                WHERE T0."validFor" = 'Y'
                ORDER BY T0."ItemCode"
                LIMIT :limit OFFSET :offset
                """;

            var lote = await _hana.QueryAsync<SapProductMasterRow>(sql, new { limit = TamanoLote, offset }, ct);
            if (lote.Count == 0)
            {
                break;
            }

            totalSap += lote.Count;
            offset += TamanoLote;

            // Sin CodeBars el artículo no sirve para el escaneo de la PWA (Product.Barcode
            // es obligatorio) -- se cuenta pero no se carga.
            var sinCodigo = lote.Count(a => string.IsNullOrWhiteSpace(a.CodeBars));
            sinBarcode += sinCodigo;

            // Dedupe DENTRO del lote antes de tocar la base: si dos artículos de SAP
            // comparten CodeBars (dato sucio, no debería pasar pero SAP no lo garantiza),
            // intentar insertar ambos en el mismo SaveChanges revienta el índice único
            // (company_id, barcode). Último gana -- mismo criterio simple que un upsert.
            var porBarcode = lote
                .Where(a => !string.IsNullOrWhiteSpace(a.CodeBars))
                .GroupBy(a => a.CodeBars!)
                .Select(g => g.Last())
                .ToList();

            if (porBarcode.Count == 0)
            {
                continue;
            }

            var barcodesDelLote = porBarcode.Select(a => a.CodeBars!).ToList();
            var existentes = await _db.Products
                .Where(p => p.CompanyId == companyId && barcodesDelLote.Contains(p.Barcode))
                .ToDictionaryAsync(p => p.Barcode, ct);

            foreach (var articulo in porBarcode)
            {
                var barcode = articulo.CodeBars!;
                if (barcode.Length > 50 || articulo.ItemCode.Length > 50)
                {
                    // No entra en el esquema (columnas varchar(50)) -- se cuenta como
                    // "sin barcode cargable" en vez de reventar el lote entero.
                    sinBarcode++;
                    continue;
                }

                // ItemName puede venir NULL en datos reales de SAP (no todo articulo tiene
                // nombre cargado) -- el tipo no-nullable del DTO solo describe el caso
                // comun, RowReflectionMapper asigna null igual si la columna lo es.
                var nombreSap = articulo.ItemName ?? string.Empty;
                var descripcion = nombreSap.Length > 300 ? nombreSap[..300] : nombreSap;

                if (existentes.TryGetValue(barcode, out var existente))
                {
                    existente.ProductCode = articulo.ItemCode;
                    existente.Description = descripcion;
                    existente.SapMaterialCode = articulo.ItemCode;
                    existente.Source = "INTEGRACION";
                    existente.LoadedAt = DateTimeOffset.UtcNow;
                    actualizados++;
                }
                else
                {
                    _db.Products.Add(new Product
                    {
                        CompanyId = companyId,
                        Barcode = barcode,
                        ProductCode = articulo.ItemCode,
                        Description = descripcion,
                        SapMaterialCode = articulo.ItemCode,
                        Source = "INTEGRACION",
                    });
                    creados++;
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        return new SapProductSyncResult(creados, actualizados, sinBarcode, totalSap);
    }
}
