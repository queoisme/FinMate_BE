namespace FinMate.Application.Common.Exceptions;

public static class GroupErrorCodes
{
    public const string TooManyGroups = "GROUP_TOO_MANY_GROUPS";
    public const string TooManyMembers = "GROUP_TOO_MANY_MEMBERS";
    public const string TooManyInvitations = "GROUP_TOO_MANY_INVITATIONS";
    public const string AlreadyMember = "GROUP_ALREADY_MEMBER";

    /// <summary>Hai thao tác đồng thời cùng đổi thành viên; không có gì được lưu.</summary>
    public const string MembershipConflict = "GROUP_MEMBERSHIP_CONFLICT";

    public const string OwnerMustTransfer = "GROUP_OWNER_MUST_TRANSFER";
    public const string CannotChangeSelf = "GROUP_CANNOT_CHANGE_SELF";
    public const string InvalidOwnerAssignment = "GROUP_INVALID_OWNER_ASSIGNMENT";

    /// <summary>Mã mời sai, hết hạn, đã dùng hoặc không dành cho tài khoản này. Cố ý chỉ một mã: không tiết lộ lý do cho người cầm mã đoán.</summary>
    public const string InvitationInvalid = "GROUP_INVITATION_INVALID";
}
