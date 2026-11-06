using FinMate.Application.Community;
using SkiaSharp;

namespace FinMate.Infrastructure.Community;

/// <summary>
/// Giải mã rồi vẽ lại thành JPEG, nhờ đó EXIF (trong đó có GPS) và mọi thứ nhúng trong tệp gốc đều mất. Chiều xoay trong EXIF được áp vào điểm ảnh trước,
/// nếu không ảnh chụp dọc từ điện thoại sẽ nằm ngang.
/// </summary>
public sealed class SkiaCommunityImageProcessor : ICommunityImageProcessor
{
    private const int MaxPixels = 40_000_000;
    private const int MaxLongEdge = 1600;
    private const int JpegQuality = 80;

    // Ảnh 40 MP giải mã tốn khoảng 160 MB: giới hạn số lượt cùng lúc để vài lượt tải lên không làm cạn bộ nhớ của cả backend.
    private static readonly SemaphoreSlim Gate = new(2, 2);

    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] RiffMagic = "RIFF"u8.ToArray();
    private static readonly byte[] WebpMagic = "WEBP"u8.ToArray();

    public async Task<CommunityProcessedImage?> ProcessAsync(byte[] content, CancellationToken ct = default)
    {
        if (!HasAllowedSignature(content))
        {
            return null;
        }

        await Gate.WaitAsync(ct);
        try
        {
            return await Task.Run(() => Render(content), ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool HasAllowedSignature(byte[] content)
        => content.AsSpan().StartsWith(JpegMagic)
            || content.AsSpan().StartsWith(PngMagic)
            || (content.Length > 12 && content.AsSpan().StartsWith(RiffMagic) && content.AsSpan(8).StartsWith(WebpMagic));

    private static CommunityProcessedImage? Render(byte[] content)
    {
        using var data = SKData.CreateCopy(content);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            return null;
        }

        // Kiểm tra kích thước trên header ở trên, chỉ giải mã sau đó: tệp nhỏ vẫn có thể bung ra hàng GB.
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return null;
        }

        return Draw(decoded, codec.EncodedOrigin);
    }

    private static CommunityProcessedImage? Draw(SKBitmap source, SKEncodedOrigin origin)
    {
        var swapped = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var uprightWidth = swapped ? source.Height : source.Width;
        var uprightHeight = swapped ? source.Width : source.Height;
        var scale = Math.Min(1f, (float)MaxLongEdge / Math.Max(uprightWidth, uprightHeight));
        var width = Math.Max(1, (int)Math.Round(uprightWidth * scale));
        var height = Math.Max(1, (int)Math.Round(uprightHeight * scale));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface is null)
        {
            return null;
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);  // JPEG không có nền trong suốt.
        canvas.Scale(scale);
        ApplyOrigin(canvas, origin, source.Width, source.Height);
        using (var image = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return encoded is null ? null : new CommunityProcessedImage(encoded.ToArray(), width, height);
    }

    /// <summary>Viết thẳng phép biến đổi (x, y) của ảnh gốc sang ảnh đứng, theo bảng chiều xoay của EXIF, vì ghép nhiều phép xoay và lật rất dễ sai.</summary>
    private static void ApplyOrigin(SKCanvas canvas, SKEncodedOrigin origin, int width, int height)
    {
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, width, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, width, 0, -1, height),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, height),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, height, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, height, -1, 0, width),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, width),
            _ => SKMatrix.Identity,
        };
        canvas.Concat(in matrix);
    }

    private static SKMatrix Matrix(float scaleX, float skewX, float transX, float skewY, float scaleY, float transY)
        => new(scaleX, skewX, transX, skewY, scaleY, transY, 0, 0, 1);
}
