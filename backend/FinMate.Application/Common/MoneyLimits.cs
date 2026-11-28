namespace FinMate.Application.Common;

/// <summary>
/// Trần cho mọi số tiền người dùng nhập (đơn vị đồng, lưu trong cột <c>*_cents</c>): 100 tỷ đồng, cùng mức đã dùng cho thu nhập hồ sơ,
/// dự báo, khoản định kỳ, khoản vay và ngân sách nhóm. Không có trần thì vài khoản cực lớn cộng dồn là tràn <c>bigint</c>
/// và làm sai số dư, dự báo, báo cáo của chính người đó (pentest 2026-11-28, #18).
/// </summary>
public static class MoneyLimits
{
    public const long MaxAmountCents = 100_000_000_000L;

    public const string TooLargeMessage = "Số tiền tối đa là 100 tỷ đồng.";
}
