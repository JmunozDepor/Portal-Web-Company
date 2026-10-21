using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace Modulo.Despachos.Servicios;

/// <summary>Código de barras Code128 como PNG (ZXing.Net para la matriz, SkiaSharp para la imagen).</summary>
public static class BarcodeRenderer
{
    public static byte[] Code128Png(string text, int width = 360, int height = 70)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Texto vacío.", nameof(text));
        var matrix = new Code128Writer().encode(text, BarcodeFormat.CODE_128, width, height);
        return ToPng(matrix);
    }

    private static byte[] ToPng(BitMatrix matrix)
    {
        using var bitmap = new SKBitmap(matrix.Width, matrix.Height);
        for (var x = 0; x < matrix.Width; x++)
            for (var y = 0; y < matrix.Height; y++)
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
