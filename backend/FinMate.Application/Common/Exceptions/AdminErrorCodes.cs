namespace FinMate.Application.Common.Exceptions;

public static class AdminErrorCodes
{
    /// <summary>Admin tự khoá chính mình — khoá luôn người duy nhất mở khoá được.</summary>
    public const string CannotLockSelf = "ADMIN_CANNOT_LOCK_SELF";

    /// <summary>Admin tự đổi vai trò của chính mình — tự hạ quyền là tự khoá mình ra ngoài.</summary>
    public const string CannotChangeOwnRole = "ADMIN_CANNOT_CHANGE_OWN_ROLE";

    /// <summary>
    /// Hạ quyền admin hoạt động CUỐI CÙNG. Không còn ai quản trị được thì đường cứu duy nhất
    /// là sửa biến môi trường rồi khởi động lại service.
    /// </summary>
    public const string CannotDemoteLastAdmin = "ADMIN_CANNOT_DEMOTE_LAST_ADMIN";

    /// <summary>Thăng quyền cho một tài khoản đang chờ xoá cứng.</summary>
    public const string CannotPromoteDeletedUser = "ADMIN_CANNOT_PROMOTE_DELETED_USER";

    /// <summary>Huỷ một yêu cầu xoá đã chạy xong — dữ liệu không còn để khôi phục.</summary>
    public const string DeletionAlreadyProcessed = "ADMIN_DELETION_ALREADY_PROCESSED";

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
