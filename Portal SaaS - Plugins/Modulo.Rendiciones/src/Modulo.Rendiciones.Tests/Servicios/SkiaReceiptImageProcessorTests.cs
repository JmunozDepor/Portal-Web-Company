using Modulo.Rendiciones.Servicios;
using SkiaSharp;

namespace Modulo.Rendiciones.Tests.Servicios;

public class SkiaReceiptImageProcessorTests
{
    private static byte[] MakePng(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task ProcessAsync_downscales_large_image_and_returns_jpeg()
    {
        var sut = new SkiaReceiptImageProcessor();
        var big = MakePng(4000, 3000);

        var result = await sut.ProcessAsync("boleta.png", "image/png", big);

        Assert.Equal("image/jpeg", result.MimeType);
        Assert.EndsWith(".jpg", result.FileName);
        Assert.True(result.Content.Length < big.Length);

        using var codec = SKCodec.Create(new MemoryStream(result.Content));
        Assert.NotNull(codec);
        Assert.True(Math.Max(codec!.Info.Width, codec.Info.Height) <= SkiaReceiptImageProcessor.DefaultMaxLongEdgePx);
    }

    [Fact]
    public async Task ProcessAsync_respects_explicit_max_edge_override()
    {
        var sut = new SkiaReceiptImageProcessor();
        var big = MakePng(4000, 3000);

        var result = await sut.ProcessAsync("b.png", "image/png", big, maxLongEdgePx: 1000, jpegQuality: 70);

        using var codec = SKCodec.Create(new MemoryStream(result.Content));
        Assert.True(Math.Max(codec!.Info.Width, codec.Info.Height) <= 1000);
    }

    [Fact]
    public async Task ProcessAsync_leaves_pdf_untouched()
    {
        var sut = new SkiaReceiptImageProcessor();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

        var result = await sut.ProcessAsync("boleta.pdf", "application/pdf", bytes);

        Assert.Equal("boleta.pdf", result.FileName);
        Assert.Equal("application/pdf", result.MimeType);
        Assert.Equal(bytes, result.Content);
    }

    [Fact]
    public async Task ProcessAsync_returns_original_when_bytes_are_not_a_real_image()
    {
        var sut = new SkiaReceiptImageProcessor();
        var junk = new byte[] { 1, 2, 3, 4, 5 };

        var result = await sut.ProcessAsync("x.jpg", "image/jpeg", junk);

        Assert.Equal(junk, result.Content);
    }

    [Fact]
    public async Task ProcessAsync_returns_original_when_recompressed_is_not_smaller()
    {
        var sut = new SkiaReceiptImageProcessor();
        var tiny = MakePng(8, 8); // ya minúsculo: el JPEG no va a quedar más chico

        var result = await sut.ProcessAsync("t.png", "image/png", tiny);

        Assert.Equal(tiny, result.Content);
        Assert.Equal("image/png", result.MimeType);
    }
}
