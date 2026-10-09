namespace FinMate.Application.Common.Interfaces;

/// <summary>Ảnh hóa đơn gửi sang OCR service. Ảnh chỉ đi qua bộ nhớ, không được lưu ở đâu.</summary>
public record OcrServiceRequest(Guid UserId, byte[] Image, string FileName, string ContentType);

/// <param name="Readable">False khi ảnh không ra chữ nào hoặc không giống hóa đơn (người dùng cần chụp lại).</param>
/// <param name="TotalAmount">Tổng tiền (đồng VND); null khi đọc được chữ nhưng không thấy tổng tiền.</param>
/// <param name="MerchantName">Tên OCR đọc ra, dùng để nhận diện thương hiệu.</param>
/// <param name="MerchantDisplay">Tên đã làm đẹp để hiển thị.</param>
/// <param name="Lines">Chữ ĐÃ lọc dữ liệu cá nhân (điện thoại, email, tên khách, tài khoản). Chữ gốc không bao giờ rời OCR service.</param>
/// <param name="Category">Nhóm (9 nhóm của bộ luật bill) OCR service tự đoán. Chỉ để tham khảo: danh mục do AI Service quyết.</param>
/// <param name="CategorySource">Nhận ra nhờ đâu (memory | brand | keyword | product | none).</param>
/// <param name="NeedsReview">OCR service tự thấy chưa chắc (ảnh mờ, số không khớp nhau...).</param>
/// <param name="TotalSource">"text" khi tổng tiền đọc được từ dòng có nhãn tổng; "items" khi chỉ cộng từ các dòng hàng.</param>
public record OcrServiceResponse(
    bool Readable,
    long? TotalAmount,
    string? MerchantName,
    string? MerchantDisplay,
    IReadOnlyList<string> Lines,
    string? Category,
    string? CategorySource,
    bool NeedsReview,
    DateTimeOffset? TransactedAt,
    string? TotalSource);

public interface IOcrServiceClient
{
    /// <summary>False khi OCR_SERVICE_URL chưa cấu hình: backend quay về <c>POST /api/v1/ocr</c> của AI Service.</summary>
    bool IsConfigured { get; }

    /// <summary>Ném <see cref="Exceptions.AIServiceUnavailableException"/> khi OCR service không phản hồi.</summary>
    Task<OcrServiceResponse> AnalyzeAsync(OcrServiceRequest request, CancellationToken ct = default);
}
