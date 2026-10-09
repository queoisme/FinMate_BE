using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Transactions.Commands;

/// <summary>
/// Một câu chat ("đi ăn 200k", "tháng này tôi tiêu bao nhiêu") → ghi giao dịch, bản nháp để xác nhận, hoặc số liệu tổng hợp.
///
/// AI Service chỉ HIỂU câu (<c>POST /api/v1/chat/parse</c>); mọi thứ có hậu quả nằm ở đây. Chỉ ghi thẳng khi AI
/// đủ chắc <i>và</i> backend tự chọn được ví và tìm được danh mục — thiếu một trong hai thì trả bản nháp, vì ghi
/// một giao dịch không danh mục hay vào nhầm ví là sai lặng lẽ. Thu nhập và chuyển tiền luôn qua bước xác nhận.
/// Hoàn tác và sửa danh mục dùng <c>DELETE</c> và <c>PATCH /transactions/{id}</c> có sẵn.
/// </summary>
public class ChatCommandHandler : IChatCommandHandler
{
    private const string DebitType = "debit";
    private const string CreditType = "credit";
    private const string ExpenseKind = "expense";
    private const string IncomeKind = "income";
    private const string TransferKind = "transfer";
    private const string ExpenseMetric = "expense";
    private const string IncomeMetric = "income";
    private const string UncategorizedName = "Chưa phân loại";

    private readonly IAIServiceClient _aiServiceClient;
    private readonly ICreateManualTransactionCommandHandler _createHandler;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IReportRepository _reportRepository;
    private readonly IValidator<ChatCommand> _validator;

    public ChatCommandHandler(
        IAIServiceClient aiServiceClient,
        ICreateManualTransactionCommandHandler createHandler,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IReportRepository reportRepository,
        IValidator<ChatCommand> validator)
    {
        _aiServiceClient = aiServiceClient;
        _createHandler = createHandler;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _reportRepository = reportRepository;
        _validator = validator;
    }

    public async Task<ChatResultDto> HandleAsync(ChatCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var parsed = await _aiServiceClient.ParseChatAsync(new ChatParseRequest(command.UserId, command.Text), ct);

        if (parsed.Entry is not null)
        {
            return await HandleEntryAsync(command, parsed, parsed.Entry, ct);
        }

        if (parsed.Query is not null)
        {
            return new ChatResultDto(
                ChatActions.Summary, parsed.Intent, parsed.ReplyText, null, null,
                await SummarizeAsync(command.UserId, parsed.Query, ct));
        }

        return new ChatResultDto(ChatActions.Reply, parsed.Intent, parsed.ReplyText, null, null, null);
    }

    private async Task<ChatResultDto> HandleEntryAsync(
        ChatCommand command, ChatParseResponse parsed, ChatParseEntry entry, CancellationToken ct)
    {
        var accountId = await ResolveAccountAsync(command, ct);

        if (entry.AutoSaveOk && entry.Kind == ExpenseKind && accountId is not null && entry.AmountCents > 0)
        {
            var category = entry.CategorySlug is null
                ? null
                : await _categoryRepository.GetSystemBySlugAsync(entry.CategorySlug, ct);
            if (category is not null)
            {
                var created = await _createHandler.HandleAsync(
                    new CreateManualTransactionCommand(
                        command.UserId,
                        accountId.Value,
                        category.Id,
                        entry.AmountCents,
                        TransactionType.Debit,
                        DateTimeOffset.UtcNow,
                        entry.MerchantName,
                        entry.Description,
                        command.ClientRequestId,
                        TransactionSource.Manual),
                    ct);
                return new ChatResultDto(ChatActions.Saved, parsed.Intent, parsed.ReplyText, created.Transaction, null, null);
            }
        }

        return new ChatResultDto(ChatActions.Confirm, parsed.Intent, parsed.ReplyText, null, DraftOf(entry, accountId), null);
    }

    private static ChatDraftDto DraftOf(ChatParseEntry entry, Guid? accountId)
    {
        var missing = new List<string>();
        if (entry.Kind == TransferKind)
        {
            missing.Add(ChatMissingFields.Accounts);  // chuyển tiền cần ví nguồn và ví đích
        }
        else
        {
            if (entry.Kind == ExpenseKind && entry.CategorySlug is null)
            {
                missing.Add(ChatMissingFields.Category);
            }

            if (accountId is null)
            {
                missing.Add(ChatMissingFields.Account);
            }
        }

        var transactionType = entry.Kind switch
        {
            ExpenseKind => DebitType,
            IncomeKind => CreditType,
            _ => null,
        };

        return new ChatDraftDto(
            entry.Kind,
            entry.AmountCents,
            transactionType,
            entry.MerchantName,
            entry.Description,
            entry.CategorySlug,
            entry.IncomeType,
            entry.ToOthers,
            entry.Kind == TransferKind ? null : accountId,
            missing);
    }

    /// <summary>Ví người dùng chọn, hoặc ví duy nhất của họ; nhiều ví mà không chọn thì null (không đoán).</summary>
    private async Task<Guid?> ResolveAccountAsync(ChatCommand command, CancellationToken ct)
    {
        if (command.FinancialAccountId is { } chosen)
        {
            var account = await _financialAccountRepository.GetByIdAsync(chosen, command.UserId, ct);
            return account?.Id;
        }

        var accounts = await _financialAccountRepository.GetListByUserAsync(command.UserId, ct);
        return accounts.Count == 1 ? accounts[0].Id : null;
    }

    private async Task<ChatSummaryDto> SummarizeAsync(Guid userId, ChatParseQuery query, CancellationToken ct)
    {
        var from = DateOnly.FromDateTime(query.From.DateTime);
        var to = DateOnly.FromDateTime(query.To.DateTime).AddDays(-1);  // [From, To) -> ngày cuối cùng của kỳ

        var days = await _reportRepository.GetDailySpendAsync(userId, from, to, ct);
        var expense = days.Sum(d => d.SpentCents);
        var income = days.Sum(d => d.IncomeCents);

        var items = new List<ChatSummaryItemDto>();
        var total = query.Metric switch
        {
            ExpenseMetric => expense,
            IncomeMetric => income,
            _ => income - expense,
        };

        if (query.Metric == ExpenseMetric)
        {
            items = await CategoryItemsAsync(userId, query, from, to, ct);
            total = items.Sum(i => i.AmountCents);  // lọc theo danh mục thì tổng là của danh mục đó
        }

        return new ChatSummaryDto(
            query.Metric, query.PeriodLabel, query.From, query.To, total, income, expense, income - expense, items);
    }

    private async Task<List<ChatSummaryItemDto>> CategoryItemsAsync(
        Guid userId, ChatParseQuery query, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var spend = await _reportRepository.GetCategorySpendAsync(userId, from, to, ct);
        var rows = spend
            .Where(s => query.CategorySlug is null || s.CategorySlug == query.CategorySlug)
            .Where(s => s.SpentCents > 0)
            .ToList();
        var sum = rows.Sum(s => s.SpentCents);

        return rows
            .OrderByDescending(s => s.SpentCents)
            .Select(s => new ChatSummaryItemDto(
                s.CategorySlug,
                s.CategoryName ?? UncategorizedName,
                s.SpentCents,
                sum <= 0 ? 0 : Math.Round((double)s.SpentCents / sum, 3)))
            .ToList();
    }
}
