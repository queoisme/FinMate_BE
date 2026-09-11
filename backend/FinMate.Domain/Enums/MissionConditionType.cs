namespace FinMate.Domain.Enums;

/// <summary>
/// Điều kiện hoàn thành mission. Dùng enum + <c>condition_target</c> thay vì một DSL bằng
/// JSON: bộ điều kiện là hữu hạn và biết trước, nên kiểu dữ liệu có tên vừa dịch được sang
/// SQL vừa test được, còn DSL thì phải tự viết parser và mất hết kiểm tra lúc biên dịch.
/// </summary>
public enum MissionConditionType
{
    ConfirmTransaction,
    CreateManualTransaction,
    ContributeToGoal,
    StayUnderBudget,
    LoginStreak,
}
