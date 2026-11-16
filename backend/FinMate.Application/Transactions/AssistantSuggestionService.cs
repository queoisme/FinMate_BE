using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Enums;

namespace FinMate.Application.Transactions;

public sealed record SuggestionDto(string Key, string Text);

public interface IAssistantSuggestionService
{
    Task<IReadOnlyList<SuggestionDto>> ListAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>Gợi ý câu hỏi cho khung chat: mẫu do admin đặt (cấu hình hệ thống), lọc theo ngữ cảnh của chính người dùng. Có thể rỗng.</summary>
public class AssistantSuggestionService : IAssistantSuggestionService
{
    private readonly ISystemConfigProvider _config;
    private readonly ITransactionRepository _transactions;
    private readonly IBudgetRepository _budgets;
    private readonly ISavingGoalRepository _goals;

    public AssistantSuggestionService(ISystemConfigProvider config, ITransactionRepository transactions, IBudgetRepository budgets, ISavingGoalRepository goals)
    {
        _config = config;
        _transactions = transactions;
        _budgets = budgets;
        _goals = goals;
    }

    public async Task<IReadOnlyList<SuggestionDto>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        var settings = (await _config.GetAsync(ct)).Document.Assistant;
        var result = new List<SuggestionDto>();

        // Mỗi ngữ cảnh chỉ tra một lần, và chỉ khi có mẫu cần đến nó.
        var cache = new Dictionary<string, bool>();
        foreach (var s in settings.Suggestions)
        {
            if (result.Count >= settings.MaxSuggestions)
            {
                break;
            }

            if (!cache.TryGetValue(s.When, out var ok))
            {
                ok = cache[s.When] = await MatchesAsync(userId, s.When, ct);
            }

            if (ok)
            {
                result.Add(new SuggestionDto(s.Key, s.Text));
            }
        }

        return result;
    }

    private async Task<bool> MatchesAsync(Guid userId, string context, CancellationToken ct)
    {
        switch (context)
        {
            case "noTransactionToday":
                var start = new DateTimeOffset(VietnamTime.Today().ToDateTime(TimeOnly.MinValue), VietnamTime.Offset);
                var today = await _transactions.GetListAsync(
                    new TransactionListFilter(userId, null, null, null, TransactionStatus.Confirmed, start, start.AddDays(1), null, 1), ct);
                return today.Items.Count == 0;
            case "hasBudget":
                return (await _budgets.GetListForUserAsync(userId, ct)).Count > 0;
            case "hasGoal":
                return (await _goals.GetListForUserAsync(userId, null, ct)).Count > 0;
            default:
                return true;
        }
    }
}
