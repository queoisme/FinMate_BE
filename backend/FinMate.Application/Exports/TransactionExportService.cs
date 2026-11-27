using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Exports;

public static class ExportErrorCodes
{
    public const string InvalidRange = "EXPORT_INVALID_RANGE";
    public const string TooLarge = "EXPORT_TOO_LARGE";
}

public interface ITransactionExportService
{
    /// <summary>CSV giao dịch ĐÃ XÁC NHẬN của chính <paramref name="userId"/> từ <paramref name="from"/> tới <paramref name="to"/> (gồm cả hai ngày, giờ Việt Nam).</summary>
    Task<string> ExportCsvAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId, string? delimiter, CancellationToken ct = default);
}

public class TransactionExportService : ITransactionExportService
{
    /// <summary>Mỗi lần đọc một trang; đọc dần để không giữ cả bảng trong bộ nhớ trước khi biết có vượt trần hay không.</summary>
    private const int PageSize = 500;

    /// <summary>Trần số dòng một tệp: vượt thì báo người dùng chọn khoảng ngắn hơn thay vì dựng tệp khổng lồ trong một request.</summary>
    public const int MaxRows = 20_000;

    private readonly ITransactionRepository _transactions;
    private readonly IFinancialAccountRepository _accounts;

    public TransactionExportService(ITransactionRepository transactions, IFinancialAccountRepository accounts)
    {
        _transactions = transactions;
        _accounts = accounts;
    }

    public async Task<string> ExportCsvAsync(Guid userId, DateOnly from, DateOnly to, Guid? accountId, string? delimiter, CancellationToken ct = default)
    {
        // default(DateOnly) = thiếu tham số (ASP.NET không báo lỗi cho tham số không bắt buộc): tránh hiểu thành "từ năm 0001".
        if (from == default || to == default || to < from)
        {
            throw new BusinessRuleException(ExportErrorCodes.InvalidRange, "Cần ngày bắt đầu và ngày kết thúc, ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
        }

        // DayRange là nửa mở [start, end); bộ lọc danh sách so sánh "<= ToDate", nên lùi một tick để 00:00 của ngày kế tiếp không lọt vào.
        var (start, exclusiveEnd) = VietnamTime.DayRange(from, to);
        var end = exclusiveEnd.AddTicks(-1);
        var rows = new List<Transaction>();
        string? cursor = null;
        do
        {
            // Luôn lọc theo userId; accountId của ví người khác chỉ cho ra danh sách rỗng, không lộ gì.
            var page = await _transactions.GetListAsync(
                new TransactionListFilter(userId, accountId, null, null, TransactionStatus.Confirmed, start, end, cursor, PageSize), ct);
            rows.AddRange(page.Items);
            if (rows.Count > MaxRows)
            {
                throw new BusinessRuleException(
                    ExportErrorCodes.TooLarge, $"Có hơn {MaxRows:N0} giao dịch trong khoảng này. Hãy chọn khoảng thời gian ngắn hơn.");
            }

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        var names = (await _accounts.GetListByUserAsync(userId, ct)).ToDictionary(a => a.Id, a => a.AccountName);
        return TransactionCsvBuilder.Build(rows, names, TransactionCsvBuilder.DelimiterOf(delimiter));
    }
}
