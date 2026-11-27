using FinMate.Application.Categories.Commands;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
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
    private const string WalletBalanceMetric = "wallet_balance";
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
    private readonly ISystemConfigProvider? _config;  // null (test cũ): dùng cấu hình trợ lý mặc định
    private readonly IAiConsentReader? _consent;  // null: không có bộ đọc đồng ý (test cũ) thì không gửi gì ra bên thứ ba
    private readonly Refunds.IRefundSuggestions? _refundSuggestions;  // null: không gợi ý khoản chi gốc cho câu "được hoàn tiền"
    private readonly Loans.ILoanService? _loans;  // null: không tìm khoản vay cho câu trả nợ, giữ giao dịch thu/chi như cũ
    private readonly Duplicates.IDuplicateDetector? _duplicates;  // null: chỉ còn cảnh báo ±10 phút như trước Phase 82

    public ChatCommandHandler(
        IAIServiceClient aiServiceClient,
        ICreateManualTransactionCommandHandler createHandler,
        IFinancialAccountRepository financialAccountRepository,
        ICategoryRepository categoryRepository,
        IReportRepository reportRepository,
        ITransactionRepository transactionRepository,
        IValidator<ChatCommand> validator,
        IAiConsentReader? consent = null,
        ISystemConfigProvider? config = null,
        Refunds.IRefundSuggestions? refundSuggestions = null,
        Duplicates.IDuplicateDetector? duplicates = null,
        Loans.ILoanService? loans = null)
    {
        _loans = loans;
        _config = config;
        _refundSuggestions = refundSuggestions;
        _duplicates = duplicates;
        _aiServiceClient = aiServiceClient;
        _createHandler = createHandler;
        _financialAccountRepository = financialAccountRepository;
        _categoryRepository = categoryRepository;
        _reportRepository = reportRepository;
        _transactionRepository = transactionRepository;
        _validator = validator;
        _consent = consent;
    }

    /// <summary>Gắn mốc thời gian của server vào MỌI kết quả (ghi ngay, nháp, tổng kết, trả lời...): backend không lưu tin nhắn, nên app dựng "Hôm nay • 12:45" từ đây.</summary>
    public async Task<ChatResultDto> HandleAsync(ChatCommand command, CancellationToken ct = default)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var result = await HandleCoreAsync(command, ct);
        return result with { ReceivedAt = receivedAt, RespondedAt = DateTimeOffset.UtcNow };
    }

    private async Task<ChatResultDto> HandleCoreAsync(ChatCommand command, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        _assistant = _config is null ? new AssistantSettings() : (await _config.GetAsync(ct)).Document.Assistant;
        var allowAi = _consent is not null && await _consent.HasAiConsentAsync(command.UserId, ct);
        var parsed = await _aiServiceClient.ParseChatAsync(new ChatParseRequest(command.UserId, command.Text, allowAi, command.ItemContext), ct);

        if (parsed.Goal is { } goal)
        {
            // Đặt mục tiêu tiết kiệm: luôn qua bước xác nhận, không tạo gì ở đây. Client tạo bằng POST /saving-goals sau khi người dùng bấm Tạo.
            return new ChatResultDto(
                ChatActions.Goal, parsed.Intent, parsed.ReplyText, null, null, null, null,
                new ChatGoalDraftDto(goal.Name, goal.NameIsDefault, goal.TargetCents, goal.Deadline, goal.DeadlineText, goal.AiAssisted));
        }

        if (parsed.Item is { Operation: "repay" } repay && await TryRepayAsync(command, parsed, repay, ct) is { } repayResult)
        {
            return repayResult;
        }

        if (parsed.Item is { } item && item.Operation != "repay")
        {
            // Ngân sách / khoản định kỳ / vay nợ đủ thông tin: chỉ trả bản nháp, client gọi API tạo (và cho Hoàn tác). Thiếu thông tin thì AI không trả Item: rơi xuống nhánh trả lời với câu hỏi lại.
            return new ChatResultDto(
                ChatActions.Item, parsed.Intent, parsed.ReplyText, null, null, null, null, null, null, null, null,
                new ChatItemDraftDto(item.Kind, item.AmountCents, item.Name, item.CategorySlug, item.Cadence, item.NextDue, item.Direction, item.Counterparty, item.Operation));
        }

        if (parsed.Purchase is { } purchase)
        {
            // Hỏi trước khi mua: chỉ trả tham số. Không tạo giao dịch; app gọi POST /forecast/purchase-check để có đánh giá do backend tính.
            return new ChatResultDto(
                ChatActions.Purchase, parsed.Intent, parsed.ReplyText, null, null, null, null, null,
                new ChatPurchaseDraftDto(purchase.Item, purchase.AmountCents, purchase.PurchaseDate, purchase.CategorySlug));
        }

        if (parsed.Entries is { Count: > 1 } many)
        {
            // Nhiều khoản trong một câu: luôn qua bước xác nhận, không bao giờ ghi ngay (một câu sai là sai nhiều khoản cùng lúc).
            var accountId = await ResolveAccountAsync(command, ct);
            var drafts = new List<ChatDraftDto>();
            foreach (var e in many)
            {
                var (recent, suggested) = await FindDuplicatesAsync(command, e, accountId, ct);
                drafts.Add(await WithRefundSuggestionsAsync(command, e, DraftOf(e, accountId, recent, suggested), ct));
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
        var (duplicate, suggested) = await FindDuplicatesAsync(command, entry, accountId, ct);

        // Có thể trùng thì không ghi ngay: hỏi lại (kèm mã giao dịch có thể trùng), người dùng vẫn lưu được nếu đó là khoản thứ hai thật.
        // Phase 82 (BR-DUP-06): khoản mà ngân hàng có thể đã báo (thông báo trong 48 giờ) cũng không tự lưu, vì lưu là thành nghi trùng.
        if (duplicate is null && !suggested.MarksSuspected && entry.AutoSaveOk && entry.Kind == ExpenseKind && accountId is not null && entry.AmountCents > 0)
        {
            var category = entry.CategorySlug is null
                ? null
                : await _categoryRepository.GetVisibleSystemBySlugAsync(command.UserId, entry.CategorySlug, ct);
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

        return new ChatResultDto(ChatActions.Confirm, parsed.Intent, WithNotes(parsed.ReplyText, parsed), null, await WithRefundSuggestionsAsync(command, entry, DraftOf(entry, accountId, duplicate, suggested), ct), null);
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

    /// <summary>
    /// Hai lớp, giữ cả hai (D3): khoản chi cùng số tiền trong ±10 phút (gửi lại / bấm hai lần, không phân biệt nguồn) chỉ để chặn tự lưu;
    /// ứng viên của <see cref="Duplicates.DuplicateMatcher"/> (chủ yếu thông báo ngân hàng trong 48 giờ) để app hiện và để chặn tự lưu.
    /// </summary>
    private async Task<(Guid? Recent, Duplicates.DuplicateSuggestion Suggested)> FindDuplicatesAsync(
        ChatCommand command, ChatParseEntry entry, Guid? accountId, CancellationToken ct)
    {
        var at = entry.OccurredAt ?? DateTimeOffset.UtcNow;
        var recent = entry.Kind == ExpenseKind && entry.AmountCents > 0
            ? await _transactionRepository.FindPossibleDuplicateAsync(command.UserId, entry.AmountCents, at, DuplicateWindow, command.ClientRequestId, ct)
            : null;

        var type = entry.Kind switch
        {
            ExpenseKind => TransactionType.Debit,
            IncomeKind => TransactionType.Credit,
            _ => (TransactionType?)null,
        };
        var suggested = _duplicates is null || type is null
            ? Duplicates.DuplicateSuggestion.None
            : await _duplicates.SuggestForInputAsync(
                new Duplicates.DuplicateInput(command.UserId, TransactionSource.Manual, type.Value, entry.AmountCents, at, accountId, entry.MerchantName), ct);
        return (recent, suggested);
    }

    /// <summary>
    /// Phase 81 (D17): câu chat là nhận tiền hoàn (AI đoán <c>income_type = refund</c>) → kèm khoản chi gốc gợi ý. Chat không bao giờ tự lưu
    /// thu nhập (FR-CHAT-02), nên không có đường lưu mới: app chọn khoản gốc rồi gọi <c>POST /transactions/{id}/refunds</c>.
    /// </summary>
    private async Task<ChatDraftDto> WithRefundSuggestionsAsync(ChatCommand command, ChatParseEntry entry, ChatDraftDto draft, CancellationToken ct)
    {
        if (_refundSuggestions is null || entry.Kind != IncomeKind || !string.Equals(entry.IncomeType, RefundIncomeType, StringComparison.OrdinalIgnoreCase))
        {
            return draft;
        }

        var probe = new Transaction
        {
            UserId = command.UserId,
            FinancialAccountId = draft.SuggestedFinancialAccountId ?? Guid.Empty,
            AmountCents = entry.AmountCents,
            TransactionType = TransactionType.Credit,
            MerchantName = entry.MerchantName,
            TransactedAt = entry.OccurredAt ?? DateTimeOffset.UtcNow,
        };
        var suggestions = await _refundSuggestions.SuggestAsync(command.UserId, probe, TransactionRelation.Refund, ct);
        return draft with { RelationHint = TransactionRelation.Refund.ToString(), SuggestedRelatedTransactions = suggestions };
    }

    private const string RefundIncomeType = "refund";

    private AssistantSettings _assistant = new();

    private ChatDraftDto DraftOf(ChatParseEntry entry, Guid? accountId, Guid? duplicate = null, Duplicates.DuplicateSuggestion? suggested = null)
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

        var insight = DraftInsights.ForChat(entry, _assistant);
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
            duplicate ?? suggested?.Items.FirstOrDefault()?.Id,
            SuggestedDuplicates: suggested is { Items.Count: > 0 } ? suggested.Items : null,
            SourceDetail: DraftInsights.Chat,
            AiExplanation: insight.Explanation,
            Confidence: insight.Confidence);
    }

    /// <summary>Ví người dùng chọn, hoặc ví duy nhất của họ; nhiều ví mà không chọn thì null (không đoán).</summary>
    /// <summary>
    /// Câu trả nợ ("bạn An trả tôi 500k"): tìm khoản vay CỦA NGƯỜI DÙNG khớp tên và đúng chiều, còn đủ số để trừ; đúng MỘT khoản thì trả bản ghi lần trả để app xác nhận rồi gọi
    /// <c>POST /loans/{id}/repayments</c> (lần trả ghi cả giao dịch ví nên không ghi thêm giao dịch thu/chi). Không khớp, nhiều khoản cùng tên hoặc trả quá số còn lại thì null:
    /// câu vẫn được đọc như giao dịch thu/chi thường. Tên người không bao giờ rời khỏi backend.
    /// </summary>
    private async Task<ChatResultDto?> TryRepayAsync(ChatCommand command, ChatParseResponse parsed, ChatParseItem repay, CancellationToken ct)
    {
        if (_loans is null || string.IsNullOrWhiteSpace(repay.Counterparty) || repay.AmountCents <= 0)
        {
            return null;
        }

        var wanted = repay.Direction ?? string.Empty;
        // Phase 90 (S3, Q15): cùng một hàm khớp tên với gợi ý từ thông báo; câu có dấu thì so giữ dấu ("Hòa" ≠ "Hoa"), không dấu thì bỏ dấu.
        var matches = (await _loans.ListAsync(command.UserId, ct))
            .Where(l => l.Status == "open" && l.Direction == wanted && l.RemainingCents >= repay.AmountCents
                        && Loans.PersonNameMatch.SameName(l.Counterparty, repay.Counterparty, command.Text))
            .ToList();
        if (matches.Count != 1)
        {
            return null;
        }

        var loan = matches[0];
        var accountId = await ResolveAccountAsync(command, ct);
        return new ChatResultDto(
            ChatActions.Item, parsed.Intent, null, null, null, null, null, null, null, null, null,
            new ChatItemDraftDto(
                "loan", repay.AmountCents, loan.Title, null, null, null, loan.Direction, loan.Counterparty, "repay", loan.Id, loan.RemainingCents, accountId));
    }

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
        var left = income - expense;
        var total = query.Metric switch
        {
            ExpenseMetric => expense,
            IncomeMetric => income,
            // "số dư của tôi" là số tiền đang có trong các ví lúc này (cùng cách tính với "Tổng tài sản" ở trang chủ); "còn lại tháng này" (balance) là thu trừ chi của kỳ
            WalletBalanceMetric => (await _financialAccountRepository.GetListByUserAsync(userId, ct)).Sum(a => a.BalanceCents),
            _ => income - expense,
        };

        if (query.Metric == ExpenseMetric)
        {
            items = await CategoryItemsAsync(userId, query, from, to, ct);
            if (query.CategorySlug is not null || query.CategorySlugs is { Count: > 0 })
            {
                // lọc theo danh mục thì cả thẻ nói về danh mục đó: tổng chi là của danh mục, không kèm thu và "còn lại" của cả kỳ (khác số với tổng chi nên đọc là mâu thuẫn)
                expense = items.Sum(i => i.AmountCents);
                income = 0;
                left = 0;
            }

            total = items.Sum(i => i.AmountCents);
        }

        IReadOnlyList<ChatTransactionDto>? transactions = null;
        var hasMore = false;
        if (query.Detail && (query.Metric == ExpenseMetric || query.Metric == IncomeMetric))
        {
            (transactions, hasMore) = await ListTransactionsAsync(userId, query, ct);
        }

        return new ChatSummaryDto(
            query.Metric, query.PeriodLabel, query.From, query.To, total, income, expense, left, items, transactions, hasMore);
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
        var sum = rows.Sum(s => s.SpentCents);  // tỷ lệ tính trong phạm vi được hỏi, khớp với "Tổng chi" trên thẻ (xem SummarizeAsync: hỏi riêng nhóm thì tổng chi là của nhóm đó)

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
