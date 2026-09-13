namespace FinMate.Application.Auth.Commands;

/// <param name="MonthlyIncomeCents">
/// Thu nhập hằng tháng dự kiến (docx Bước 1.3). <c>null</c> = giữ nguyên giá trị hiện có;
/// gửi <c>0</c> để xoá. Phân biệt được hai ý đó vì "chưa khai" và "khai là 0" dẫn tới hai
/// hành vi khác nhau ở cảnh báo bội chi.
/// </param>
public record UpdateUserProfileCommand(
    Guid UserId,
    string DisplayName,
    long? MonthlyIncomeCents = null);
