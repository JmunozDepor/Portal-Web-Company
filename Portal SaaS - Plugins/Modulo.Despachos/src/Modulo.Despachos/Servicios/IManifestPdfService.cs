namespace Modulo.Despachos.Servicios;

/// <summary>PDF interno (no tributario) del manifiesto. El permiso EXPORT se exige en las páginas.</summary>
public interface IManifestPdfService
{
    /// <summary>
    /// Genera el PDF y registra el evento printed (original) o reprinted (copia, con marca "COPIA",
    /// fecha/hora Chile y usuario). Lanza InvalidOperationException si el manifiesto no existe.
    /// </summary>
    Task<byte[]> RenderAsync(Guid manifestId, bool isCopy);
}
