using PortalSaas.Core.ImportacionGenerica;
using Xunit;

namespace PortalSaas.Core.Tests.ImportacionGenerica;

public sealed class GenericImportUserFieldNameValidationTests
{
    [Theory]
    [InlineData("U_NumAtCard")]
    [InlineData("U_NX_Auto_ABS")]
    [InlineData("Comments")]
    [InlineData("  U_NX_WMS_SEND  ")]
    public void NombreValido_SeAceptaRecortado(string sapFieldName)
    {
        var result = GenericImportUserFieldService.ValidateSapFieldName("Etiqueta", sapFieldName);

        Assert.Equal(sapFieldName.Trim(), result);
    }

    [Theory]
    [InlineData("Usuario-Numero Orden ")]
    [InlineData("Auto Abastecimiento")]
    [InlineData("Número")]
    [InlineData("1Campo")]
    [InlineData("")]
    public void NombreConEspaciosGuionesOTildes_SeRechaza(string sapFieldName)
    {
        Assert.Throws<InvalidOperationException>(() =>
            GenericImportUserFieldService.ValidateSapFieldName("Etiqueta libre", sapFieldName));
    }

    [Fact]
    public void EtiquetaYNombreInvertidos_ElMensajeLoSugiere()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GenericImportUserFieldService.ValidateSapFieldName("U_NumAtCard", "Usuario-Numero Orden "));

        Assert.Contains("invertidos", ex.Message);
        Assert.Contains("U_NumAtCard", ex.Message);
    }

    [Fact]
    public void NombreReservado_SeSigueRechazando()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GenericImportUserFieldService.ValidateSapFieldName("N° ref", "NumAtCard"));

        Assert.Contains("reservado", ex.Message);
    }
}
