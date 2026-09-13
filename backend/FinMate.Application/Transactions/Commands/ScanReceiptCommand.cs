namespace FinMate.Application.Transactions.Commands;

/// <param name="Image">
/// Nội dung ảnh. Đi qua Application dạng mảng byte chứ không phải <c>IFormFile</c>: kiểu đó
/// thuộc ASP.NET Core, mà Application không được biết tới tầng web (ARCHITECTURE.md §2.3).
/// </param>
public record ScanReceiptCommand(
    Guid UserId,
    byte[] Image,
    string FileName,
    string ContentType);
