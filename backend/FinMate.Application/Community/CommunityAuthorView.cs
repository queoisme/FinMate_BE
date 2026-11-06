namespace FinMate.Application.Community;

/// <summary>Chỗ duy nhất quyết định một tác giả hiện ra thế nào. Mọi DTO công khai đi qua đây, để không có nhánh nào lỡ tay trả tên của bài ẩn danh.</summary>
public static class CommunityAuthorView
{
    public static CommunityAuthorDto From(bool isAnonymous, string? displayName)
        => isAnonymous || string.IsNullOrWhiteSpace(displayName)
            ? new CommunityAuthorDto(true, null)
            : new CommunityAuthorDto(false, displayName);
}
