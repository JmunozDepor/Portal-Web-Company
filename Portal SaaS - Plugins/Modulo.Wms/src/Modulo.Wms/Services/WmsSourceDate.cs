using System.Globalization;

namespace Modulo.Wms.Services;

/// <summary>
/// Normaliza el valor "SourceUpdateDate" que trae <c>SqlDirectConnector</c> desde la
/// Query de Bajada a un <see cref="DateTime"/> con <see cref="DateTimeKind.Utc"/>, apto
/// para las columnas <c>source_update_date</c> del staging (todas
/// <c>timestamp with time zone</c>): con EF Core 8 + Npgsql 8, escribir un DateTime que
/// NO sea Utc revienta en <c>SaveChangesAsync</c> con
/// <em>"Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with
/// time zone'"</em> (era el <c>DbUpdateException</c> que tumbaba la Bajada de Sucursal).
///
/// SAP entrega este campo de dos formas: DateTime nativo (<c>OITM.UpdateDate</c>) o
/// string sin zona (<c>OCRD.U_NX_UPDATEDATE</c>, UDF <c>nvarchar</c>). En ambos casos es
/// reloj de pared y solo se usa como cursor de cambio (comparación de igualdad/orden),
/// así que se marca Utc SIN desplazar la hora. Nulo/ausente/vacío -&gt;
/// <see cref="DateTime.MinValue"/> (Npgsql lo manda como <c>-infinity</c>): "sin fecha".
/// </summary>
public static class WmsSourceDate
{
    public static DateTime ToUtc(object? valor) => valor switch
    {
        null => default,
        DateTime dt => dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc),
        string s when string.IsNullOrWhiteSpace(s) => default,
        string s => DateTime.SpecifyKind(DateTime.Parse(s, CultureInfo.InvariantCulture), DateTimeKind.Utc),
        _ => throw new InvalidOperationException($"SourceUpdateDate con tipo inesperado: {valor.GetType().FullName}"),
    };
}
