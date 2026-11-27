using System.Text.Json;
using FinMate.Application.Admin;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Agent;

/// <summary>Bằng chứng từ dữ liệu quản lý đã xác nhận của người dùng (ngân sách, mục tiêu, khoản định kỳ): nguồn <see cref="EvidenceSource.Confirmed"/>, đầy đủ.</summary>
internal static class ManagementEvidence
{
    public static Evidence Money(string id, long value, string text, DateTimeOffset now)
        => new(id, id, value, text, "VND", EvidenceSource.Confirmed, now, ConfidenceCalculator.Compute(EvidenceSource.Confirmed, now, now, 1.0));
}

/// <summary>
/// Công cụ <c>budget_status</c>: "ngân sách tháng này còn bao nhiêu / nhóm nào sắp vượt?". Đọc tiến độ từng ngân sách qua <see cref="IBudgetForecastService"/> (cùng số màn Dự báo hiển thị).
/// Giữ tối đa 4 ngân sách có phần trăm dự kiến cao nhất (cao trước, hòa thì theo tên), mỗi cái hai bằng chứng (đã chi, hạn mức) nên tổng ≤ 8; phần trăm chỉ nằm trong <c>Text</c>.
/// </summary>
public sealed class BudgetStatusTool(IBudgetForecastService budgets) : IAgentTool
{
    private const int Top = 4;

    public ToolSpec Spec { get; } = new(
        "budget_status",
        "Tình hình các ngân sách tháng này của người dùng: đã chi bao nhiêu trên hạn mức, nhóm nào có nguy cơ vượt. Không có tham số.",
        []);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var rows = (await budgets.GetAsync(context.UserId, ct))
            .OrderByDescending(b => b.ProjectedPercent).ThenBy(b => b.CategoryName, StringComparer.Ordinal)
            .Take(Top).ToList();
        if (rows.Count == 0)
        {
            return ToolResult.Insufficient("no_budgets");
        }

        var evidence = new List<Evidence>();
        foreach (var (budget, index) in rows.Select((b, i) => (b, i + 1)))
        {
            var name = budget.CategoryName ?? "Tổng chi tiêu";
            var spent = budget.LimitCents > 0 ? (budget.SpentCents * 100 + budget.LimitCents / 2) / budget.LimitCents : 0;
            var state = budget.Status switch { "over" => "đã vượt hạn mức", "at_risk" => "có nguy cơ vượt hạn mức", _ => "trong hạn mức" };
            evidence.Add(ManagementEvidence.Money($"budget_{index}_spent", budget.SpentCents, $"{name}: đã chi {spent}% hạn mức tháng này, {state}", context.Now));
            evidence.Add(ManagementEvidence.Money($"budget_{index}_limit", budget.LimitCents, $"{name}: hạn mức tháng này", context.Now));
        }

        return ToolResult.Ok(evidence);
    }
}

/// <summary>
/// Công cụ <c>goals_progress</c>: "mục tiêu tiết kiệm của tôi đến đâu rồi?". Tối đa 4 mục tiêu đang chạy (hạn gần nhất trước, không hạn đứng sau), mỗi cái hai bằng chứng (đã có, mục tiêu).
/// </summary>
public sealed class GoalsProgressTool(ISavingGoalRepository goals) : IAgentTool
{
    private const int Top = 4;

    public ToolSpec Spec { get; } = new(
        "goals_progress",
        "Tiến độ các mục tiêu tiết kiệm đang chạy của người dùng: đã có bao nhiêu trên mục tiêu, hạn khi nào. Không có tham số.",
        []);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var rows = (await goals.GetListForUserAsync(context.UserId, SavingGoalStatus.Active, ct))
            .OrderBy(g => g.Deadline ?? DateTimeOffset.MaxValue).ThenBy(g => g.Name, StringComparer.Ordinal)
            .Take(Top).ToList();
        if (rows.Count == 0)
        {
            return ToolResult.Insufficient("no_goals");
        }

        var evidence = new List<Evidence>();
        foreach (var (goal, index) in rows.Select((g, i) => (g, i + 1)))
        {
            var percent = goal.TargetCents > 0 ? (goal.SavedCents * 100 + goal.TargetCents / 2) / goal.TargetCents : 0;
            var due = goal.Deadline is { } deadline ? $", hạn {VietnamTime.DateOf(deadline):dd/MM/yyyy}" : ", không đặt hạn";
            evidence.Add(ManagementEvidence.Money($"goal_{index}_saved", goal.SavedCents, $"{goal.Name}: đã có {percent}% mục tiêu{due}", context.Now));
            evidence.Add(ManagementEvidence.Money($"goal_{index}_target", goal.TargetCents, $"{goal.Name}: số tiền mục tiêu", context.Now));
        }

        return ToolResult.Ok(evidence);
    }
}

/// <summary>
/// Công cụ <c>upcoming_items</c>: "sắp tới có khoản nào đến hạn?". Các khoản thu/chi định kỳ đang theo dõi có ngày đến hạn trong 14 ngày tới (từ hôm nay, giờ Việt Nam), sớm nhất trước, tối đa 5.
/// </summary>
public sealed class UpcomingItemsTool(IRecurringItemRepository items) : IAgentTool
{
    private const int Days = 14;
    private const int Top = 5;

    public ToolSpec Spec { get; } = new(
        "upcoming_items",
        "Các khoản thu/chi định kỳ (hóa đơn, tiền nhà, lương...) sắp đến hạn trong 14 ngày tới. Không có tham số.",
        []);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var today = VietnamTime.DateOf(context.Now);
        var rows = (await items.ListAsync(context.UserId, RecurringStatus.Active, ct))
            .Where(i => !i.ToView().Exhausted && i.NextDueDate >= today && i.NextDueDate <= today.AddDays(Days))
            .OrderBy(i => i.NextDueDate).ThenBy(i => i.Name, StringComparer.Ordinal)
            .Take(Top).ToList();
        if (rows.Count == 0)
        {
            return ToolResult.Insufficient("no_upcoming_items");
        }

        var evidence = rows.Select((item, i) => ManagementEvidence.Money(
            $"upcoming_{i + 1}", item.AmountCents,
            $"{item.Name} ({(item.Direction == RecurringDirection.Inflow ? "thu" : "chi")}) đến hạn {item.NextDueDate:dd/MM/yyyy}", context.Now)).ToList();
        return ToolResult.Ok(evidence);
    }
}

/// <summary>
/// Công cụ <c>propose_budget_limit</c>: "đặt hạn mức ăn uống 3 triệu". CHỈ đề xuất: tìm nhóm chi theo tên người dùng nói (bỏ dấu, không phân biệt hoa thường; khớp đúng tên trước, rồi tên chứa cụm đó,
/// phải ra đúng một nhóm), trả bằng chứng là số tiền người dùng nêu và một thẻ <see cref="SuggestedActionKinds.SetBudgetLimit"/>. App hiện thẻ, người dùng bấm thì hạn mức mới được đặt.
/// Không tìm thấy hoặc nhiều nhóm cùng khớp thì "chưa đủ dữ liệu" (agent hỏi lại), không đoán.
/// </summary>
public sealed class ProposeBudgetLimitTool(ICategoryRepository categories) : IAgentTool
{
    public ToolSpec Spec { get; } = new(
        "propose_budget_limit",
        "Đề xuất đặt hạn mức chi mỗi tháng cho một nhóm chi: category_name là tên nhóm người dùng nói (ví dụ 'ăn uống'), limit_vnd là số tiền họ nêu. Chỉ đề xuất, người dùng bấm xác nhận mới đặt.",
        [
            new ToolArg("category_name", "string", true, 0, 64),
            new ToolArg("limit_vnd", "int", true, 1_000, AgentLimits.MaxPriceVnd),
        ]);

    public async Task<ToolResult> RunAsync(ToolContext context, JsonElement args, CancellationToken ct = default)
    {
        var wanted = AdminPaging.Fold(args.GetProperty("category_name").GetString());
        var limit = args.GetProperty("limit_vnd").GetInt64();
        if (wanted.Length == 0)
        {
            return ToolResult.Insufficient("category_not_found");
        }

        var expense = (await categories.GetListForUserAsync(context.UserId, ct))
            .Where(c => c.Kind == Category.KindExpense && c.IsActive && c.Slug.Length > 0).ToList();
        var matches = expense.Where(c => AdminPaging.Fold(c.Name) == wanted).ToList();
        if (matches.Count == 0)
        {
            matches = expense.Where(c => AdminPaging.Fold(c.Name).Contains(wanted, StringComparison.Ordinal)).ToList();
        }

        if (matches.Count != 1)
        {
            return ToolResult.Insufficient(matches.Count == 0 ? "category_not_found" : "category_ambiguous");
        }

        var category = matches[0];
        var evidence = new List<Evidence>
        {
            new("limit", "budget_limit_proposed", limit, $"Đề xuất hạn mức mỗi tháng cho {category.Name} đúng bằng số người dùng nêu; CHƯA đặt, chờ người dùng bấm xác nhận", "VND", EvidenceSource.UserEntered, context.Now,
                ConfidenceCalculator.Compute(EvidenceSource.UserEntered, context.Now, context.Now, 1.0)),
        };
        var actions = new List<SuggestedActionDto>
        {
            new(SuggestedActionKinds.SetBudgetLimit, $"Đặt hạn mức {category.Name} {LocalTemplateComposer.FormatVnd(limit)} mỗi tháng", limit, null, category.Slug, category.Name, ["limit"]),
        };
        return ToolResult.Ok(evidence, actions);
    }
}
