namespace FinMate.Application.Common.Interfaces;

/// <summary>Lưu logo ứng dụng do admin tải lên và trả đường dẫn https công khai để ghi vào <c>theme.logoUrl</c>.</summary>
public interface IBrandLogoStorage
{
    Task<string> UploadAsync(byte[] data, string contentType, string extension, CancellationToken ct = default);
}
