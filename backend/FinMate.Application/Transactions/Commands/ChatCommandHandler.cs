using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Models;
using FinMate.Domain.Entities;
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

    /// <summary>Số khoản tối đa trả về trong danh sách chi tiết: chat không phải màn hình danh sách; còn nữa thì <c>TransactionsHasMore</c> mời xem ở màn hình giao dịch.</summary>
    private const int DetailLimit = 20;

    // Gửi lại cùng một câu do mạng chập chờn hay bấm hai lần: hẹp để hai ly cà phê thật cách nhau lâu hơn không bị cảnh báo.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(10);

    private readonly IAIServiceClient _aiServiceClient;
    private readonly ICreateManualTransactionCommandHandler _createHandler;
    private readonly IFinancialAccountRepository _financialAccountRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IReportRepository _reportRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IValidator<ChatCommand> _validator;
    private readonly IAiConsentReader? _consent;  // null: không có bộ đọc đồng ý (test cũ) thì không gửi gì ra bên thứ ba

    public ChatCommandHandler(
        IAIServiceClient aiServiceClient,
        ICreateManualTransactionCommandHandler createHandler,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IReportRepository reportRepository,
        ITransactionRepository transactionRepository,
        IValidator<ChatCommand> validator,
        IAiConsentReader? consent = null)
    {
        _aiServiceClient = aiServiceClient;
        _createHandler = createHandler;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _reportRepository = reportRepository;
        _transactionRepository = transactionRepository;
        _validator = validator;
        _consent = consent;
    }

    public async Task<ChatResultDto> HandleAsync(ChatCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var allowAi = _consent is not null && await _consent.HasAiConsentAsync(command.UserId, ct);
        var parsed = await _aiServiceClient.ParseChatAsync(new ChatParseRequest(command.UserId, command.Text, allowAi), ct);

        if (parsed.Goal is { } goal)
        {
            // Đặt mục tiêu tiết kiệm: luôn qua bước xác nhận, không tạo gì ở đây. Client tạo bằng POST /saving-goals sau khi người dùng bấm Tạo.
            return new ChatResultDto(
                ChatActions.Goal, parsed.Intent, parsed.ReplyText, null, null, null, null,
                new ChatGoalDraftDto(goal.Name, goal.NameIsDefault, goal.TargetCents, goal.Deadline, goal.DeadlineText));
        }

        if (parsed.Entries is { Count: > 1 } many)
        {
            // Nhiều khoản trong một câu: luôn qua bước xác nhận, không bao giờ ghi ngay (một câu sai là sai nhiều khoản cùng lúc).
            var accountId = await ResolveAccountAsync(command, ct);
            var drafts = new List<ChatDraftDto>();
            foreach (var e in many)
            {
                drafts.Add(DraftOf(e, accountId, await FindDuplicateAsync(command, e, ct)));
            }

            // Client cũ chỉ đọc `draft` (bản đầu): câu trả lời nói rõ có nhiều khoản để người dùng không im lặng mất các khoản sau.
            var reply = WithNotes(parsed.ReplyText ?? $"Mình thấy {drafts.Count} khoản trong câu này. Bạn kiểm tra và xác nhận từng khoản nhé.", parsed);
            return new ChatResultDto(ChatActions.Confirm, parsed.Intent, reply, null, drafts[0], null, drafts);
        }

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
        var duplicate = await FindDuplicateAsync(command, entry, ct);

        // Có thể trùng thì không ghi ngay: hỏi lại (kèm mã giao dịch có thể trùng), người dùng vẫn lưu được nếu đó là khoản thứ hai thật.
        if (duplicate is null && entry.AutoSaveOk && entry.Kind == ExpenseKind && accountId is not null && entry.AmountCents > 0)
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
                        entry.OccurredAt ?? DateTimeOffset.UtcNow,
                        entry.MerchantName,
                        entry.Description,
                        command.ClientRequestId,
                        TransactionSource.Manual),
                    ct);
                return new ChatResultDto(ChatActions.Saved, parsed.Intent, parsed.ReplyText, created.Transaction, null, null);
            }
        }

        return new ChatResultDto(ChatActions.Confirm, parsed.Intent, WithNotes(parsed.ReplyText, parsed), null, DraftOf(entry, accountId, duplicate), null);
    }

    // Chạy được cả khi runtime ở chế độ globalization-invariant (không có culture vi-VN): tự đổi dấu phân cách nghìn sang dấu chấm.
    private static string Money(long cents) => cents.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.') + " ₫";

    /// <summary>
    /// Điều AI thấy chưa khớp trong câu (vế thiếu số tiền, tổng đã nói khác tổng các khoản) được nói thẳng với người dùng: nếu không,
    /// khoản bị bỏ biến mất im lặng. AI đã chặn ghi ngay trong các trường hợp này nên luôn có bước xác nhận để đọc lời nhắn.
    /// </summary>
    private static string? WithNotes(string? reply, ChatParseResponse parsed)
    {
        var notes = new List<string>();
        foreach (var clause in parsed.Skipped ?? [])
        {
            notes.Add($"Mình chưa thấy số tiền cho \"{clause}\" nên chưa ghi. Bạn nhắn thêm số tiền nhé.");
        }

        if (parsed.TotalMismatch && parsed.TotalAmount is { } stated)
        {
            var entries = parsed.Entries is { Count: > 0 } many ? many : parsed.Entry is null ? [] : new[] { parsed.Entry };
            var sum = entries.Sum(e => e.AmountCents);
            notes.Add($"Bạn nói tổng {Money(stated)} nhưng các khoản cộng lại là {Money(sum)}. Kiểm tra lại giúp mình nhé.");
        }

        return notes.Count == 0 ? reply : string.Join(" ", new[] { reply }.Concat(notes).Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    private Task<Guid?> FindDuplicateAsync(ChatCommand command, ChatParseEntry entry, CancellationToken ct)
        => entry.Kind == ExpenseKind && entry.AmountCents > 0
            ? _transactionRepository.FindPossibleDuplicateAsync(
                command.UserId, entry.AmountCents, entry.OccurredAt ?? DateTimeOffset.UtcNow, DuplicateWindow, command.ClientRequestId, ct)
            : Task.FromResult<Guid?>(null);

    private static ChatDraftDto DraftOf(ChatParseEntry entry, Guid? accountId, Guid? duplicate = null)
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
            missing,
            entry.OccurredAt,
            duplicate);
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

        IReadOnlyList<ChatTransactionDto>? transactions = null;
        var hasMore = false;
        if (query.Detail && (query.Metric == ExpenseMetric || query.Metric == IncomeMetric))
        {
            (transactions, hasMore) = await ListTransactionsAsync(userId, query, ct);
        }

        return new ChatSummaryDto(
            query.Metric, query.PeriodLabel, query.From, query.To, total, income, expense, income - expense, items, transactions, hasMore);
    }

    /// <summary>
    /// Danh sách từng khoản của kỳ cho câu "chi tiết hôm nay" / "liệt kê các khoản chi tháng này": mới nhất trước, tối đa <see cref="DetailLimit"/> dòng, cùng quy ước với tổng
    /// (chỉ khoản đã xác nhận, bỏ chuyển tiền giữa ví và cho vay/mượn, đúng giờ Việt Nam). Hỏi theo danh mục thì chỉ các khoản của danh mục đó.
    /// </summary>
    private async Task<(IReadOnlyList<ChatTransactionDto> Items, bool HasMore)> ListTransactionsAsync(Guid userId, ChatParseQuery query, CancellationToken ct)
    {
        var isExpense = query.Metric == ExpenseMetric;
        var type = isExpense ? TransactionType.Debit : TransactionType.Credit;
        var to = query.To.AddTicks(-1);  // kỳ là nửa mở [From, To) còn bộ lọc của repository thì gồm cả mốc cuối

        var slugs = !isExpense ? null
            : query.CategorySlugs is { Count: > 0 } many ? many
            : query.CategorySlug is null ? null : new[] { query.CategorySlug };

        var found = new List<Transaction>();
        var hasMore = false;
        if (slugs is null)
        {
            var page = await _transactionRepository.GetListAsync(
                new TransactionListFilter(userId, null, null, type, TransactionStatus.Confirmed, query.From, to, null, DetailLimit, ExcludeLoans: true), ct);
            found.AddRange(page.Items);
            hasMore = page.NextCursor is not null;
        }
        else
        {
            foreach (var slug in slugs.Distinct())
            {
                var category = await _categoryRepository.GetSystemBySlugAsync(slug, ct);
                if (category is null)
                {
                    continue;
                }

                var page = await _transactionRepository.GetListAsync(
                    new TransactionListFilter(userId, null, category.Id, type, TransactionStatus.Confirmed, query.From, to, null, DetailLimit, ExcludeLoans: true), ct);
                found.AddRange(page.Items);
                hasMore |= page.NextCursor is not null;
            }

            found = found.OrderByDescending(t => t.TransactedAt).ThenByDescending(t => t.CreatedAt).ToList();
            hasMore |= found.Count > DetailLimit;
        }

        var items = found
            .Take(DetailLimit)
            .Select(t => new ChatTransactionDto(
                t.Id, t.TransactedAt, t.AmountCents, isExpense ? ExpenseKind : IncomeKind, t.MerchantName, t.Description, t.Category?.Slug, t.Category?.Name))
            .ToList();
        return (items, hasMore);
    }

    private async Task<List<ChatSummaryItemDto>> CategoryItemsAsync(
        Guid userId, ChatParseQuery query, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var spend = await _reportRepository.GetCategorySpendAsync(userId, from, to, ct);
        // Hỏi về hai danh mục trở lên ("giải trí và mua sắm") thì AI gửi cả danh sách; câu cũ chỉ có một CategorySlug.
        var slugs = query.CategorySlugs is { Count: > 0 } many ? many
            : query.CategorySlug is null ? null : new[] { query.CategorySlug };
        var rows = spend
            .Where(s => slugs is null || (s.CategorySlug is not null && slugs.Contains(s.CategorySlug)))
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
