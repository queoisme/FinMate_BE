using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Notifications;
using FinMate.Application.Ops;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Support;

public record SupportAccessRequestDto(
    Guid Id,
    Guid TicketId,
    IReadOnlyList<string> Scopes,
    string Reason,
    string Status,
    Guid? FinancialAccountId,
    DateTimeOffset CreatedAt,
    DateTimeOffset RequestExpiresAt,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

public record SupportAccessLogDto(Guid TicketId, string Scope, DateTimeOffset ViewedAt);

public record SupportAccessLogListResult(IReadOnlyList<SupportAccessLogDto> Items, string? NextCursor);

/// <summary>Giới hạn trang cho các danh sách phân trang kiểu con trỏ trong tính năng hỗ trợ.</summary>
public static class SupportPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static int Clamp(int limit) => limit <= 0 ? DefaultLimit : Math.Min(limit, MaxLimit);
}

/// <summary>Các DTO dưới đây tách riêng để không mang theo trường riêng tư như <c>NotificationLogId</c>, <c>ClientRequestId</c> hay số dư sau giao dịch.</summary>
public record CustomerAccountDto(Guid Id, string AccountName, string AccountType, long BalanceCents, bool IsMonitored);

public record CustomerTransactionDto(
    Guid Id,
    Guid FinancialAccountId,
    long AmountCents,
    string TransactionType,
    string Source,
    string Status,
    string? CategoryName,
    string? MerchantName,
    string? Description,
    DateTimeOffset TransactedAt);

public record CustomerTransactionPageDto(IReadOnlyList<CustomerTransactionDto> Items, string? NextCursor);

public record CustomerBudgetDto(Guid Id, string? CategoryName, long LimitCents, long SpentCents, string PeriodType);

public sealed record SupportDraftInput(
    long AmountCents,
    TransactionType TransactionType,
    DateTimeOffset TransactedAt,
    Guid? CategoryId,
    string? MerchantName,
    string? Description,
    Guid? ClientRequestId);

public record SupportDraftResult(Guid TransactionId, bool AlreadyExisted);

/// <summary>Phía khách: trả lời yêu cầu xem dữ liệu và biết ai đã xem gì.</summary>
public interface ISupportAccessService
{
    Task<IReadOnlyList<SupportAccessRequestDto>> ListAsync(Guid userId, Guid ticketId, CancellationToken ct = default);
    Task<SupportAccessRequestDto> ApproveAsync(Guid userId, Guid ticketId, Guid requestId, Guid? financialAccountId, CancellationToken ct = default);
    Task<SupportAccessRequestDto> DenyAsync(Guid userId, Guid ticketId, Guid requestId, CancellationToken ct = default);
    Task<SupportAccessRequestDto> RevokeAsync(Guid userId, Guid ticketId, Guid requestId, CancellationToken ct = default);
    Task<SupportAccessLogListResult> AccessLogAsync(Guid userId, string? cursor, int limit, CancellationToken ct = default);
}

/// <summary>
/// Đường duy nhất để đội vận hành chạm vào dữ liệu tài chính của khách. <c>userId</c> của khách luôn lấy từ ticket, không nhận từ request.
/// Có vai trò cao đến đâu cũng phải là người phụ trách ca và được chính khách đồng ý.
/// </summary>
public interface IOpsCustomerDataService
{
    Task<SupportAccessRequestDto> RequestAccessAsync(Guid actorId, Guid ticketId, SupportAccessScope scopes, string? reason, string? ip, CancellationToken ct = default);
    Task<IReadOnlyList<SupportAccessRequestDto>> ListRequestsAsync(Guid actorId, Guid ticketId, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerAccountDto>> GetAccountsAsync(Guid actorId, Guid ticketId, string? ip, CancellationToken ct = default);
    Task<CustomerTransactionPageDto> GetTransactionsAsync(Guid actorId, Guid ticketId, DateTimeOffset? from, DateTimeOffset? to, string? cursor, int limit, string? ip, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerBudgetDto>> GetBudgetsAsync(Guid actorId, Guid ticketId, string? ip, CancellationToken ct = default);
    Task<SupportDraftResult> CreateDraftAsync(Guid actorId, Guid ticketId, SupportDraftInput input, string? ip, CancellationToken ct = default);
}

public static class SupportAccessRules
{
    /// <summary>Thời hạn tối đa đã cam kết với khách. Không cho cấu hình để không ai vô tình nới ra.</summary>
    public static readonly TimeSpan GrantLifetime = TimeSpan.FromHours(24);

    public const int MaxReasonLength = 500;

    public static IReadOnlyList<string> ScopeNames(SupportAccessScope scopes)
        => Enum.GetValues<SupportAccessScope>()
            .Where(s => s != SupportAccessScope.None && scopes.HasFlag(s))
            .Select(s => s.ToString())
            .ToList();

    public static SupportAccessRequestDto ToDto(SupportAccessGrant g)
        => new(g.Id, g.TicketId, ScopeNames(g.Scopes), g.Reason, g.Status.ToString(), g.FinancialAccountId, g.CreatedAt, g.RequestExpiresAt, g.DecidedAt, g.ExpiresAt, g.RevokedAt);
}

public sealed class SupportAccessService : ISupportAccessService
{
    private readonly ISupportRepository _support;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ISystemConfigProvider _config;

    public SupportAccessService(ISupportRepository support, IFinancialAccountRepository accounts, ISystemConfigProvider config)
    {
        _config = config;
        _support = support;
        _accounts = accounts;
    }

    private async Task<SupportTicket> RequireTicketAsync(Guid userId, Guid ticketId, CancellationToken ct)
        => await _support.GetForRequesterAsync(ticketId, userId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);

    private async Task<SupportAccessGrant> RequireGrantAsync(Guid ticketId, Guid requestId, CancellationToken ct)
        => await _support.GetGrantAsync(ticketId, requestId, ct) ?? throw new NotFoundException("SupportAccessRequest", requestId);

    public async Task<IReadOnlyList<SupportAccessRequestDto>> ListAsync(Guid userId, Guid ticketId, CancellationToken ct = default)
    {
        await RequireTicketAsync(userId, ticketId, ct);
        return (await _support.ListGrantsAsync(ticketId, ct)).Select(SupportAccessRules.ToDto).ToList();
    }

    public async Task<SupportAccessRequestDto> ApproveAsync(Guid userId, Guid ticketId, Guid requestId, Guid? financialAccountId, CancellationToken ct = default)
    {
        // Chỉ chặn cấp quyền mới. Xem, từ chối, thu hồi và xem lịch sử vẫn mở khi tắt cờ: khách luôn phải rút lại được quyền đã cho.
        if (!(await _config.GetAsync(ct)).Document.Features.Support)
        {
            throw new BusinessRuleException(SupportErrorCodes.Disabled, "Tính năng hỗ trợ chưa được bật.");
        }

        var ticket = await RequireTicketAsync(userId, ticketId, ct);
        SupportTicketRules.EnsureNotClosed(ticket);
        var grant = await RequireGrantAsync(ticketId, requestId, ct);
        var now = DateTimeOffset.UtcNow;

        if (grant.Status != SupportAccessStatus.Pending || grant.RequestExpiresAt <= now)
        {
            throw new BusinessRuleException(SupportErrorCodes.AccessRequestNotPending, "Yêu cầu này không còn chờ bạn trả lời.");
        }

        if (grant.Scopes.HasFlag(SupportAccessScope.Draft))
        {
            if (financialAccountId is not { } accountId || await _accounts.GetByIdAsync(accountId, userId, ct) is null)
            {
                throw new BusinessRuleException(SupportErrorCodes.DraftAccountRequired, "Vui lòng chọn ví để nhận giao dịch nháp.");
            }

            grant.FinancialAccountId = accountId;
        }

        grant.Status = SupportAccessStatus.Approved;
        grant.DecidedAt = now;
        grant.ExpiresAt = now + SupportAccessRules.GrantLifetime;
        await _support.SaveChangesAsync(ct);
        return SupportAccessRules.ToDto(grant);
    }

    public async Task<SupportAccessRequestDto> DenyAsync(Guid userId, Guid ticketId, Guid requestId, CancellationToken ct = default)
    {
        await RequireTicketAsync(userId, ticketId, ct);
        var grant = await RequireGrantAsync(ticketId, requestId, ct);
        if (grant.Status != SupportAccessStatus.Pending)
        {
            throw new BusinessRuleException(SupportErrorCodes.AccessRequestNotPending, "Yêu cầu này không còn chờ bạn trả lời.");
        }

        grant.Status = SupportAccessStatus.Denied;
        grant.DecidedAt = DateTimeOffset.UtcNow;
        await _support.SaveChangesAsync(ct);
        return SupportAccessRules.ToDto(grant);
    }

    public async Task<SupportAccessRequestDto> RevokeAsync(Guid userId, Guid ticketId, Guid requestId, CancellationToken ct = default)
    {
        await RequireTicketAsync(userId, ticketId, ct);
        var grant = await RequireGrantAsync(ticketId, requestId, ct);

        // Gọi lại vẫn hợp lệ: khách bấm thu hồi hai lần không phải lỗi.
        if (grant.Status == SupportAccessStatus.Approved)
        {
            grant.Status = SupportAccessStatus.Revoked;
            grant.RevokedAt = DateTimeOffset.UtcNow;
            await _support.SaveChangesAsync(ct);
        }

        return SupportAccessRules.ToDto(grant);
    }

    public async Task<SupportAccessLogListResult> AccessLogAsync(Guid userId, string? cursor, int limit, CancellationToken ct = default)
    {
        var page = await _support.ListAccessLogsAsync(userId, cursor, SupportPaging.Clamp(limit), ct);
        return new SupportAccessLogListResult(
            page.Items.Select(l => new SupportAccessLogDto(l.TicketId, l.Scope.ToString(), l.CreatedAt)).ToList(),
            page.NextCursor);
    }
}

public sealed class OpsCustomerDataService : IOpsCustomerDataService
{
    private const int MaxTransactionsPerPage = 100;

    private readonly ISupportRepository _support;
    private readonly IOpsAccessService _access;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ITransactionRepository _transactions;
    private readonly IBudgetRepository _budgets;
    private readonly ICategoryRepository _categories;
    private readonly IPushNotificationService _push;
    private readonly IAuditLogService _audit;
    private readonly IUserRepository _users;
    private readonly ISystemConfigProvider _config;

    public OpsCustomerDataService(
        ISupportRepository support,
        IOpsAccessService access,
        IFinancialAccountRepository accounts,
        ITransactionRepository transactions,
        IBudgetRepository budgets,
        ICategoryRepository categories,
        IPushNotificationService push,
        IAuditLogService audit,
        IUserRepository users,
        ISystemConfigProvider config)
    {
        _users = users;
        _config = config;
        _support = support;
        _access = access;
        _accounts = accounts;
        _transactions = transactions;
        _budgets = budgets;
        _categories = categories;
        _push = push;
        _audit = audit;
    }

    /// <summary>Người gọi phải có quyền và đang phụ trách ca. Manager cũng phải nhận ca trước, vì khách đồng ý với người phụ trách ca đó.</summary>
    private async Task<SupportTicket> RequireAssignedTicketAsync(Guid actorId, Guid ticketId, OpsCapability capability, CancellationToken ct)
    {
        var actor = await _access.RequireAsync(actorId, capability, ct);
        var ticket = await _support.GetByIdAsync(ticketId, ct) ?? throw new NotFoundException("SupportTicket", ticketId);
        if (ticket.RequesterUserId == actor.Id || ticket.AssigneeUserId != actor.Id)
        {
            throw new ForbiddenException("Bạn không phụ trách yêu cầu này.");
        }

        return ticket;
    }

    /// <summary>Khách đã bị khóa hoặc đã yêu cầu xóa tài khoản không tự thu hồi quyền được nữa, nên đồng ý trước đó không còn được tính.</summary>
    private async Task EnsureCustomerActiveAsync(SupportTicket ticket, CancellationToken ct)
    {
        if (await _users.GetByIdAsync(ticket.RequesterUserId, ct) is not { IsLocked: false })
        {
            throw new ForbiddenException("Tài khoản khách không còn hoạt động, không xem được dữ liệu.");
        }
    }

    /// <summary>Quyền còn hiệu lực cho đúng người, đúng ca, đúng phần dữ liệu. Thiếu một điều kiện là 403.</summary>
    private async Task<(SupportTicket Ticket, SupportAccessGrant Grant)> RequireGrantAsync(
        Guid actorId, Guid ticketId, OpsCapability capability, SupportAccessScope scope, CancellationToken ct)
    {
        var ticket = await RequireAssignedTicketAsync(actorId, ticketId, capability, ct);
        SupportTicketRules.EnsureNotClosed(ticket);
        await EnsureCustomerActiveAsync(ticket, ct);
        var now = DateTimeOffset.UtcNow;
        var grant = (await _support.ListGrantsAsync(ticketId, ct)).FirstOrDefault(g =>
            g.Status == SupportAccessStatus.Approved
            && g.RequestedByUserId == actorId
            && g.ExpiresAt > now
            && g.Scopes.HasFlag(scope));
        if (grant is null)
        {
            throw new ForbiddenException("Khách chưa đồng ý cho xem phần dữ liệu này, hoặc quyền đã hết hạn.");
        }

        return (ticket, grant);
    }

    /// <summary>Ghi trước khi trả dữ liệu: không ghi được thì không xem được.</summary>
    private async Task LogViewAsync(Guid actorId, SupportTicket ticket, SupportAccessGrant grant, SupportAccessScope scope, string? ip, CancellationToken ct)
    {
        _support.AddAccessLog(new SupportDataAccessLog
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            GrantId = grant.Id,
            CustomerUserId = ticket.RequesterUserId,
            ActorUserId = actorId,
            Scope = scope,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _support.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsCustomerDataViewed, actorId, ip, new { ticketId = ticket.Id, grantId = grant.Id, scope = scope.ToString() }, ct);
    }

    public async Task<SupportAccessRequestDto> RequestAccessAsync(Guid actorId, Guid ticketId, SupportAccessScope scopes, string? reason, string? ip, CancellationToken ct = default)
    {
        var ticket = await RequireAssignedTicketAsync(actorId, ticketId, OpsCapability.AccessCustomerData, ct);
        SupportTicketRules.EnsureNotClosed(ticket);
        await EnsureCustomerActiveAsync(ticket, ct);

        // Khách không mở được màn hình hỗ trợ khi tắt cờ, nên không gửi yêu cầu họ không trả lời được.
        if (!(await _config.GetAsync(ct)).Document.Features.Support)
        {
            throw new BusinessRuleException(SupportErrorCodes.Disabled, "Tính năng hỗ trợ chưa được bật.");
        }

        var all = SupportAccessScope.Accounts | SupportAccessScope.Transactions | SupportAccessScope.Budgets | SupportAccessScope.Draft;
        if (scopes == SupportAccessScope.None || (scopes & ~all) != 0)
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Phạm vi dữ liệu không hợp lệ.");
        }

        var cleanReason = SupportTicketRules.RequireText(reason, SupportAccessRules.MaxReasonLength, "lý do");
        var now = DateTimeOffset.UtcNow;
        var existing = await _support.ListGrantsAsync(ticketId, ct);
        var pending = existing.FirstOrDefault(g => g.Status == SupportAccessStatus.Pending);
        if (pending is not null)
        {
            if (pending.RequestExpiresAt > now)
            {
                throw new ConflictException(SupportErrorCodes.AccessRequestPending, "Yêu cầu trước vẫn đang chờ khách trả lời.");
            }

            pending.Status = SupportAccessStatus.Revoked;
            pending.RevokedAt = now;
            await _support.SaveChangesAsync(ct);
        }

        var grant = new SupportAccessGrant
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            RequestedByUserId = actorId,
            Scopes = scopes,
            Reason = cleanReason,
            Status = SupportAccessStatus.Pending,
            CreatedAt = now,
            RequestExpiresAt = now + SupportAccessRules.GrantLifetime,
        };
        _support.AddGrant(grant);
        await _support.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditEvents.OpsCustomerDataRequested, actorId, ip, new { ticketId, grantId = grant.Id, scopes = SupportAccessRules.ScopeNames(scopes) }, ct);

        await _push.NotifyAsync(
            ticket.RequesterUserId,
            "FinMate hỗ trợ",
            "Nhân viên hỗ trợ xin phép xem dữ liệu để xử lý yêu cầu của bạn.",
            new Dictionary<string, string> { ["action"] = "support_access_request", ["ticketId"] = ticketId.ToString(), ["requestId"] = grant.Id.ToString() },
            ct);

        return SupportAccessRules.ToDto(grant);
    }

    public async Task<IReadOnlyList<SupportAccessRequestDto>> ListRequestsAsync(Guid actorId, Guid ticketId, CancellationToken ct = default)
    {
        await RequireAssignedTicketAsync(actorId, ticketId, OpsCapability.AccessCustomerData, ct);
        return (await _support.ListGrantsAsync(ticketId, ct)).Select(SupportAccessRules.ToDto).ToList();
    }

    public async Task<IReadOnlyList<CustomerAccountDto>> GetAccountsAsync(Guid actorId, Guid ticketId, string? ip, CancellationToken ct = default)
    {
        var (ticket, grant) = await RequireGrantAsync(actorId, ticketId, OpsCapability.AccessCustomerData, SupportAccessScope.Accounts, ct);
        await LogViewAsync(actorId, ticket, grant, SupportAccessScope.Accounts, ip, ct);
        var accounts = await _accounts.GetListByUserAsync(ticket.RequesterUserId, ct);
        return accounts.Select(a => new CustomerAccountDto(a.Id, a.AccountName, a.AccountType.ToString(), a.BalanceCents, a.IsMonitored)).ToList();
    }

    public async Task<CustomerTransactionPageDto> GetTransactionsAsync(Guid actorId, Guid ticketId, DateTimeOffset? from, DateTimeOffset? to, string? cursor, int limit, string? ip, CancellationToken ct = default)
    {
        var (ticket, grant) = await RequireGrantAsync(actorId, ticketId, OpsCapability.AccessCustomerData, SupportAccessScope.Transactions, ct);
        await LogViewAsync(actorId, ticket, grant, SupportAccessScope.Transactions, ip, ct);
        var page = await _transactions.GetListAsync(
            new TransactionListFilter(ticket.RequesterUserId, null, null, null, null, from, to, cursor, Math.Clamp(limit, 1, MaxTransactionsPerPage)),
            ct);
        return new CustomerTransactionPageDto(
            page.Items.Select(t => new CustomerTransactionDto(
                t.Id,
                t.FinancialAccountId,
                t.AmountCents,
                t.TransactionType.ToString(),
                t.Source.ToString(),
                t.Status.ToString(),
                t.Category?.Name,
                t.MerchantName,
                t.Description,
                t.TransactedAt)).ToList(),
            page.NextCursor);
    }

    public async Task<IReadOnlyList<CustomerBudgetDto>> GetBudgetsAsync(Guid actorId, Guid ticketId, string? ip, CancellationToken ct = default)
    {
        var (ticket, grant) = await RequireGrantAsync(actorId, ticketId, OpsCapability.AccessCustomerData, SupportAccessScope.Budgets, ct);
        await LogViewAsync(actorId, ticket, grant, SupportAccessScope.Budgets, ip, ct);
        var (periodStart, _) = VietnamTime.MonthRange(DateTimeOffset.UtcNow);
        var budgets = await _budgets.GetListForUserAsync(ticket.RequesterUserId, ct);
        var periods = (await _budgets.GetPeriodsForUserAsync(ticket.RequesterUserId, periodStart, ct)).ToDictionary(p => p.BudgetId);
        return budgets.Select(b =>
        {
            periods.TryGetValue(b.Id, out var period);
            return new CustomerBudgetDto(b.Id, b.Category?.Name, period?.LimitCents ?? b.LimitCents, period?.SpentCents ?? 0, b.PeriodType.ToString());
        }).ToList();
    }

    public async Task<SupportDraftResult> CreateDraftAsync(Guid actorId, Guid ticketId, SupportDraftInput input, string? ip, CancellationToken ct = default)
    {
        var (ticket, grant) = await RequireGrantAsync(actorId, ticketId, OpsCapability.CreateDraftTransactions, SupportAccessScope.Draft, ct);
        var customerId = ticket.RequesterUserId;

        if (input.ClientRequestId is { } requestId && await _transactions.GetByClientRequestIdAsync(customerId, requestId, ct) is { } replay)
        {
            return new SupportDraftResult(replay.Id, AlreadyExisted: true);
        }

        var now = DateTimeOffset.UtcNow;
        if (input.AmountCents <= 0
            || input.TransactionType is not (TransactionType.Debit or TransactionType.Credit)
            || input.TransactedAt > now.AddDays(1)
            || input.MerchantName?.Length > 200
            || input.Description?.Length > 500)
        {
            throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Thông tin giao dịch không hợp lệ.");
        }

        if (grant.FinancialAccountId is not { } accountId || await _accounts.GetByIdAsync(accountId, customerId, ct) is null)
        {
            throw new BusinessRuleException(SupportErrorCodes.DraftAccountRequired, "Ví khách chọn không còn tồn tại.");
        }

        // Chỉ danh mục hệ thống: danh mục tự tạo của khách là dữ liệu riêng mà nhân viên chưa được xem.
        Category? category = null;
        if (input.CategoryId is { } categoryId)
        {
            category = await _categories.GetByIdAsync(categoryId, ct);
            if (category is null || category.UserId is not null || !category.IsActive)
            {
                throw new BusinessRuleException(SupportErrorCodes.InvalidInput, "Danh mục không hợp lệ.");
            }
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            ClientRequestId = input.ClientRequestId,
            UserId = customerId,
            FinancialAccountId = accountId,
            CategoryId = category?.Id,
            AmountCents = input.AmountCents,
            TransactionType = input.TransactionType,
            Source = TransactionSource.Support,
            Status = TransactionStatus.Draft,
            MerchantName = input.MerchantName?.Trim(),
            Description = input.Description?.Trim(),
            TransactedAt = input.TransactedAt,
            SupportTicketId = ticket.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _transactions.AddAsync(transaction, ct);
        transaction.Category = category;

        // Audit chỉ có id, không có số tiền.
        await _audit.LogAsync(AuditEvents.OpsDraftTransactionCreated, actorId, ip, new { ticketId, grantId = grant.Id, transactionId = transaction.Id }, ct);

        var review = TransactionReviewPushBuilder.Build(transaction, category?.Name, confidence: null);
        await _push.NotifyAsync(customerId, review.Title, review.Body, review.Data, ct);

        return new SupportDraftResult(transaction.Id, AlreadyExisted: false);
    }
}
