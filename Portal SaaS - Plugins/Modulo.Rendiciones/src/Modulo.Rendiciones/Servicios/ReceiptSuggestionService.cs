using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Modulo.Rendiciones.Models;

namespace Modulo.Rendiciones.Servicios;

public sealed class ReceiptSuggestionService : IReceiptSuggestionService
{
    /// <summary>Confirmaciones de la misma categoría para un proveedor antes de PRE-seleccionarla (vs. solo sugerirla).</summary>
    private const int PreselectThreshold = 2;

    private readonly ISupplierHintService _hints;
    private readonly IExpenseTypeService _expenseTypes;
    private readonly IDocumentTypeService _documentTypes;
    private readonly ILogger<ReceiptSuggestionService> _logger;

    public ReceiptSuggestionService(ISupplierHintService hints, IExpenseTypeService expenseTypes,
        IDocumentTypeService documentTypes, ILogger<ReceiptSuggestionService> logger)
    {
        _hints = hints;
        _expenseTypes = expenseTypes;
        _documentTypes = documentTypes;
        _logger = logger;
    }

    public async Task<ReceiptProposal> BuildAsync(Guid companyId, ExtractedReceiptDto ocr, CancellationToken ct = default)
    {
        if (ocr.Error is not null)
            return ReceiptProposal.FromError(ocr.Error);

        var usoMemoria = false;

        // --- Proveedor ---
        var rut = RutChileno.NormalizeOrNull(ocr.SupplierTaxId);
        var hint = rut is null ? null : await SafeGetHintAsync(companyId, rut, ct);

        var supplierName = string.IsNullOrWhiteSpace(ocr.SupplierName) ? null : ocr.SupplierName.Trim();
        if (supplierName is null && !string.IsNullOrWhiteSpace(hint?.SupplierName))
        {
            supplierName = hint!.SupplierName;
            usoMemoria = true;
        }

        // --- Tipo de documento ---
        long? documentTypeId = null;
        if (ocr.SiiDocumentCode is { } sii)
        {
            var dt = await _documentTypes.FindBySiiCodeAsync(companyId, sii, ct);
            documentTypeId = dt?.Id;
        }
        if (documentTypeId is null && hint?.DefaultDocumentTypeId is { } hd)
        {
            documentTypeId = hd;
            usoMemoria = true;
        }

        // --- Desglose neto / IVA / exento ---
        var amount = ocr.Amount;
        var tax = ocr.TaxAmount;
        var net = ocr.NetAmount;
        var exempt = ocr.ExemptAmount;

        var esAfecto = ChileanDocumentKind.IsAfecto(ocr.SiiDocumentCode);
        if (esAfecto && amount is { } total && total > 0m && tax is null && net is null)
        {
            // Boleta afecta sin desglose impreso: IVA implícito 19%.
            var neto = Math.Round(total / 1.19m, 0, MidpointRounding.AwayFromZero);
            net = neto;
            tax = total - neto;
        }

        // --- Categoría ---
        var tiposGasto = await _expenseTypes.ListActiveAsync(companyId, ct);
        long? preseleccion = null;
        long? sugerida = null;

        if (hint?.DefaultExpenseTypeId is { } catMemoria
            && tiposGasto.Any(t => t.Id == catMemoria))
        {
            if (hint.TimesCategoryConfirmed >= PreselectThreshold)
            {
                preseleccion = catMemoria;
                usoMemoria = true;
            }
            else
            {
                sugerida = catMemoria;
            }
        }

        if (preseleccion is null && sugerida is null)
            sugerida = MatchCategoryByName(ocr.SuggestedCategoryText, tiposGasto);

        var captureSource = usoMemoria ? "mixed" : "document";

        return new ReceiptProposal(
            Amount: amount,
            TaxAmount: tax,
            NetAmount: net,
            ExemptAmount: exempt,
            Date: ocr.Date,
            TransactionTime: ocr.TransactionTime,
            DocumentNumber: ocr.DocumentNumber,
            SupplierTaxId: rut ?? ocr.SupplierTaxId,
            SupplierName: supplierName,
            DocumentTypeId: documentTypeId,
            ExpenseTypeId: preseleccion,
            SuggestedExpenseTypeId: sugerida,
            CaptureSource: captureSource,
            Error: null);
    }

    private async Task<SupplierHint?> SafeGetHintAsync(Guid companyId, string rut, CancellationToken ct)
    {
        try
        {
            return await _hints.GetAsync(companyId, rut, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo consultar la memoria del proveedor {Rut}.", rut);
            return null;
        }
    }

    /// <summary>Empareja el texto de categoría sugerido por el modelo con un ExpenseType real (comparación laxa: sin acentos, sin distinción de mayúsculas, por contención).</summary>
    private static long? MatchCategoryByName(string? sugerido, IReadOnlyList<ExpenseType> tipos)
    {
        if (string.IsNullOrWhiteSpace(sugerido))
            return null;

        var s = Fold(sugerido);
        var exacta = tipos.FirstOrDefault(t => Fold(t.Name) == s);
        if (exacta is not null)
            return exacta.Id;

        var contiene = tipos.FirstOrDefault(t => Fold(t.Name).Contains(s) || s.Contains(Fold(t.Name)));
        return contiene?.Id;
    }

    private static string Fold(string value)
    {
        var norm = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var c in norm)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
