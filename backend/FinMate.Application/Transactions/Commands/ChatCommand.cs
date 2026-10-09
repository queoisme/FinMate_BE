namespace FinMate.Application.Transactions.Commands;

/// <param name="FinancialAccountId">Ví để ghi khi AI đủ chắc. Bỏ trống thì dùng ví duy nhất của người dùng, nếu có nhiều ví mà không chọn thì không tự ghi.</param>
/// <param name="ClientRequestId">Chống ghi trùng khi gửi lại, giống <c>POST /transactions</c>.</param>
public record ChatCommand(Guid UserId, string Text, Guid? FinancialAccountId = null, Guid? ClientRequestId = null);
