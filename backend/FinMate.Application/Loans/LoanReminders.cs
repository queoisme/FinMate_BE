using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>Một lần nhắc khoản vay. <paramref name="Phase"/>: <c>before</c> (sắp đến hạn) hoặc <c>overdue</c>.</summary>
public sealed record LoanReminder(Guid LoanId, string Phase, string DedupKey);

/// <summary>
/// Lịch nhắc khoản vay (Phase 90, BR-DEBT-14, D25 của Phase 86 cũ). Hàm thuần. Chỉ khoản <c>open</c> có hạn và còn gốc.
/// <list type="bullet">
/// <item>Trước hạn: khi còn 0..<c>reminderDaysBefore</c> ngày, một lần (khoá theo hạn, nên job lỡ một ngày vẫn bắt kịp).</item>
/// <item>Quá hạn: lần thứ n (n = 0, 1, …) từ ngày <c>1 + n × overdueRepeatDays</c> sau hạn, tối đa <c>maxOverdueReminders</c> lần.</item>
/// </list>
/// Hạn nằm trong khoá: đổi hạn là chu kỳ nhắc mới. Mỗi lần chạy trả nhiều nhất một lần nhắc cho một khoản.
/// </summary>
public static class LoanReminderSchedule
{
    public const string PhaseBefore = "before";
    public const string PhaseOverdue = "overdue";
    private const string KeyPrefix = "loan_due";

    public static LoanReminder? For(Loan loan, DateOnly today, LoanSettings settings)
    {
        if (loan.Status != LoanStatus.Open || loan.DeletedAt is not null || loan.DueDate is not { } due || loan.PrincipalCents - loan.RepaidCents <= 0)
        {
            return null;
        }

        var daysLeft = due.DayNumber - today.DayNumber;
        if (daysLeft >= 0)
        {
            return daysLeft <= settings.ReminderDaysBefore ? new LoanReminder(loan.Id, PhaseBefore, $"{Prefix(loan.Id, due)}:{PhaseBefore}") : null;
        }

        var round = (-daysLeft - 1) / settings.OverdueRepeatDays;
        return round < settings.MaxOverdueReminders ? new LoanReminder(loan.Id, PhaseOverdue, $"{Prefix(loan.Id, due)}:{PhaseOverdue}:{round}") : null;
    }

    /// <summary>Đầu khoá của mọi lần nhắc cho khoản này với hạn này.</summary>
    public static string Prefix(Guid loanId, DateOnly due) => $"{KeyPrefix}:{loanId}:{due:yyyy-MM-dd}";
}

/// <summary>
/// Q3: chữ của push nhắc vay là câu chung, KHÔNG có tên người, tiêu đề khoản (thường chứa tên) hay số tiền: đi qua FCM, hiện trên màn hình khoá.
/// <c>forecast_alerts.body</c> chính là nội dung push nên cũng là câu chung.
/// </summary>
public static class LoanReminderText
{
    public const string Title = "Sổ vay";

    public static string Body(string phase) => phase == LoanReminderSchedule.PhaseBefore
        ? "Bạn có một khoản nợ sắp đến hạn"
        : "Bạn có một khoản nợ đã quá hạn";
}

public interface ILoanReminderService
{
    /// <summary>Số lần nhắc đã tạo cho user này (đã trừ lần trùng, user tắt).</summary>
    Task<int> RunForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class LoanReminderService(ILoanRepository loans, IForecastAlertDispatcher dispatcher, ISystemConfigProvider config) : ILoanReminderService
{
    public async Task<int> RunForUserAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        var settings = (await config.GetAsync(ct)).Document.Loans;
        var today = VietnamTime.DateOf(now);
        var raised = 0;
        foreach (var loan in await loans.ListOpenAsync(userId, ct))
        {
            if (LoanReminderSchedule.For(loan, today, settings) is not { } reminder)
            {
                continue;
            }

            var data = new Dictionary<string, string> { ["loanId"] = loan.Id.ToString(), ["phase"] = reminder.Phase };
            var alert = await dispatcher.RaiseAsync(
                new ForecastAlertRequest(
                    userId, ForecastAlertKind.LoanDue, reminder.DedupKey, LoanReminderText.Title, LoanReminderText.Body(reminder.Phase), data, HonorQuietHours: true),
                ct);
            raised += alert is null ? 0 : 1;
        }

        return raised;
    }
}

/// <summary>
/// D25: nhắc vay bị hoãn (giờ yên lặng) mà trong lúc chờ khoản đã trả xong, đã xoá nợ, đã xoá hay đã đổi hạn thì không gửi nữa. Loại cảnh báo khác
/// luôn gửi.
/// </summary>
public sealed class LoanReminderGate(ILoanRepository loans) : IForecastAlertGate
{
    public async Task<bool> StillRelevantAsync(ForecastAlert alert, IReadOnlyDictionary<string, string> data, CancellationToken ct = default)
    {
        if (alert.Kind != ForecastAlertKind.LoanDue)
        {
            return true;
        }

        if (!data.TryGetValue("loanId", out var raw) || !Guid.TryParse(raw, out var loanId))
        {
            return false;
        }

        var loan = await loans.GetOwnedAsync(loanId, alert.UserId, ct);
        return loan is { Status: LoanStatus.Open, DueDate: { } due }
            && alert.DedupKey.StartsWith(LoanReminderSchedule.Prefix(loan.Id, due) + ":", StringComparison.Ordinal);
    }
}
