namespace PortalSaas.Abstractions.Modelos;

/// <summary>Tipo de dato declarado para un campo de usuario -- controla cómo se parsea el valor de la celda antes de mandarlo a Service Layer. Portado de TipoDatoCampoImportacionGenerica.</summary>
public enum GenericImportFieldDataType
{
    Text,
    Number,
    Date,
}
