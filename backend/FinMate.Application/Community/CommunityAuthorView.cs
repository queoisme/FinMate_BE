namespace FinMate.Application.Community;

/// <summary>
/// Chỗ duy nhất quyết định một tác giả hiện ra thế nào. Mọi DTO công khai đi qua đây, để không có nhánh nào lỡ tay trả tên của bài ẩn danh.
/// Tên được đọc trực tiếp từ tài khoản nên kiểm tra lại ở đây: đổi tên thành số điện thoại sau khi đăng không được làm số đó hiện công khai.
/// </summary>
public static class CommunityAuthorView
{
    public static CommunityAuthorDto From(bool isAnonymous, string? displayName)
        => isAnonymous || !CommunityContentPolicy.IsDisplayNameAllowed(displayName)
            ? new CommunityAuthorDto(true, null)
            : new CommunityAuthorDto(false, displayName);
}
