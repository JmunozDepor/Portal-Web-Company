namespace PortalSaas.Abstractions.Modelos;

/// <summary>
/// Motor genérico de documento que consume Modulo.ImportacionGenerica -- portado de
/// ModuloDocumentoGenerico (referencia-original/PortalSAP_v2), nombrado distinto a propósito
/// del nombre del namespace raíz del plugin ("ImportacionGenerica") para evitar ambigüedad de
/// resolución en C#, mismo motivo documentado en el original.
/// </summary>
public enum GenericImportModule
{
    Sales,
    Purchase,
    Inventory,
}
