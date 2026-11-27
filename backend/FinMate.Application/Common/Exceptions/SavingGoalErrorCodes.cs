namespace FinMate.Application.Common.Exceptions;

public static class SavingGoalErrorCodes
{
    public const string NotActive = "SAVING_GOAL_NOT_ACTIVE";

    /// <summary>Chỉ kích hoạt được mục tiêu đang ở trạng thái nháp.</summary>
    public const string NotDraft = "SAVING_GOAL_NOT_DRAFT";

    /// <summary>Số đã có sẵn ban đầu phải từ 0 và nhỏ hơn số tiền mục tiêu (bằng hoặc hơn thì mục tiêu đã xong).</summary>
    /// <summary>Hạn của bản nháp đã qua khi kích hoạt.</summary>
    public const string DeadlinePassed = "SAVING_GOAL_DEADLINE_PASSED";

    public const string InitialSavedInvalid = "SAVING_GOAL_INITIAL_SAVED_INVALID";

    /// <summary>Số góp lớn hơn phần còn lại của mục tiêu (mục tiêu - đã để dành).</summary>
    public const string ContributionExceedsRemaining = "SAVING_GOAL_CONTRIBUTION_EXCEEDS_REMAINING";
}
