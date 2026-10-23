using PortalSaas.Abstractions.Modelos.Dte;

namespace PortalSaas.Core.DocumentosElectronicos;

/// <summary>
/// Reglas del formato DTE y de la Res. Ex. SII N°154 que se validan ANTES de enviar al proveedor:
/// el ambiente de certificación de FACELE no exige los campos obligatorios de la guía 52, así que la
/// plataforma no confía en que el proveedor valide. Field = nombre del tag SII.
/// </summary>
public static class DteRules
{
    public const int MaxLines = 60;
    public const int MaxReferences = 40;

    public static IReadOnlyList<DteValidationError> Validate(DteDocument doc)
    {
        var errors = new List<DteValidationError>();
        void Add(string field, string message) => errors.Add(new DteValidationError(field, message));

        if (!Rut.IsValid(doc.Receiver.TaxId))
            Add("RUTRecep", $"El RUT del receptor «{doc.Receiver.TaxId}» no es válido.");
        if (string.IsNullOrWhiteSpace(doc.Receiver.Name))
            Add("RznSocRecep", "Falta la razón social del receptor.");

        if (doc.Lines.Count == 0)
            Add("Detalle", "El documento debe tener al menos una línea de detalle.");
        else if (doc.Lines.Count > MaxLines)
            Add("Detalle", $"El documento tiene {doc.Lines.Count} líneas; el máximo del formato es {MaxLines}.");
        foreach (var l in doc.Lines)
        {
            if (l.Quantity <= 0) Add("Detalle", $"La línea {l.LineNumber} debe tener cantidad mayor a cero.");
            if (string.IsNullOrWhiteSpace(l.Name)) Add("Detalle", $"La línea {l.LineNumber} no tiene nombre de artículo.");
        }
        if (doc.References.Count > MaxReferences)
            Add("Referencia", $"El documento tiene {doc.References.Count} referencias; el máximo del formato es {MaxReferences}.");

        if (doc.IdDoc.DteType == 52) ValidateDispatchGuide(doc, Add);
        return errors;
    }

    private static void ValidateDispatchGuide(DteDocument doc, Action<string, string> add)
    {
        if (doc.IdDoc.TransferIndicator is not (>= 1 and <= 9))
            add("IndTraslado", "La guía de despacho exige el indicador de traslado (1 a 9).");
        if (string.IsNullOrWhiteSpace(doc.Receiver.Activity))
            add("GiroRecep", "Falta el giro del receptor.");

        var t = doc.Transport;
        if (t is null)
        {
            add("Transporte", "La guía de despacho exige los datos de transporte (Res. 154).");
            return;
        }
        if (string.IsNullOrWhiteSpace(t.Plate)) add("Patente", "Falta la patente del vehículo.");
        if (string.IsNullOrWhiteSpace(t.CarrierTaxId)) add("RUTTrans", "Falta el RUT del transportista.");
        else if (!Rut.IsValid(t.CarrierTaxId)) add("RUTTrans", $"El RUT del transportista «{t.CarrierTaxId}» no es válido.");
        if (t.Driver is null || string.IsNullOrWhiteSpace(t.Driver.TaxId)) add("RUTChofer", "Falta el RUT del chofer.");
        else if (!Rut.IsValid(t.Driver.TaxId)) add("RUTChofer", $"El RUT del chofer «{t.Driver.TaxId}» no es válido.");
        if (t.Driver is null || string.IsNullOrWhiteSpace(t.Driver.Name)) add("NombreChofer", "Falta el nombre del chofer.");
        if (string.IsNullOrWhiteSpace(t.DestinationAddress)) add("DirDest", "Falta la dirección de destino.");
        if (string.IsNullOrWhiteSpace(t.DestinationCommune)) add("CmnaDest", "Falta la comuna de destino.");
        if (t.DepartureDate is null) add("FchSalida", "Falta la fecha de salida.");
        if (t.DepartureTime is null) add("HraSalida", "Falta la hora de salida.");
        if (t.ArrivalDate is null) add("FchLlegada", "Falta la fecha de llegada estimada.");
        else if (t.DepartureDate is { } dep && t.ArrivalDate < dep)
            add("FchLlegada", "La fecha de llegada no puede ser anterior a la de salida.");
    }
}
