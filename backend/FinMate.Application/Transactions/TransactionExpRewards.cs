namespace FinMate.Application.Transactions;

/// <summary>
/// EXP thưởng cho từng loại hoạt động, gom một chỗ để 4 handler không lệch nhau và để
/// <c>DeleteTransactionCommandHandler</c> trừ lại đúng số đã cộng.
/// </summary>
internal static class TransactionExpRewards
{
    internal const int ConfirmTransaction = 10;
    internal const int CreateManualTransaction = 5;
    internal const int ContributeToGoal = 20;
}
