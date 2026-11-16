using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.Transactions.Transfers;

/// <summary>
/// S3 (D14): một chiều vừa chuyển thành Transfer từng là lần trả của một khoản định kỳ đang hoạt động (cùng chiều tiền, cùng nơi nhận, đúng lần
/// <c>last_seen_at</c>). Nếu Transfer nằm TRỌN trong các ví được tính vào dự báo, tiền không rời tập đó nữa, nên hỏi người dùng "còn dùng không?"
/// qua luồng FR-FCST-12 (cảnh báo <c>bill_status_check</c>). KHÔNG đổi trạng thái, không huỷ: người dùng trả lời bằng resume/DELETE có sẵn.
/// Transfer vượt ranh giới tập ví được tính thì giữ khớp (nó vẫn là tiền ra khỏi tập đó). Không bao giờ ném.
/// </summary>
public sealed class TransferRecurringCheck : ITransferRecurringCheck
{
    private readonly IRecurringItemRepository _items;
    private readonly IFinancialAccountRepository _accounts;
    private readonly IForecastAlertDispatcher _alerts;
    private readonly ILogger<TransferRecurringCheck> _logger;

    public TransferRecurringCheck(
        IRecurringItemRepository items, IFinancialAccountRepository accounts, IForecastAlertDispatcher alerts, ILogger<TransferRecurringCheck> logger)
    {
        _items = items;
        _accounts = accounts;
        _alerts = alerts;
        _logger = logger;
    }

    public async Task AfterConversionAsync(Guid userId, Transaction transfer, IReadOnlyList<SnapshotRow> before, CancellationToken ct = default)
    {
        try
        {
            var counted = (await _accounts.GetListByUserAsync(userId, ct)).Where(a => a.IncludeInForecast).Select(a => a.Id).ToHashSet();
            if (transfer.CounterAccountId is not { } to || !counted.Contains(transfer.FinancialAccountId) || !counted.Contains(to))
            {
                return;   // vượt ranh giới: giữ khớp như cũ
            }

            var active = await _items.ListAsync(userId, RecurringStatus.Active, ct);
            foreach (var item in active.Where(i => before.Any(row => WasPaymentOf(row, i))))
            {
                await _alerts.RaiseAsync(new ForecastAlertRequest(
                    userId,
                    ForecastAlertKind.BillStatusCheck,
                    $"bill_status_check:{item.Id}:transfer:{transfer.Id}",
                    $"{item.Name} có còn dùng không?",
                    $"Lần trả gần nhất của khoản {ForecastText.Money(item.AmountCents)} giờ là chuyển tiền giữa các ví của bạn. Bạn còn dùng khoản này không?",
                    new Dictionary<string, string> { ["recurringItemId"] = item.Id.ToString(), ["transactionId"] = transfer.Id.ToString() }), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Việc sau khi lưu: không làm hỏng một lượt chuyển đổi đã thành công. Chỉ ghi loại lỗi.
            _logger.LogWarning("Transfer recurring check failed: {ExceptionType}", ex.GetType().Name);
        }
    }

    private static bool WasPaymentOf(SnapshotRow row, RecurringItem item)
    {
        var direction = row.Type == nameof(TransactionType.Debit) ? RecurringDirection.Outflow
            : row.Type == nameof(TransactionType.Credit) ? RecurringDirection.Inflow
            : (RecurringDirection?)null;
        return direction == item.Direction
               && item.CounterpartyKey is not null
               && CounterpartyKey.From(row.MerchantName) == item.CounterpartyKey
               && item.LastSeenAt == row.TransactedAt;
    }
}
