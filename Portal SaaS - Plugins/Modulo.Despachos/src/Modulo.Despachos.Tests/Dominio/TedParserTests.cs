using Modulo.Despachos.Dominio;

namespace Modulo.Despachos.Tests.Dominio;

public class TedParserTests
{
    private const string Ted = "<TED version=\"1.0\"><DD><RE>76086428-5</RE><TD>33</TD><F>154321</F><FE>2026-10-19</FE><RR>96509660-4</RR><RSR>COMERCIAL ÑUÑOA</RSR><MNT>119000</MNT><IT1>ZAPATILLA</IT1></DD><FRMT algoritmo=\"SHA1withRSA\">x</FRMT></TED>";

    [Fact]
    public void TryParse_TolerarRuidoDelEscaner()
    {
        Assert.True(TedParser.TryParse("\r\n " + Ted + "\r\n", out var d));
        Assert.Equal((33, 154321L, "76086428-5", "96509660-4", 119000m),
            (d.DocumentType, d.Folio, d.IssuerRut, d.ReceiverRut, d.Amount));
        Assert.Equal(new DateOnly(2026, 10, 19), d.IssueDate);
    }

    [Fact]
    public void TryParse_RechazaBasura()
    {
        Assert.False(TedParser.TryParse("154321", out _));
        Assert.False(TedParser.TryParse("<TED><DD><TD>x</TD></DD>", out _));
        Assert.False(TedParser.TryParse(null, out _));
        Assert.False(TedParser.TryParse("<TED><DD><RE>1</RE><TD>x</TD><F>1</F><FE>2026-10-19</FE><RR>2</RR><MNT>1</MNT></DD></TED>", out _));
    }

    [Fact]
    public void TryParse_NoResuelveEntidadesExternas()
    {
        var xxe = "<!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><TED><DD><RE>76086428-5</RE><TD>33</TD><F>1</F><FE>2026-10-19</FE><RR>96509660-4</RR><MNT>1</MNT></DD></TED>";
        // El DOCTYPE queda fuera del recorte <TED>..</TED>; igual no debe resolver nada.
        Assert.True(TedParser.TryParse(xxe, out var d));
        Assert.Equal(1L, d.Folio);
    }
}
