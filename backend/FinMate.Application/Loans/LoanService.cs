using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Refunds;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;

namespace FinMate.Application.Loans;

/// <summary>
/// Sổ vay (<c>/api/v1/loans</c>). Phase 90: mọi lệnh ghi đọc, tính, lưu MỘT lần (D2) trên <see cref="LoanPolicy"/>; việc bên ngoài (tóm tắt ngày,
/// cảnh báo ngân sách, dự báo) chạy sau khi lưu. Hai lượt cùng khoản đụng <c>xmin</c> thì <see cref="RetryingLoanService"/> chạy lại.
/// </summary>
public interface ILoanService
{
    /// <summary>Khoản còn sống của người dùng, mới tạo trước. Chat (<c>TryRepayAsync</c>) dùng hàm này.</summary>
    Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Q12: <c>GET /loans</c> với lọc tuỳ chọn; không phân trang (người dùng có ít khoản).</summary>
    Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, LoanStatus? status, LoanDirection? direction, CancellationToken ct = default);

    /// <summary><c>GET /loans/{id}</c>: khoản kèm các dòng còn sống.</summary>
    Task<LoanDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary><c>GET /loans/summary</c> (D17).</summary>
    Task<LoanSummaryDto> SummaryAsync(Guid userId, CancellationToken ct = default);

    /// <summary>BR-DEBT-03: nợ cũ (không đụng ví), kèm giao dịch giải ngân mới, hoặc từ giao dịch có sẵn (Q6).</summary>
    Task<LoanWriteResult> CreateAsync(Guid userId, LoanInput input, CancellationToken ct = default);

    /// <summary>Sửa thông tin; <c>principalCents</c> là tổng gốc mới (S2).</summary>
    Task<LoanDto> UpdateAsync(Guid userId, Guid id, LoanInput input, CancellationToken ct = default);

    /// <summary>Q4: xoá mềm khoản và các dòng; giao dịch đã gắn ở lại với danh mục <c>loan</c> và gắn lại được.</summary>
    Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Một lần trả / thu nợ. Có lãi suất thì phần vượt gốc trong ngưỡng lãi thành giao dịch lãi riêng (S1).</summary>
    Task<LoanWriteResult> RepayAsync(Guid userId, Guid id, LoanEntryInput input, CancellationToken ct = default);

    /// <summary>Cho mượn thêm / vay thêm (BR-DEBT-04).</summary>
    Task<LoanWriteResult> AddPrincipalAsync(Guid userId, Guid id, LoanEntryInput input, CancellationToken ct = default);

    /// <summary>BR-DEBT-06, 08: gắn một giao dịch đã xác nhận có sẵn làm dòng <paramref name="kind"/>; trả kèm lãi thì tách (S1, Q14).</summary>
    Task<LoanDto> LinkAsync(Guid userId, Guid id, Guid transactionId, LoanEntryKind kind, CancellationToken ct = default);

    /// <summary>Xoá một dòng (cũng là gỡ liên kết); giao dịch ở lại. Trả null khi khoản thành rỗng và bị xoá theo (D5).</summary>
    Task<LoanDto?> DeleteEntryAsync(Guid userId, Guid id, Guid entryId, CancellationToken ct = default);

    /// <summary>BR-DEBT-10: đóng là <c>written_off</c>; không tạo giao dịch, không đổi số dư.</summary>
    Task<LoanDto> WriteOffAsync(Guid userId, Guid id, CancellationToken ct = default);
}

/// <summary>Phụ thuộc của <see cref="LoanService"/>, gom lại để constructor gọn. <see cref="AfterSave"/> và <see cref="Config"/> null được trong test.</summary>
public sealed record LoanServiceDependencies(
    ILoanRepository Loans,
    ICategoryRepository Categories,
    ITransactionRepository Transactions,
    IFinancialAccountRepository Accounts,
    LoanTransactionLink Link,
    RefundAfterSave? AfterSave = null,
    ISystemConfigProvider? Config = null);

public sealed class LoanService : ILoanService
{
    public const long MaxCents = 100_000_000_000L;
    public const int MaxNoteLength = 500;

    private readonly ILoanRepository _loans;
    private readonly ICategoryRepository _categories;
    private readonly ITransactionRepository _transactions;
    private readonly IFinancialAccountRepository _accounts;
    private readonly LoanTransactionLink _link;
    private readonly RefundAfterSave? _afterSave;
    private readonly ISystemConfigProvider? _config;
    private readonly Func<DateTimeOffset> _now;

    public LoanService(LoanServiceDependencies deps)
        : this(deps, () => DateTimeOffset.UtcNow)
    {
    }

    internal LoanService(LoanServiceDependencies deps, Func<DateTimeOffset> now)
    {
        (_loans, _categories, _transactions, _accounts, _link, _afterSave, _config) = deps;
        _now = now;
    }

    public async Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, CancellationToken ct = default)
        => (await _loans.GetListAsync(userId, ct)).Select(l => LoanDto.From(l, Today())).ToList();

    public async Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, LoanStatus? status, LoanDirection? direction, CancellationToken ct = default)
        => (await _loans.GetListAsync(userId, ct))
            .Where(l => (status is null || l.Status == status) && (direction is null || l.Direction == direction))
            .Select(l => LoanDto.From(l, Today()))
            .ToList();

    public async Task<LoanDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => LoanDetailDto.From(await LoadAsync(userId, id, ct), Today());

    public async Task<LoanSummaryDto> SummaryAsync(Guid userId, CancellationToken ct = default)
        => LoanSummaryDto.From(await _loans.GetListAsync(userId, ct), Today());

    public async Task<LoanWriteResult> CreateAsync(Guid userId, LoanInput input, CancellationToken ct = default)
    {
        Validate(input);
        if (input.CreateTransaction is not null && input.TransactionId is not null)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Chỉ chọn một trong hai: tạo giao dịch mới hoặc liên kết giao dịch có sẵn.");
        }

        if (input.ClientRequestId is { } requestId && await _loans.GetByClientRequestAsync(userId, requestId, ct) is { } existing)
        {
            return new LoanWriteResult(LoanDto.From(existing, Today()), AlreadyExisted: true);
        }

        var now = _now();
        var loan = new Loan { Id = Guid.NewGuid(), UserId = userId, ClientRequestId = input.ClientRequestId, CreatedAt = now, UpdatedAt = now };
        Apply(loan, input);

        var effects = input switch
        {
            { TransactionId: { } transactionId } => await _transactions.RunWithTransactionsLockedAsync(
                userId, [transactionId], rows => CreateFromTransactionAsync(userId, loan, input, rows[transactionId], now, ct), ct),
            { CreateTransaction: { } create } => await CreateWithNewTransactionAsync(userId, loan, input, create, now, ct),
            _ => await CreateWithoutTransactionAsync(loan, input, now, ct),
        };

        await AfterSaveAsync(userId, effects, ct);
        return new LoanWriteResult(LoanDto.From(loan, Today()), AlreadyExisted: false);
    }

    public async Task<LoanDto> UpdateAsync(Guid userId, Guid id, LoanInput input, CancellationToken ct = default)
    {
        Validate(input);
        var loan = await LoadAsync(userId, id, ct);
        var now = _now();

        // S2: principalCents là tổng gốc mới; dòng giải ngân đầu đổi đúng bằng chênh lệch.
        var diff = input.PrincipalCents - loan.PrincipalCents;
        var first = loan.Live(LoanEntryKind.Principal).OrderBy(r => r.PaidAt).ThenBy(r => r.CreatedAt).FirstOrDefault();
        if (diff != 0)
        {
            if (input.PrincipalCents < loan.RepaidCents)
            {
                throw new BusinessRuleException(LoanErrorCodes.PrincipalBelowPaid, "Số tiền gốc không được nhỏ hơn số đã trả.");
            }

            if (first is null || first.TransactionId is not null)
            {
                throw new BusinessRuleException(LoanErrorCodes.PrincipalLinked, "Lần giải ngân đầu đã gắn giao dịch. Hãy sửa số tiền ở giao dịch đó.");
            }

            if (first.AmountCents + diff <= 0)
            {
                throw new BusinessRuleException(LoanErrorCodes.Invalid, "Số tiền gốc mới làm lần giải ngân đầu không còn dương.");
            }

            LoanPolicy.ChangeAmount(loan, LoanEntryKind.Principal, first.AmountCents, first.AmountCents + diff, now);
            first.AmountCents += diff;
        }

        if (input.StartDate is { } start && first is { TransactionId: null })
        {
            first.PaidAt = VietnamTime.DayRange(start).Start;   // dòng đầu chưa gắn giao dịch đi theo ngày bắt đầu
        }

        if (loan.DueDate != input.DueDate)
        {
            loan.DueNotifiedAt = null;   // hạn mới thì được nhắc lại
        }

        Apply(loan, input);
        loan.UpdatedAt = now;
        await _loans.SaveAsync(ct);
        return LoanDto.From(loan, Today());
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var loan = await LoadAsync(userId, id, ct);
        var now = _now();
        loan.DeletedAt = now;
        loan.UpdatedAt = now;
        foreach (var entry in loan.Repayments.Where(e => e.DeletedAt is null))
        {
            entry.DeletedAt = now;   // nhả giao dịch: gắn được vào khoản khác (UNIQUE chỉ trên dòng còn sống)
        }

        await _loans.SaveAsync(ct);
    }

    public async Task<LoanWriteResult> RepayAsync(Guid userId, Guid id, LoanEntryInput input, CancellationToken ct = default)
    {
        EnsureAmount(input.AmountCents);
        var loan = await LoadAsync(userId, id, ct);
        if (input.ClientRequestId is { } requestId && await _loans.EntryClientRequestExistsAsync(loan.Id, requestId, ct))
        {
            return new LoanWriteResult(LoanDto.From(loan, Today()), AlreadyExisted: true);
        }

        var now = _now();
        var at = input.At ?? now;
        var tolerance = _config is null ? new LoanSettings().InterestToleranceCents : (await _config.GetAsync(ct)).Document.Loans.InterestToleranceCents;
        var (principalPart, interest) = LoanPolicy.SplitRepayment(loan, input.AmountCents, LoanInterest.Accrued(loan, VietnamTime.DateOf(at)), tolerance);

        var entry = NewEntry(loan, LoanEntryKind.Repayment, principalPart, at, input.ClientRequestId, now);
        var effects = RefundEffects.None;
        if (input.FinancialAccountId is { } accountId)
        {
            var account = await _accounts.GetByIdAsync(accountId, userId, ct) ?? throw new NotFoundException("FinancialAccount", accountId);
            var transaction = await _link.NewAsync(userId, loan, LoanEntryKind.Repayment, principalPart, account, await LoanCategoryAsync(ct), at, now, ct);
            entry.TransactionId = transaction.Id;
            _loans.Track(transaction);
            effects = LoanTransactionLink.Touched(transaction);
            if (interest > 0)
            {
                var interestTransaction = LoanTransactionLink.NewInterest(
                    userId, loan, entry.Id, interest, account, await InterestCategoryAsync(loan, ct), at, moveBalance: true, TransactionSource.Manual, now);
                entry.InterestTransactionId = interestTransaction.Id;
                _loans.Track(interestTransaction);
                effects = LoanTransactionLink.Merge(effects, await _link.RecomputeForNewAsync(userId, interestTransaction, ct));
            }
        }

        // Không ví: chỉ phần gốc vào sổ. Phần lãi (nếu có) không có ví để ghi, nên không thành giao dịch.
        LoanPolicy.Add(loan, LoanEntryKind.Repayment, principalPart, now);
        loan.Repayments.Add(entry);
        _loans.Track(entry);
        await _loans.SaveAsync(ct);   // một lần: dòng, tổng của khoản (xmin), ví, giao dịch, kỳ ngân sách
        await AfterSaveAsync(userId, effects, ct);
        return new LoanWriteResult(LoanDto.From(loan, Today()), AlreadyExisted: false);
    }

    public async Task<LoanWriteResult> AddPrincipalAsync(Guid userId, Guid id, LoanEntryInput input, CancellationToken ct = default)
    {
        EnsureAmount(input.AmountCents);
        var loan = await LoadAsync(userId, id, ct);
        if (input.ClientRequestId is { } requestId && await _loans.EntryClientRequestExistsAsync(loan.Id, requestId, ct))
        {
            return new LoanWriteResult(LoanDto.From(loan, Today()), AlreadyExisted: true);
        }

        var now = _now();
        var at = input.At ?? now;
        var entry = NewEntry(loan, LoanEntryKind.Principal, input.AmountCents, at, input.ClientRequestId, now);
        var effects = RefundEffects.None;
        if (input.FinancialAccountId is { } accountId)
        {
            var account = await _accounts.GetByIdAsync(accountId, userId, ct) ?? throw new NotFoundException("FinancialAccount", accountId);
            var transaction = await _link.NewAsync(userId, loan, LoanEntryKind.Principal, input.AmountCents, account, await LoanCategoryAsync(ct), at, now, ct);
            entry.TransactionId = transaction.Id;
            _loans.Track(transaction);
            effects = LoanTransactionLink.Touched(transaction);
        }

        LoanPolicy.Add(loan, LoanEntryKind.Principal, input.AmountCents, now);
        loan.Repayments.Add(entry);
        _loans.Track(entry);
        await _loans.SaveAsync(ct);
        await AfterSaveAsync(userId, effects, ct);
        return new LoanWriteResult(LoanDto.From(loan, Today()), AlreadyExisted: false);
    }

    /// <summary>
    /// Khoá dòng giao dịch trước (thứ tự giao dịch → khoản → ví → kỳ ngân sách, như Phase 82), đổi sang <c>loan</c> và tính lại kỳ ngân sách (D21).
    /// Trả vượt gốc còn lại trong ngưỡng lãi (S1, Q14): giao dịch giữ phần gốc, phần lãi thành giao dịch riêng cùng ví, cùng ngày; số dư ví không đổi
    /// vì giao dịch gốc đã mang cả số.
    /// </summary>
    public async Task<LoanDto> LinkAsync(Guid userId, Guid id, Guid transactionId, LoanEntryKind kind, CancellationToken ct = default)
    {
        var loanCategory = await LoanCategoryAsync(ct);
        var (loan, effects) = await _transactions.RunWithTransactionsLockedAsync(userId, [transactionId], async rows =>
        {
            var transaction = rows[transactionId];
            var loan = await LoadAsync(userId, id, ct);
            LoanPolicy.EnsureEligible(transaction, await _loans.IsTransactionLinkedAsync(userId, transaction.Id, ct));
            LoanPolicy.EnsureDirection(loan.Direction, kind, transaction.TransactionType);
            var now = _now();

            var amount = transaction.AmountCents;
            var interest = 0L;
            if (kind == LoanEntryKind.Repayment)
            {
                var tolerance = _config is null ? new LoanSettings().InterestToleranceCents : (await _config.GetAsync(ct)).Document.Loans.InterestToleranceCents;
                (amount, interest) = LoanPolicy.SplitRepayment(loan, amount, LoanInterest.Accrued(loan, VietnamTime.DateOf(transaction.TransactedAt)), tolerance);
            }

            var entry = NewEntry(loan, kind, amount, transaction.TransactedAt, null, now);
            entry.TransactionId = transaction.Id;
            var moved = RefundEffects.None;
            if (interest > 0)
            {
                var account = await _accounts.GetByIdAsync(transaction.FinancialAccountId, userId, ct)
                    ?? throw new NotFoundException("FinancialAccount", transaction.FinancialAccountId);
                var interestTransaction = LoanTransactionLink.NewInterest(
                    userId, loan, entry.Id, interest, account, await InterestCategoryAsync(loan, ct), transaction.TransactedAt, moveBalance: false, transaction.Source, now);
                entry.InterestTransactionId = interestTransaction.Id;
                _loans.Track(interestTransaction);
                moved = await _link.RecomputeForNewAsync(userId, interestTransaction, ct);
                transaction.AmountCents = amount;   // số dư ví không đổi: hai giao dịch cộng lại đúng số ngân hàng báo
            }

            moved = LoanTransactionLink.Merge(moved, await _link.MoveToLoanAsync(userId, transaction, loanCategory, now, ct));
            LoanPolicy.Add(loan, kind, amount, now);
            loan.Repayments.Add(entry);
            _loans.Track(entry);
            await _loans.SaveAsync(ct);
            return (loan, moved);
        }, ct);

        await AfterSaveAsync(userId, effects, ct);
        return LoanDto.From(loan, Today());
    }

    public async Task<LoanDto?> DeleteEntryAsync(Guid userId, Guid id, Guid entryId, CancellationToken ct = default)
    {
        var loan = await LoadAsync(userId, id, ct);
        var entry = loan.Repayments.FirstOrDefault(e => e.Id == entryId && e.DeletedAt is null) ?? throw new NotFoundException("LoanEntry", entryId);
        var now = _now();
        var emptied = LoanPolicy.Remove(loan, entry.Kind, entry.AmountCents, now);
        entry.DeletedAt = now;   // xoá mềm; KHÔNG bỏ khỏi danh sách: FK bắt buộc làm EF xoá cứng dòng mồ côi
        if (emptied)
        {
            loan.DeletedAt = now;   // D5: khoản 0đ không có nghĩa
        }

        await _loans.SaveAsync(ct);
        return emptied ? null : LoanDto.From(loan, Today());
    }

    public async Task<LoanDto> WriteOffAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var loan = await LoadAsync(userId, id, ct);
        var before = loan.Status;
        LoanPolicy.WriteOff(loan, _now());
        if (before != loan.Status)
        {
            await _loans.SaveAsync(ct);
        }

        return LoanDto.From(loan, Today());
    }

    /// <summary>Q6 cách 2, D18: giao dịch có sẵn thành dòng giải ngân đầu; ngày bắt đầu = ngày giao dịch; danh mục thành <c>loan</c>.</summary>
    private async Task<RefundEffects> CreateFromTransactionAsync(Guid userId, Loan loan, LoanInput input, Transaction transaction, DateTimeOffset now, CancellationToken ct)
    {
        LoanPolicy.EnsureEligible(transaction, await _loans.IsTransactionLinkedAsync(userId, transaction.Id, ct));
        LoanPolicy.EnsureDirection(loan.Direction, LoanEntryKind.Principal, transaction.TransactionType);
        if (input.PrincipalCents != transaction.AmountCents)
        {
            throw new BusinessRuleException(LoanErrorCodes.AmountMismatch, "Số tiền gốc phải bằng số tiền của giao dịch được liên kết.");
        }

        loan.StartDate = VietnamTime.DateOf(transaction.TransactedAt);
        var entry = NewEntry(loan, LoanEntryKind.Principal, transaction.AmountCents, transaction.TransactedAt, input.ClientRequestId, now);
        entry.TransactionId = transaction.Id;
        AddFirst(loan, entry, now);
        var effects = await _link.MoveToLoanAsync(userId, transaction, await LoanCategoryAsync(ct), now, ct);
        _loans.Track(loan);
        await _loans.SaveAsync(ct);
        return effects;
    }

    /// <summary>Q6 cách 1: tạo giao dịch <c>loan</c> đã xác nhận từ ví; giao dịch, ví và khoản lưu cùng một lần.</summary>
    private async Task<RefundEffects> CreateWithNewTransactionAsync(
        Guid userId, Loan loan, LoanInput input, NewLoanTransaction create, DateTimeOffset now, CancellationToken ct)
    {
        var account = await _accounts.GetByIdAsync(create.FinancialAccountId, userId, ct)
            ?? throw new NotFoundException("FinancialAccount", create.FinancialAccountId);
        var at = create.TransactedAt ?? (input.StartDate is { } start ? VietnamTime.DayRange(start).Start : now);
        loan.StartDate = VietnamTime.DateOf(at);
        var transaction = await _link.NewAsync(userId, loan, LoanEntryKind.Principal, input.PrincipalCents, account, await LoanCategoryAsync(ct), at, now, ct);
        var entry = NewEntry(loan, LoanEntryKind.Principal, input.PrincipalCents, at, input.ClientRequestId, now);
        entry.TransactionId = transaction.Id;
        AddFirst(loan, entry, now);
        _loans.Track(loan);
        _loans.Track(transaction);
        await _loans.SaveAsync(ct);
        return LoanTransactionLink.Touched(transaction);
    }

    /// <summary>Cách 3: nợ cũ, không giao dịch, không đụng số dư (hành vi của <c>POST /loans</c> trước Phase 90).</summary>
    private async Task<RefundEffects> CreateWithoutTransactionAsync(Loan loan, LoanInput input, DateTimeOffset now, CancellationToken ct)
    {
        AddFirst(loan, NewEntry(loan, LoanEntryKind.Principal, input.PrincipalCents, VietnamTime.DayRange(loan.StartDate).Start, input.ClientRequestId, now), now);
        await _loans.AddAsync(loan, ct);
        return RefundEffects.None;
    }

    private static void AddFirst(Loan loan, LoanRepayment entry, DateTimeOffset now)
    {
        loan.PrincipalCents = 0;
        loan.RepaidCents = 0;
        LoanPolicy.Add(loan, LoanEntryKind.Principal, entry.AmountCents, now);
        loan.Repayments.Add(entry);
    }

    private static LoanRepayment NewEntry(Loan loan, LoanEntryKind kind, long amountCents, DateTimeOffset at, Guid? clientRequestId, DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(), LoanId = loan.Id, Kind = kind, AmountCents = amountCents, PaidAt = at, ClientRequestId = clientRequestId, CreatedAt = now,
        };

    private async Task<Loan> LoadAsync(Guid userId, Guid id, CancellationToken ct)
        => await _loans.GetOwnedAsync(id, userId, ct) ?? throw new NotFoundException("Loan", id);

    private async Task AfterSaveAsync(Guid userId, RefundEffects effects, CancellationToken ct)
    {
        if (_afterSave is not null)
        {
            await _afterSave.RunAsync(userId, effects, ct);
        }
    }

    private async Task<Category> LoanCategoryAsync(CancellationToken ct)
        => await _categories.GetSystemBySlugAsync(Category.LoanSlug, ct) ?? throw new InvalidOperationException("Thiếu danh mục hệ thống 'loan'.");

    private Task<Category?> InterestCategoryAsync(Loan loan, CancellationToken ct)
        => _categories.GetSystemBySlugAsync(LoanTransactionLink.InterestCategorySlug(loan.Direction), ct);

    private DateOnly Today() => VietnamTime.DateOf(_now());

    private static void EnsureAmount(long amountCents)
    {
        if (amountCents <= 0 || amountCents > MaxCents)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Số tiền không hợp lệ.");
        }
    }

    private static void Validate(LoanInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Title) || i.Title.Trim().Length > 100
            || string.IsNullOrWhiteSpace(i.Counterparty) || i.Counterparty.Trim().Length > 100)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Tên khoản vay và đối tác là bắt buộc (tối đa 100 ký tự).");
        }

        if (i.PrincipalCents <= 0 || i.PrincipalCents > MaxCents)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Số tiền gốc phải lớn hơn 0 và không quá 100 tỷ đồng.");
        }

        if (i.InterestRatePercentYear is < 0 or > 100)
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, "Lãi suất phải từ 0 đến 100 %/năm.");
        }

        if (i.Note is { Length: > MaxNoteLength })
        {
            throw new BusinessRuleException(LoanErrorCodes.Invalid, $"Ghi chú tối đa {MaxNoteLength} ký tự.");
        }
    }

    /// <summary>Chép trường thông tin. KHÔNG đụng <see cref="Loan.PrincipalCents"/>: gốc là tổng các dòng, chỉ <see cref="LoanPolicy"/> đổi nó.</summary>
    private void Apply(Loan loan, LoanInput i)
    {
        loan.Direction = i.Direction;
        loan.Title = i.Title.Trim();
        loan.Counterparty = i.Counterparty.Trim();
        loan.InterestRatePercentYear = i.InterestRatePercentYear;
        loan.DueDate = i.DueDate;
        loan.StartDate = i.StartDate ?? (loan.StartDate == default ? Today() : loan.StartDate);
        if (i.Note is not null)
        {
            loan.Note = string.IsNullOrWhiteSpace(i.Note) ? null : i.Note.Trim();
        }
    }
}

/// <summary>D2: hai lượt ghi cùng khoản vay (hoặc cùng ví, cùng kỳ ngân sách) đồng thời: <c>xmin</c> lệch thì lượt thua đọc lại và tính lại.</summary>
public sealed class RetryingLoanService(LoanService inner, IConcurrencyRetry retry) : ILoanService
{
    public Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, CancellationToken ct = default) => inner.ListAsync(userId, ct);

    public Task<IReadOnlyList<LoanDto>> ListAsync(Guid userId, LoanStatus? status, LoanDirection? direction, CancellationToken ct = default)
        => inner.ListAsync(userId, status, direction, ct);

    public Task<LoanDetailDto> GetAsync(Guid userId, Guid id, CancellationToken ct = default) => inner.GetAsync(userId, id, ct);

    public Task<LoanSummaryDto> SummaryAsync(Guid userId, CancellationToken ct = default) => inner.SummaryAsync(userId, ct);

    public Task<LoanWriteResult> CreateAsync(Guid userId, LoanInput input, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.CreateAsync(userId, input, token), ct);

    public Task<LoanDto> UpdateAsync(Guid userId, Guid id, LoanInput input, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.UpdateAsync(userId, id, input, token), ct);

    public Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
        => retry.ExecuteAsync(async token => { await inner.DeleteAsync(userId, id, token); return true; }, ct);

    public Task<LoanWriteResult> RepayAsync(Guid userId, Guid id, LoanEntryInput input, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.RepayAsync(userId, id, input, token), ct);

    public Task<LoanWriteResult> AddPrincipalAsync(Guid userId, Guid id, LoanEntryInput input, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.AddPrincipalAsync(userId, id, input, token), ct);

    public Task<LoanDto> LinkAsync(Guid userId, Guid id, Guid transactionId, LoanEntryKind kind, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.LinkAsync(userId, id, transactionId, kind, token), ct);

    public Task<LoanDto?> DeleteEntryAsync(Guid userId, Guid id, Guid entryId, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.DeleteEntryAsync(userId, id, entryId, token), ct);

    public Task<LoanDto> WriteOffAsync(Guid userId, Guid id, CancellationToken ct = default)
        => retry.ExecuteAsync(token => inner.WriteOffAsync(userId, id, token), ct);
}
