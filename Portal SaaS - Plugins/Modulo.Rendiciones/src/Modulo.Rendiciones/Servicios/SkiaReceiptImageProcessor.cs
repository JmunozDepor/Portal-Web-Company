using SkiaSharp;

namespace Modulo.Rendiciones.Servicios;

/// <summary>
/// Implementación de IReceiptImageProcessor con SkiaSharp (MIT). Única clase del
/// plugin que referencia SkiaSharp -- si algún día cambia la librería, se reemplaza
/// solo este archivo.
/// </summary>
public sealed class SkiaReceiptImageProcessor : IReceiptImageProcessor
{
    /// <summary>Lado largo máximo tras reescalar. Suficiente para leer una boleta.</summary>
    public const int DefaultMaxLongEdgePx = 2200;

    /// <summary>Calidad JPEG de salida por defecto.</summary>
    public const int DefaultJpegQuality = 78;

    private static readonly HashSet<string> RasterMimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/jpg", "image/png", "image/webp",
    };

    public Task<ProcessedReceipt> ProcessAsync(
        string fileName, string mimeType, byte[] content,
        int? maxLongEdgePx = null, int? jpegQuality = null, CancellationToken ct = default)
    {
        mimeType ??= "";
        var mime = mimeType.Trim();
        if (!RasterMimes.Contains(mime))
            return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

        var maxEdge = maxLongEdgePx is > 0 ? maxLongEdgePx.Value : DefaultMaxLongEdgePx;
        var quality = jpegQuality is >= 1 and <= 100 ? jpegQuality.Value : DefaultJpegQuality;

        try
        {
            using var input = new MemoryStream(content, writable: false);
            using var codec = SKCodec.Create(input);
            if (codec is null)
                return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

            using var original = SKBitmap.Decode(codec);
            if (original is null)
                return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

            using var oriented = ApplyOrientation(original, codec.EncodedOrigin);
            using var scaled = Downscale(oriented, maxEdge);

            using var image = SKImage.FromBitmap(scaled);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            var jpegBytes = encoded.ToArray();

            if (jpegBytes.Length >= content.Length)
                return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));

            var newName = Path.ChangeExtension(fileName, ".jpg");
            return Task.FromResult(new ProcessedReceipt(newName, "image/jpeg", jpegBytes));
        }
        catch
        {
            // Un archivo que dice ser imagen pero no se puede decodificar no debe
            // reventar la corrida -- se deja tal cual.
            return Task.FromResult(new ProcessedReceipt(fileName, mimeType, content));
        }
    }

    private static SKBitmap ApplyOrientation(SKBitmap src, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.Default or SKEncodedOrigin.TopLeft)
            return src.Copy();

        var swapWh = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        var dst = new SKBitmap(swapWh ? src.Height : src.Width, swapWh ? src.Width : src.Height);
        using var canvas = new SKCanvas(dst);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Scale(-1, 1); canvas.Translate(-src.Width, 0); break;
            case SKEncodedOrigin.BottomRight: canvas.RotateDegrees(180, src.Width / 2f, src.Height / 2f); break;
            case SKEncodedOrigin.BottomLeft: canvas.Scale(1, -1); canvas.Translate(0, -src.Height); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(dst.Width, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(dst.Width, dst.Height); canvas.RotateDegrees(90); canvas.Scale(-1, 1); canvas.Translate(-src.Width, 0); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, dst.Height); canvas.RotateDegrees(-90); break;
        }

        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }

    private static SKBitmap Downscale(SKBitmap src, int maxLongEdge)
    {
        var longEdge = Math.Max(src.Width, src.Height);
        if (longEdge <= maxLongEdge)
            return src.Copy();

        var ratio = (double)maxLongEdge / longEdge;
        var w = Math.Max(1, (int)Math.Round(src.Width * ratio));
        var h = Math.Max(1, (int)Math.Round(src.Height * ratio));

        return src.Resize(new SKImageInfo(w, h), SKFilterQuality.Medium) ?? src.Copy();
    }
}
