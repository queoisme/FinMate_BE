using System.Globalization;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;
using FluentValidation;
using FluentValidation.Results;

namespace FinMate.Application.DataExport;

/// <param name="Status"><c>Confirmed</c> (mặc định) | <c>Draft</c> | <c>All</c>.</param>
/// <param name="Delimiter"><c>comma</c> (mặc định) | <c>semicolon</c> (S1).</param>
/// <param name="IncludeDeleted">Mặc định không. Bật thì thêm giao dịch đã xoá/đã gộp và cột "Đã xoá lúc"; dòng đã xoá có "Số tiền tính vào chi tiêu" = 0.</param>
public sealed record TransactionCsvRequest(
    DateOnly? From, DateOnly? To, Guid? AccountId, Guid? CategoryId, string? Status, string? Delimiter, bool IncludeDeleted = false);

/// <summary>Kết quả kiểm tra xong, sẵn sàng ghi file.</summary>
public sealed record TransactionCsvPlan(TransactionExportFilter Filter, CsvDelimiter Delimiter, string FileName);

/// <summary>
/// <c>GET /exports/transactions.csv</c> (Phase 91, BR-EXP-01…05, S1, S3). Tách hai bước: <see cref="PrepareAsync"/> kiểm mọi thứ có thể trả lỗi JSON
/// (cờ, tham số, quyền sở hữu, giới hạn) trước khi gửi byte nào; <see cref="WriteAsync"/> chỉ ghi file theo luồng. Không ghi log nội dung, số tiền hay bộ lọc.
/// Không bị chặn bởi gói trả phí (BR-EXP-13).
/// </summary>
public sealed class TransactionCsvExportService(
    IDataExportRepository repository,
    IFinancialAccountRepository accounts,
    ICategoryRepository categories,
    ISystemConfigProvider config,
    CsvExportRateLimiter limiter,
    IAuditLogService audit,
    TimeProvider clock)
{
    public async Task<TransactionCsvPlan> PrepareAsync(Guid userId, TransactionCsvRequest request, string? ip, CancellationToken ct = default)
    {
        var document = (await config.GetAsync(ct)).Document;
        EnsureEnabled(document);
        var settings = document.DataExport;
        var delimiter = ParseDelimiter(request.Delimiter);
        var statuses = ParseStatuses(request.Status);
        var (from, to) = ResolveRange(request.From, request.To, settings.MaxRangeDays);

        await limiter.EnsureAllowedAsync(userId, settings.CsvPerHour, ct);
        await EnsureOwnedAsync(userId, request.AccountId, request.CategoryId, ct);

        var (start, end) = VietnamTime.DayRange(from, to);
        var filter = new TransactionExportFilter(start, end, request.AccountId, request.CategoryId, statuses, IncludeDeleted: request.IncludeDeleted);
        if (await repository.CountTransactionsAsync(userId, filter, ct) > settings.MaxRows)
        {
            throw new BusinessRuleException(DataExportErrorCodes.RangeTooLarge,
                $"Khoảng này có hơn {Thousands(settings.MaxRows)} giao dịch. Vui lòng chọn khoảng ngày ngắn hơn.");
        }

        await limiter.RecordAsync(userId, ct);
        await audit.LogAsync(AuditEvents.DataExportTransactionsCsv, userId, ip, ct: ct);
        return new TransactionCsvPlan(filter, delimiter, $"finmate-giao-dich-{Day(from)}-{Day(to)}.csv");
    }

    public async Task WriteAsync(Guid userId, TransactionCsvPlan plan, Stream output, CancellationToken ct = default)
    {
        await using var writer = new CsvWriter(output, plan.Delimiter);
        var withDeleted = plan.Filter.IncludeDeleted;
        await writer.WriteRowAsync(TransactionCsvFormat.HeaderCells(deletedFile: withDeleted), ct);
        await foreach (var row in repository.StreamTransactionsAsync(userId, plan.Filter, ct))
        {
            await writer.WriteRowAsync(TransactionCsvFormat.ToCells(row, deletedFile: withDeleted), ct);
        }
    }

    public static void EnsureEnabled(SystemConfigDocument document)
    {
        if (!document.Features.DataExport)
        {
            throw new BusinessRuleException(DataExportErrorCodes.Disabled, "Tính năng xuất dữ liệu đang tạm tắt. Vui lòng thử lại sau.");
        }
    }

    private (DateOnly From, DateOnly To) ResolveRange(DateOnly? from, DateOnly? to, int maxRangeDays)
    {
        var today = VietnamTime.DateOf(clock.GetUtcNow());
        var start = from ?? (to is { } end ? new DateOnly(end.Year, end.Month, 1) : new DateOnly(today.Year, today.Month, 1));
        var last = to ?? new DateOnly(start.Year, start.Month, 1).AddMonths(1).AddDays(-1);
        if (last < start)
        {
            throw Invalid("to", "Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
        }

        if (last.DayNumber - start.DayNumber + 1 > maxRangeDays)
        {
            throw new BusinessRuleException(DataExportErrorCodes.RangeTooLarge, $"Mỗi lần xuất tối đa {maxRangeDays} ngày. Vui lòng chọn khoảng ngắn hơn.");
        }

        return (start, last);
    }

    private async Task EnsureOwnedAsync(Guid userId, Guid? accountId, Guid? categoryId, CancellationToken ct)
    {
        if (accountId is { } account && await accounts.GetByIdAsync(account, userId, ct) is null)
        {
            throw new NotFoundException("FinancialAccount", account);
        }

        if (categoryId is { } category)
        {
            // Danh mục hệ thống (UserId null) ai cũng dùng được; danh mục riêng của người khác coi như không tồn tại.
            var found = await categories.GetByIdAsync(category, ct);
            if (found is null || (found.UserId is { } owner && owner != userId))
            {
                throw new NotFoundException("Category", category);
            }
        }
    }

    private static CsvDelimiter ParseDelimiter(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "comma" => CsvDelimiter.Comma,
        "semicolon" => CsvDelimiter.Semicolon,
        _ => throw Invalid("delimiter", "Dấu phân cách phải là comma hoặc semicolon."),
    };

    private static IReadOnlyCollection<TransactionStatus> ParseStatuses(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "confirmed" => [TransactionStatus.Confirmed],
        "draft" => [TransactionStatus.Draft],
        "all" => [TransactionStatus.Confirmed, TransactionStatus.Draft],
        _ => throw Invalid("status", "Trạng thái phải là Confirmed, Draft hoặc All."),
    };

    private static ValidationException Invalid(string field, string message) => new([new ValidationFailure(field, message)]);

    /// <summary>50000 → "50.000" (dự án bật InvariantGlobalization nên không có culture vi-VN).</summary>
    private static string Thousands(int value) => value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');

    private static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
