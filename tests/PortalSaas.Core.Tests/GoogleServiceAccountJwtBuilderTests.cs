using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PortalSaas.Core.Correo;
using Xunit;

namespace PortalSaas.Core.Tests;

public class GoogleServiceAccountJwtBuilderTests
{
    /// <summary>
    /// Par de llaves RSA generado EN EL TEST -- nunca una credencial real de Google.
    /// Permite verificar que la firma es válida sin depender de ninguna cuenta de
    /// servicio real.
    /// </summary>
    private static (string PrivateKeyPem, RSA PublicKey) GenerarParDePrueba()
    {
        var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa);
    }

    private static (JsonElement Header, JsonElement Claims, string Signature) Descomponer(string jwt)
    {
        var partes = jwt.Split('.');
        Assert.Equal(3, partes.Length);

        static JsonElement DecodificarSegmento(string segmento)
        {
            var padded = segmento.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            return JsonDocument.Parse(Convert.FromBase64String(padded)).RootElement.Clone();
        }

        return (DecodificarSegmento(partes[0]), DecodificarSegmento(partes[1]), partes[2]);
    }

    [Fact]
    public void BuildSignedAssertion_ProduceUnJwtDeTresPartesConLosClaimsCorrectos()
    {
        var (privateKeyPem, publicKey) = GenerarParDePrueba();
        var ahora = new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero);

        var jwt = GoogleServiceAccountJwtBuilder.BuildSignedAssertion(
            "cuenta-de-servicio@proyecto.iam.gserviceaccount.com", privateKeyPem, "no-reply@comercialdepor.cl", ahora);

        var (header, claims, _) = Descomponer(jwt);

        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("cuenta-de-servicio@proyecto.iam.gserviceaccount.com", claims.GetProperty("iss").GetString());
        Assert.Equal("no-reply@comercialdepor.cl", claims.GetProperty("sub").GetString());
        Assert.Equal(GoogleServiceAccountJwtBuilder.GmailSendScope, claims.GetProperty("scope").GetString());
        Assert.Equal(ahora.ToUnixTimeSeconds(), claims.GetProperty("iat").GetInt64());
        Assert.Equal(ahora.AddMinutes(50).ToUnixTimeSeconds(), claims.GetProperty("exp").GetInt64());
    }

    [Fact]
    public void BuildSignedAssertion_LaFirmaEsVerificableConLaLlavePublicaCorrespondiente()
    {
        var (privateKeyPem, publicKey) = GenerarParDePrueba();

        var jwt = GoogleServiceAccountJwtBuilder.BuildSignedAssertion(
            "cuenta@proyecto.iam.gserviceaccount.com", privateKeyPem, "no-reply@comercialdepor.cl", DateTimeOffset.UtcNow);

        var ultimoPunto = jwt.LastIndexOf('.');
        var datosFirmados = Encoding.UTF8.GetBytes(jwt[..ultimoPunto]);
        var firmaSegmento = jwt[(ultimoPunto + 1)..].Replace('-', '+').Replace('_', '/');
        firmaSegmento = firmaSegmento.PadRight(firmaSegmento.Length + (4 - firmaSegmento.Length % 4) % 4, '=');
        var firma = Convert.FromBase64String(firmaSegmento);

        var esValida = publicKey.VerifyData(datosFirmados, firma, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.True(esValida);
    }
}
