namespace FinMate.Application.Common.Exceptions;

public static class AdminErrorCodes
{
    /// <summary>Admin tự khoá chính mình — khoá luôn người duy nhất mở khoá được.</summary>
    public const string CannotLockSelf = "ADMIN_CANNOT_LOCK_SELF";

    /// <summary>
    /// Cố sửa một khoá định danh bất biến (<c>provider_key</c>, <c>slug</c>, <c>code</c>).
    /// Chúng là thứ hệ thống khác join vào, đổi là làm hỏng liên kết một cách im lặng.
    /// </summary>
    public const string ImmutableField = "ADMIN_IMMUTABLE_FIELD";

    public const string ProviderKeyDuplicate = "ADMIN_PROVIDER_KEY_DUPLICATE";
    public const string PackageNameDuplicate = "ADMIN_PACKAGE_NAME_DUPLICATE";
    public const string SystemCategorySlugDuplicate = "ADMIN_SYSTEM_CATEGORY_SLUG_DUPLICATE";
    public const string MissionCodeDuplicate = "ADMIN_MISSION_CODE_DUPLICATE";
}
