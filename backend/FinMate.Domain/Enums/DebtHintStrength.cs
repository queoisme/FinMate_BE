namespace FinMate.Domain.Enums;

/// <summary>
/// Độ mạnh của gợi ý trả nợ từ AI (Phase 90, S3 của Phase 86 cũ), ở <c>ai_results.debt_hint_strength</c> khi <c>relation_hint = debt_repayment</c>.
/// Gợi ý yếu ("trả lại tiền", "gửi trả") chỉ tạo gợi ý liên kết khi tên người khớp.
/// </summary>
public enum DebtHintStrength
{
    Strong,
    Weak,
}
