using Modulo.Despachos.Models;
using PortalSaas.Abstractions.Modelos.Dte;

namespace Modulo.Despachos.Dominio.Guias;

/// <summary>Línea digitada o importada: código opcional, descripción, cantidad, unidad y precio unitario neto.</summary>
public sealed record ManualGuideLineInput(string? ItemCode, string Description, decimal Quantity, string? Unit, decimal UnitPrice);

/// <summary>Referencia opcional (tipo de documento como texto: "52", "33", "801"...).</summary>
public sealed record ManualGuideReferenceInput(string DocumentType, long Folio, DateOnly Date, string? Reason);

/// <summary>Transporte Res. 154 de la guía manual (de los maestros o digitado como particular).</summary>
public sealed record ManualGuideTransportInput(string? Plate, string? TrailerPlate, string? DriverTaxId, string? DriverName,
    string? CarrierTaxId, string? CarrierName);

/// <summary>Formulario de la guía manual (spec §6.1).</summary>
public sealed record ManualGuideInput(
    DateOnly IssueDate, int TransferIndicator, int DispatchType, long OriginId,
    string ReceiverTaxId, string ReceiverName, string? ReceiverActivity, string? ReceiverCardCode,
    string ReceiverAddress, string ReceiverCommune, string? ReceiverCity,
    string DestinationAddress, string DestinationCommune, string? DestinationCity,
    IReadOnlyList<ManualGuideLineInput> Lines, IReadOnlyList<ManualGuideReferenceInput> References,
    string? Notes, string TransportMode, ManualGuideTransportInput? Transport);

/// <summary>Origen del traslado (DirOrigen / CmnaOrigen / CiudadOrigen).</summary>
public sealed record ManualGuideOrigin(string Address, string Commune, string? City);

/// <summary>
/// Arma y valida el DTE 52 de una guía manual, sin I/O:
/// - IVA según el motivo: afecto = neto + IVA 19%; no afecto = líneas exentas (MntExe), total = neto;
/// - observaciones como última línea del detalle (sin código, monto 0), solo al emitir;
/// - transporte Res. 154: digitado o de los maestros en el borrador; el del manifiesto se pone al emitir.
/// </summary>
public static class ManualGuideBuilder
{
    public const int MaxNotes = 500;
    public const string NotesLineName = "OBSERVACIONES";
    private const decimal VatRate = 19m;

    /// <summary>Errores del formulario (vacío = válido). <paramref name="maxLines"/> = máximo de la compañía, incluida la línea de observaciones.</summary>
    public static IReadOnlyList<string> Validate(ManualGuideInput input, int maxLines)
    {
        var errors = new List<string>();
        if (input.TransferIndicator <= 0) errors.Add("Elija el motivo de traslado.");
        if (input.DispatchType is < 1 or > 3) errors.Add("Elija el tipo de despacho.");
        if (!RutChileno.IsValid(input.ReceiverTaxId)) errors.Add($"El RUT del receptor «{input.ReceiverTaxId}» no es válido.");
        if (Blank(input.ReceiverName)) errors.Add("Ingrese la razón social del receptor.");
        if (Blank(input.ReceiverActivity)) errors.Add("Ingrese el giro del receptor.");
        if (Blank(input.ReceiverAddress) || Blank(input.ReceiverCommune)) errors.Add("Ingrese la dirección y comuna del receptor.");
        if (Blank(input.DestinationAddress) || Blank(input.DestinationCommune)) errors.Add("Ingrese la dirección y comuna de destino.");

        if (input.Lines.Count == 0) errors.Add("Agregue al menos una línea de detalle.");
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var l = input.Lines[i];
            if (Blank(l.Description)) errors.Add($"Línea {i + 1}: ingrese la descripción.");
            if (l.Quantity <= 0) errors.Add($"Línea {i + 1}: la cantidad debe ser mayor que cero.");
            if (l.UnitPrice < 0) errors.Add($"Línea {i + 1}: el precio no puede ser negativo.");
        }
        var notes = !Blank(input.Notes);
        if (input.Lines.Count + (notes ? 1 : 0) > maxLines)
            errors.Add($"La guía admite un máximo de {maxLines} líneas (incluida la de observaciones).");
        if (notes && input.Notes!.Trim().Length > MaxNotes) errors.Add($"Las observaciones admiten hasta {MaxNotes} caracteres.");

        for (var i = 0; i < input.References.Count; i++)
        {
            var r = input.References[i];
            if (Blank(r.DocumentType) || r.Folio <= 0) errors.Add($"Referencia {i + 1}: indique el tipo de documento y el folio.");
        }

        if (input.TransportMode is not (ManualTransportModes.Manifest or ManualTransportModes.Master or ManualTransportModes.Manual))
            errors.Add("Elija de dónde salen los datos de transporte.");
        else if (input.TransportMode != ManualTransportModes.Manifest && input.DispatchType is 2 or 3)
        {
            // Res. 154: con despacho por cuenta del emisor la guía identifica vehículo, chofer y transportista.
            var t = input.Transport;
            const string why = " (obligatorio con despacho por cuenta del emisor, Res. 154)";
            if (Blank(t?.Plate)) errors.Add("Ingrese la patente del vehículo" + why + ".");
            if (!RutChileno.IsValid(t?.DriverTaxId)) errors.Add("Ingrese un RUT de chofer válido" + why + ".");
            if (Blank(t?.DriverName)) errors.Add("Ingrese el nombre del chofer" + why + ".");
            if (!RutChileno.IsValid(t?.CarrierTaxId)) errors.Add("Ingrese un RUT de transportista válido" + why + ".");
        }
        return errors;
    }

    /// <summary>Documento guardado del borrador: sin línea de observaciones; transporte digitado/maestros (vacío si va en manifiesto).</summary>
    public static DteDocument BuildDraft(ManualGuideInput input, ManualGuideOrigin origin, bool vatAffected)
    {
        var lines = input.Lines.Select((l, i) => new DteLine(
            i + 1, Blank(l.ItemCode) ? null : l.ItemCode!.Trim(), l.Description.Trim(), null, l.Quantity,
            Blank(l.Unit) ? null : l.Unit!.Trim(), l.UnitPrice,
            Math.Round(l.Quantity * l.UnitPrice, 0, MidpointRounding.AwayFromZero), Exempt: !vatAffected)).ToList();
        var refs = input.References.Select((r, i) => new DteReference(
            i + 1, r.DocumentType.Trim(), r.Folio, r.Date, null, Blank(r.Reason) ? null : r.Reason!.Trim())).ToList();
        var t = input.TransportMode == ManualTransportModes.Manifest ? null : input.Transport;
        var transport = new DteTransport(
            Clean(t?.Plate), Clean(t?.TrailerPlate), Rut(t?.CarrierTaxId),
            Blank(t?.DriverTaxId) && Blank(t?.DriverName) ? null : new DteDriver(Rut(t?.DriverTaxId) ?? "", Clean(t?.DriverName) ?? ""),
            input.DestinationAddress.Trim(), input.DestinationCommune.Trim(), Clean(input.DestinationCity),
            Blank(t?.CarrierName) ? null : new DteCustoms(null, null, t!.CarrierName!.Trim()),
            null, null, null);
        return new DteDocument(
            new DteIdDoc(52, input.IssueDate, input.TransferIndicator, input.DispatchType),
            new DteIssuerOverride(origin.Address, origin.Commune, origin.City),
            new DteReceiver(RutChileno.Normalize(input.ReceiverTaxId), input.ReceiverName.Trim(), Clean(input.ReceiverActivity),
                input.ReceiverAddress.Trim(), input.ReceiverCommune.Trim(), Clean(input.ReceiverCity)),
            transport, null, lines, refs, Array.Empty<DteExtraField>());
    }

    /// <summary>
    /// Documento a emitir: fecha de emisión, línea de observaciones al final y, si <paramref name="transport"/> no es
    /// null (snapshot del manifiesto), ese transporte con el destino del borrador.
    /// </summary>
    public static DteDocument ForEmission(DteDocument draft, string? notes, DteTransport? transport, DateOnly issueDate)
    {
        var lines = draft.Lines.ToList();
        if (!Blank(notes))
            lines.Add(new DteLine(lines.Count + 1, null, NotesLineName, notes!.Trim(), 1, null, 0, 0,
                Exempt: lines.Count > 0 && lines.All(l => l.Exempt)));
        var dest = draft.Transport;
        var t = transport is null ? dest : transport with
        {
            DestinationAddress = dest?.DestinationAddress, DestinationCommune = dest?.DestinationCommune,
            DestinationCity = dest?.DestinationCity,
        };
        return draft with { IdDoc = draft.IdDoc with { IssueDate = issueDate }, Transport = t, Lines = lines };
    }

    /// <summary>Formulario desde el documento guardado y las columnas de la fila (para editar el borrador).</summary>
    public static ManualGuideInput ToInput(DteDocument draft, TransportGuide row)
    {
        var t = draft.Transport;
        return new ManualGuideInput(
            row.IssueDate ?? draft.IdDoc.IssueDate, row.TransferIndicator, row.DispatchType ?? draft.IdDoc.DispatchType ?? 0,
            row.OriginId ?? 0,
            draft.Receiver.TaxId, draft.Receiver.Name, draft.Receiver.Activity, row.ReceiverCardCode,
            draft.Receiver.Address ?? "", draft.Receiver.Commune ?? "", draft.Receiver.City,
            t?.DestinationAddress ?? "", t?.DestinationCommune ?? "", t?.DestinationCity,
            draft.Lines.Select(l => new ManualGuideLineInput(l.ItemCode, l.Name, l.Quantity, l.Unit, l.UnitPrice ?? 0)).ToList(),
            draft.References.Select(r => new ManualGuideReferenceInput(r.DocumentType, r.Folio ?? 0, r.Date, r.Reason)).ToList(),
            row.Notes, row.TransportMode ?? ManualTransportModes.Manual,
            new ManualGuideTransportInput(t?.Plate, t?.TrailerPlate, t?.Driver?.TaxId is { Length: > 0 } d ? d : null,
                t?.Driver?.Name is { Length: > 0 } n ? n : null, t?.CarrierTaxId, t?.Customs?.CarrierName));
    }

    /// <summary>Neto (afectas), IVA 19% del neto y total (neto + exento + IVA), como la plataforma.</summary>
    public static (decimal Net, decimal Vat, decimal Total) Totals(IEnumerable<DteLine> lines)
    {
        var list = lines.ToList();
        var net = list.Where(l => !l.Exempt).Sum(l => l.Amount);
        var exempt = list.Where(l => l.Exempt).Sum(l => l.Amount);
        var vat = Math.Round(net * VatRate / 100m, 0, MidpointRounding.AwayFromZero);
        return (net + exempt, vat, net + exempt + vat);
    }

    private static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
    private static string? Clean(string? s) => Blank(s) ? null : s!.Trim();
    private static string? Rut(string? s) => RutChileno.IsValid(s) ? RutChileno.Normalize(s!) : Clean(s);
}
