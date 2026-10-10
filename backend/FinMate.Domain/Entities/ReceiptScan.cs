namespace FinMate.Domain.Entities;

/// <summary>Giá trị của <see cref="ReceiptScan.OcrResult"/>; khớp đúng <c>ScannedReceiptDto.OcrResult</c> trả cho app.</summary>
public static class ReceiptScanResults
{
    public const string Success = "success";

    /// <summary>Đọc được chữ nhưng không thấy tổng tiền.</summary>
    public const string NoAmount = "no_amount";

    /// <summary>Ảnh không ra chữ nào hoặc không giống hóa đơn.</summary>
    public const string Unreadable = "unreadable";
}

/// <summary>
/// Kết quả BÓC TÁCH của một lần quét hóa đơn: những gì đã đọc ra (tổng tiền, cửa hàng, ngày) và danh mục AI đoán.
///
/// KHÔNG lưu ảnh, KHÔNG lưu chữ OCR (kể cả bản đã lọc): chỉ các trường đã bóc tách. Muốn xem lại chữ để gỡ lỗi là một quyết định
/// riêng về dữ liệu cá nhân, không phải hệ quả phụ của việc ghi nhật ký quét.
///
/// Khác với <see cref="Transaction"/>: bản ghi này ra đời ngay khi quét, TRƯỚC khi người dùng xem và bấm Lưu, và một lần quét có
/// thể không bao giờ thành giao dịch. Vì vậy nó không động tới số dư, ngân sách hay gamification.
/// </summary>
public class ReceiptScan
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Một trong <see cref="ReceiptScanResults"/>. Các trường bên dưới null khi không phải <c>success</c>.</summary>
    public string OcrResult { get; set; } = ReceiptScanResults.Unreadable;

    /// <summary>Tổng tiền (đồng VND, BIGINT như mọi nơi khác).</summary>
    public long? AmountCents { get; set; }

    public string? MerchantName { get; set; }
    public DateTimeOffset? TransactedAt { get; set; }

    /// <summary>Slug hệ thống AI đoán; null khi AI không nhận ra hoặc không chạy được (không ghi "other" cho có).</summary>
    public string? CategorySlug { get; set; }

    /// <summary>Nhận ra nhờ đâu: memory | brand | keyword | product | model | none, hoặc <see cref="ItemsCategorySource"/> khi bill được tách theo món. Null khi đi đường cũ (không biết).</summary>
    public string? CategorySource { get; set; }

    /// <summary>Nguồn ghi cho lần quét đã tách theo món; IsSplit dựa vào nó khi lưu món hỏng.</summary>
    public const string ItemsCategorySource = "items";

    public double? Confidence { get; set; }

    /// <summary>OCR service tự thấy chưa chắc (ảnh mờ, số không khớp nhau...).</summary>
    public bool NeedsReview { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public User? User { get; set; }
}
