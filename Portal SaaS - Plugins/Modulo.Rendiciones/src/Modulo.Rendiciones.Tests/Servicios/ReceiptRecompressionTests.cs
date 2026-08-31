using Modulo.Rendiciones.Models;
using Modulo.Rendiciones.Servicios;

namespace Modulo.Rendiciones.Tests.Servicios;

public class ReceiptRecompressionTests
{
    private static ExpenseReceipt R(string mime, int len) => new()
    {
        CompanyId = Guid.NewGuid(), UserId = Guid.NewGuid(),
        FileName = "x", MimeType = mime, Content = new byte[len],
    };

    [Theory]
    [InlineData("image/jpeg", 600_000, true)]
    [InlineData("image/png", 600_000, true)]
    [InlineData("image/webp", 600_000, true)]
    [InlineData("image/jpeg", 100_000, false)]        // ya chico
    [InlineData("application/pdf", 5_000_000, false)] // PDF nunca
    [InlineData("image/heic", 5_000_000, false)]      // no raster soportado
    public void ShouldRecompress_picks_only_large_raster_images(string mime, int len, bool expected)
    {
        Assert.Equal(expected, ReceiptRecompression.ShouldRecompress(R(mime, len), minBytes: 500_000));
    }
}
