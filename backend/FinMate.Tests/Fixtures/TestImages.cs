using System.Text;
using SkiaSharp;

namespace FinMate.Tests;

/// <summary>Ảnh dựng trong bộ nhớ, gồm cả JPEG có EXIF giống ảnh chụp từ điện thoại.</summary>
internal static class TestImages
{
    private const string GpsMarker = "GPS-MARKER-10.7769-106.7009";

    public static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.CornflowerBlue);

        // Góc trên trái màu đỏ, để kiểm tra được ảnh đã xoay đúng hướng.
        for (var x = 0; x < width / 4; x++)
        {
            for (var y = 0; y < height / 4; y++)
            {
                bitmap.SetPixel(x, y, SKColors.Red);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>Chèn đoạn EXIF ngay sau SOI, có Orientation và một đoạn chữ đại diện cho tọa độ GPS.</summary>
    public static byte[] JpegWithGps(int width, int height, ushort orientation = 1)
    {
        var jpeg = Jpeg(width, height);
        var marker = Encoding.ASCII.GetBytes(GpsMarker);
        using var tiff = new MemoryStream();
        using (var writer = new BinaryWriter(tiff, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("II"u8.ToArray());
            writer.Write((ushort)42);
            writer.Write(8u);
            writer.Write((ushort)2);
            WriteEntry(writer, 0x0112, 3, 1, orientation);
            WriteEntry(writer, 0x010E, 2, (uint)marker.Length, 38);
            writer.Write(0u);
            writer.Write(marker);
        }

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff.ToArray()).ToArray();
        var length = payload.Length + 2;
        var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(payload);
        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }

    private static void WriteEntry(BinaryWriter writer, ushort tag, ushort type, uint count, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(value);
    }

    public static byte[] HeicHeader()
        => new byte[] { 0, 0, 0, 24 }.Concat("ftypheic"u8.ToArray()).Concat(new byte[64]).ToArray();

    /// <summary>Tệp PNG có header ghi kích thước rất lớn mà không có dữ liệu ảnh, kiểu tệp dùng để làm cạn bộ nhớ khi giải mã.</summary>
    public static byte[] PngClaiming(int width, int height)
    {
        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new List<byte>("IHDR"u8.ToArray());
        ihdr.AddRange(BigEndian(width));
        ihdr.AddRange(BigEndian(height));
        ihdr.AddRange(new byte[] { 8, 6, 0, 0, 0 });
        var chunk = BigEndian(13).Concat(ihdr).Concat(BigEndian((int)Crc32(ihdr.ToArray())));
        return signature.Concat(chunk).ToArray();
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    public static bool ContainsGpsMarker(byte[] bytes) => Encoding.ASCII.GetString(bytes).Contains(GpsMarker, StringComparison.Ordinal);

    public static bool HasExifSegment(byte[] bytes) => Encoding.ASCII.GetString(bytes).Contains("Exif\0\0", StringComparison.Ordinal);

    public static bool IsDecodableJpeg(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        return bitmap is not null && bytes[0] == 0xFF && bytes[1] == 0xD8;
    }

    /// <summary>Ảnh mẫu toàn màu xanh trên nền trắng: giữa ảnh còn xanh nghĩa là hình vẫn nằm trong khung.</summary>
    public static bool CenterIsBlue(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
        return center.Blue > center.Red + 50;
    }

    /// <summary>Góc nào đang có màu đỏ: 0 trên trái, 1 trên phải, 2 dưới phải, 3 dưới trái.</summary>
    public static int RedCorner(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        var (w, h) = (bitmap.Width - 3, bitmap.Height - 3);
        var corners = new[] { bitmap.GetPixel(2, 2), bitmap.GetPixel(w, 2), bitmap.GetPixel(w, h), bitmap.GetPixel(2, h) };
        return Array.FindIndex(corners, c => c.Red > c.Blue + 50);
    }

    public static (int Width, int Height) SizeOf(byte[] bytes)
    {
        using var bitmap = SKBitmap.Decode(bytes);
        return (bitmap.Width, bitmap.Height);
    }
}
