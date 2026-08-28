using PortalSaas.Core.Sap;
using Xunit;

namespace PortalSaas.Core.Tests;

/// <summary>
/// Ejercita HanaToSqlServerTranslator contra formas de query calcadas de las reales de
/// PortalSAP_v2 (ver su TraductorSqlHanaASqlServerTests) -- no contra SQL sintético
/// arbitrario, el traductor está pensado solo para el set cerrado de patrones ya
/// inventariados, no para SQL general.
/// </summary>
public class HanaToSqlServerTranslatorTests
{
    [Fact]
    public void Translate_Parametro_ReemplazaDosPuntosPorArroba()
    {
        var resultado = HanaToSqlServerTranslator.Translate("""SELECT * FROM "OITM" WHERE "ItemCode" = :codigo""");

        Assert.Equal("""SELECT * FROM "OITM" WHERE "ItemCode" = @codigo""", resultado);
    }

    [Fact]
    public void Translate_ParametroDentroDeLiteralDeTexto_NoLoToca()
    {
        var resultado = HanaToSqlServerTranslator.Translate(
            """SELECT * FROM "OITM" WHERE "Comments" = 'hola:mundo' AND "ItemCode" = :codigo""");

        Assert.Equal(
            """SELECT * FROM "OITM" WHERE "Comments" = 'hola:mundo' AND "ItemCode" = @codigo""", resultado);
    }

    [Fact]
    public void Translate_LiteralConComillaEscapada_NoRompeElParseoDeLiterales()
    {
        // '' dentro de un literal es una comilla simple escapada (ej. "O'Brien"), no el
        // cierre del literal -- si el traductor la interpreta mal, el ":codigo" de abajo
        // terminaría "dentro" del literal según el parser y no se traduciría.
        var resultado = HanaToSqlServerTranslator.Translate(
            """SELECT * FROM "OCRD" WHERE "CardName" = 'O''Brien' AND "CardCode" = :codigo""");

        Assert.Equal(
            """SELECT * FROM "OCRD" WHERE "CardName" = 'O''Brien' AND "CardCode" = @codigo""", resultado);
    }

    [Fact]
    public void Translate_LimitConOffset_MuevenAOffsetFetchNext()
    {
        var resultado = HanaToSqlServerTranslator.Translate(
            """SELECT * FROM "ORDR" o ORDER BY o."DocEntry" DESC LIMIT 25 OFFSET 50""");

        Assert.Equal(
            """SELECT * FROM "ORDR" o ORDER BY o."DocEntry" DESC OFFSET 50 ROWS FETCH NEXT 25 ROWS ONLY""", resultado);
    }

    [Fact]
    public void Translate_LimitSinOffset_SeMueveATopDespuesDelSelect()
    {
        var resultado = HanaToSqlServerTranslator.Translate(
            """SELECT "ItemCode" AS "Codigo", "ItemName" AS "Nombre" FROM "OITM" WHERE 1 = 1 ORDER BY "ItemName" LIMIT 50""");

        Assert.Equal(
            """SELECT TOP (50) "ItemCode" AS "Codigo", "ItemName" AS "Nombre" FROM "OITM" WHERE 1 = 1 ORDER BY "ItemName" """.TrimEnd(),
            resultado);
    }

    [Fact]
    public void Translate_SinLimit_EsNoOp()
    {
        const string sql = """SELECT "ItemCode" AS "Codigo" FROM "OITM" WHERE 1 = 1 ORDER BY "ItemName" """;

        var resultado = HanaToSqlServerTranslator.Translate(sql);

        Assert.DoesNotContain("TOP", resultado);
        Assert.DoesNotContain("LIMIT", resultado);
        Assert.Equal(sql, resultado);
    }

    [Fact]
    public void Translate_ToVarcharUnSoloArgumento_SeConvierteACast()
    {
        var resultado = HanaToSqlServerTranslator.Translate(
            """SELECT TO_VARCHAR("WhsCode") AS "Codigo", "WhsName" AS "Nombre" FROM "OWHS" WHERE 1 = 1 ORDER BY "WhsCode" """.TrimEnd());

        Assert.Equal(
            """SELECT CAST("WhsCode" AS NVARCHAR(100)) AS "Codigo", "WhsName" AS "Nombre" FROM "OWHS" WHERE 1 = 1 ORDER BY "WhsCode" """.TrimEnd(),
            resultado);
    }

    [Fact]
    public void Translate_ConcatenacionDoblePipe_Empleado()
    {
        var resultado = HanaToSqlServerTranslator.Translate(
            """SELECT "empID" AS "Codigo", "firstName" || ' ' || "lastName" AS "Nombre" FROM "OHEM" WHERE 1 = 1 ORDER BY "lastName" """.TrimEnd());

        Assert.Equal(
            """SELECT "empID" AS "Codigo", "firstName" + ' ' + "lastName" AS "Nombre" FROM "OHEM" WHERE 1 = 1 ORDER BY "lastName" """.TrimEnd(),
            resultado);
    }

    [Fact]
    public void Translate_MultiplesParametrosDistintos_FiltroDinamico()
    {
        const string sql = """
            (UPPER("ItemCode") LIKE UPPER(:texto0) OR UPPER("ItemName") LIKE UPPER(:texto1)) AND "ItemCode" IN (:codigo0,:codigo1)
            """;

        var resultado = HanaToSqlServerTranslator.Translate(sql);

        Assert.Equal(
            """(UPPER("ItemCode") LIKE UPPER(@texto0) OR UPPER("ItemName") LIKE UPPER(@texto1)) AND "ItemCode" IN (@codigo0,@codigo1)""",
            resultado);
    }

    [Fact]
    public void Translate_QueryCompletaConDosToVarcharMasLimitOffsetMasParametro()
    {
        var sql = """
            SELECT o."DocEntry" AS "DocEntry", o."DocNum" AS "DocNum", o."CardCode" AS "SocioNegocioCardCode",
                   o."CardName" AS "SocioNegocioNombre", o."DocDate" AS "Fecha",
                   TO_VARCHAR(o."ToWhsCode") AS "AlmacenDestinoCodigo", o."Address2" AS "DestinoDireccion",
                   o."U_NumAtCard" AS "NumAtCard",
                   CASE o."DocStatus" WHEN 'O' THEN 'Abierto' ELSE 'Cerrado' END AS "Estado"
            FROM "OWTQ" o
            WHERE TO_VARCHAR(o."DocNum") LIKE :docNum
            ORDER BY o."DocEntry" DESC
            LIMIT 25 OFFSET 0
            """;

        var resultado = HanaToSqlServerTranslator.Translate(sql);

        Assert.DoesNotContain("TO_VARCHAR", resultado);
        Assert.DoesNotContain("LIMIT", resultado);
        Assert.DoesNotContain(":docNum", resultado);
        Assert.Contains("""CAST(o."ToWhsCode" AS NVARCHAR(100))""", resultado);
        Assert.Contains("""CAST(o."DocNum" AS NVARCHAR(100))""", resultado);
        Assert.Contains("@docNum", resultado);
        Assert.Contains("OFFSET 0 ROWS FETCH NEXT 25 ROWS ONLY", resultado);
        Assert.Contains("""CASE o."DocStatus" WHEN 'O' THEN 'Abierto' ELSE 'Cerrado' END""", resultado);
    }
}
