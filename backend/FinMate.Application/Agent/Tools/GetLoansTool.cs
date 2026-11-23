using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Domain.Entities;

namespace FinMate.Application.Agent;

/// <summary>
/// Công cụ <c>get_loans</c> (Phase 90, BR-DEBT-18; chép từ <c>get_debts</c> của Phase 86 cũ): chỉ đọc. Tổng tôi đang nợ, tổng người khác nợ tôi, số khoản
/// quá hạn, và tối đa 10 khoản (quá hạn và hạn gần trước). Số là gốc còn lại, không lãi (Q11). KHÔNG có tên người hay tiêu đề khoản (D14; tiêu đề thường
/// chứa tên): kết quả công cụ đi tới LLM bên ngoài, tên là dữ liệu của người chưa đồng ý điều khoản nào. Không có công cụ ghi.
/// </summary>
public sealed class GetLoansTool(IFinancialDataSource source) : IAgentTool
{
    private const int MaxRows = 10;

    public ToolSpec Spec { get; } = new(
        "get_loans",
        "Sổ vay của người dùng: tổng đang nợ người khác, tổng người khác nợ, số khoản quá hạn, và tối đa 10 khoản (chiều, số còn lại, hạn). Không có tên người.",
        []);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var debts = await source.GetOpenLoansAsync(context.UserId, ct);
        if (debts.Count == 0)
        {
            return ToolResult.Insufficient("no_open_loans");
        }

        var now = context.Now;
        var today = VietnamTime.DateOf(now);
        Evidence Item(string id, long? value, string text, string unit = "VND")
            => new(id, id, value, text, unit, EvidenceSource.UserEntered, now, ConfidenceCalculator.Compute(EvidenceSource.UserEntered, now, now, 1.0));

        var overdue = debts.Where(d => d.DueDate is { } due && due < today).ToList();
        var evidence = new List<Evidence>
        {
            Item("loans_i_owe", debts.Where(d => d.Direction == LoanDirection.Borrow).Sum(d => d.RemainingVnd), "Tổng bạn đang nợ người khác (các khoản chưa trả xong)"),
            Item("loans_owed_to_me", debts.Where(d => d.Direction == LoanDirection.Lend).Sum(d => d.RemainingVnd), "Tổng người khác đang nợ bạn"),
            Item("loans_overdue_count", overdue.Count, $"{overdue.Count} khoản đã quá hạn", "count"),
        };

        var number = 0;
        foreach (var debt in debts.OrderBy(d => d.DueDate ?? DateOnly.MaxValue).Take(MaxRows))
        {
            evidence.Add(Item($"loan_{++number}", debt.RemainingVnd, Describe(debt, today)));
        }

        return ToolResult.Ok(evidence);
    }

    private static string Describe(LoanVnd debt, DateOnly today)
    {
        var who = debt.Direction == LoanDirection.Borrow ? "Bạn nợ một người" : "Một người nợ bạn";
        return debt.DueDate switch
        {
            null => $"{who}, chưa đặt hạn",
            { } due when due < today => $"{who}, quá hạn {today.DayNumber - due.DayNumber} ngày (hạn {due:dd/MM/yyyy})",
            { } due => $"{who}, hạn {due:dd/MM/yyyy}",
        };
    }
}
