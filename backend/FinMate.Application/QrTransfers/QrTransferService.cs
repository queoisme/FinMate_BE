using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.QrTransfers;

public interface IQrTransferService
{
    Task<IReadOnlyList<QrBankDto>> ListBanksAsync(CancellationToken ct = default);
    Task<QrTransferDto> CreateAsync(CreateQrTransferCommand command, CancellationToken ct = default);
    Task<IReadOnlyList<QrTransferDto>> ListAsync(Guid userId, QrTransferStatus? status, int limit, CancellationToken ct = default);
    Task<QrTransferDto> CancelAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Danh mục đã nhớ cho người nhận này, hoặc null nếu chưa có. Không có là trạng thái bình thường, không phải lỗi.</summary>
    Task<CounterpartyOverrideDto?> LookupOverrideAsync(Guid userId, string bankBin, string accountNumber, CancellationToken ct = default);

    Task<IReadOnlyList<CounterpartyOverrideDto>> ListOverridesAsync(Guid userId, CancellationToken ct = default);
    Task<CounterpartyOverrideDto> UpdateOverrideAsync(Guid userId, Guid id, Guid categoryId, CancellationToken ct = default);
    Task DeleteOverrideAsync(Guid userId, Guid id, CancellationToken ct = default);
}

public class QrTransferService : IQrTransferService
{
    /// <summary>Quá thời gian này mà chưa khớp thì coi là hết hạn (chỉ để hiển thị). Dài hơn cửa sổ khớp của <see cref="QrTransferLinker"/> để người dùng còn thấy bản ghi vừa lỡ.</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(24);

    private readonly IQrTransferRepository _repository;
    private readonly ICategoryRepository _categories;
    private readonly IValidator<CreateQrTransferCommand> _validator;

    public QrTransferService(IQrTransferRepository repository, ICategoryRepository categories, IValidator<CreateQrTransferCommand> validator)
    {
        _repository = repository;
        _categories = categories;
        _validator = validator;
    }

    public async Task<IReadOnlyList<QrBankDto>> ListBanksAsync(CancellationToken ct = default)
        => (await _repository.GetActiveBanksAsync(ct))
            .Select(v => new QrBankDto(v.Bank.Bin, v.Bank.DisplayName, v.Bank.DeeplinkAppCode, v.CanMatchNotifications))
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task<QrTransferDto> CreateAsync(CreateQrTransferCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);

        var banks = await BankNamesAsync(ct);
        foreach (var bin in new[] { command.ReceiverBankBin, command.SenderBankBin })
        {
            if (!banks.ContainsKey(bin))
            {
                throw new BusinessRuleException(QrErrorCodes.BankUnsupported, "Ngân hàng này chưa được hỗ trợ mở app tự động.");
            }
        }

        var account = command.ReceiverAccountNumber.Trim();
        var receiverName = string.IsNullOrWhiteSpace(command.ReceiverName) ? null : command.ReceiverName.Trim();
        var now = DateTimeOffset.UtcNow;

        Category? category = null;
        if (command.CategoryId is Guid chosen)
        {
            // Chọn tay là tín hiệu rõ ràng duy nhất: ghi đè luôn mapping của người nhận này, không đợi tới lúc khớp thông báo.
            category = await RequireUsableCategoryAsync(command.UserId, chosen, ct);
            await UpsertOverrideAsync(command.UserId, command.ReceiverBankBin, account, receiverName, category.Id, now, ct);
        }
        else if (await _repository.GetOverrideAsync(command.UserId, command.ReceiverBankBin, account, ct) is { } known)
        {
            // Đã từng chọn cho người này: tự gắn, không hỏi lại. Danh mục bị tắt/xóa thì bỏ qua thay vì gắn cái không còn dùng được.
            var remembered = await _categories.GetByIdAsync(known.CategoryId, ct);
            if (remembered is { IsActive: true } && (remembered.UserId is null || remembered.UserId == command.UserId))
            {
                category = remembered;
            }
        }

        var intent = new QrTransferIntent
        {
            UserId = command.UserId,
            ReceiverBankBin = command.ReceiverBankBin,
            ReceiverAccountNumber = account,
            ReceiverName = receiverName,
            SenderBankBin = command.SenderBankBin,
            AmountCents = command.AmountCents,
            Message = string.IsNullOrWhiteSpace(command.Message) ? null : command.Message.Trim(),
            CategoryId = category?.Id,
            Status = QrTransferStatus.Pending,
            ScannedAt = now,
            CreatedAt = now,
        };
        await _repository.AddIntentAsync(intent, ct);
        return ToDto(intent, banks, category is null ? null : new Dictionary<Guid, string> { [category.Id] = category.Name }, now);
    }

    public async Task<IReadOnlyList<QrTransferDto>> ListAsync(Guid userId, QrTransferStatus? status, int limit, CancellationToken ct = default)
    {
        var intents = await _repository.ListIntentsAsync(userId, status, Math.Clamp(limit, 1, 100), ct);
        var banks = await BankNamesAsync(ct);
        var categories = (await _categories.GetListForUserAsync(userId, ct)).ToDictionary(c => c.Id, c => c.Name);
        var now = DateTimeOffset.UtcNow;
        return intents.Select(i => ToDto(i, banks, categories, now)).ToList();
    }

    public async Task<QrTransferDto> CancelAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var intent = await _repository.GetIntentAsync(id, userId, ct) ?? throw new NotFoundException("QrTransfer", id);
        if (intent.Status == QrTransferStatus.Matched)
        {
            throw new ConflictException(QrErrorCodes.AlreadyMatched, "Lần quét này đã khớp với một giao dịch thật nên không hủy được.");
        }

        if (intent.Status == QrTransferStatus.Pending)
        {
            intent.Status = QrTransferStatus.Cancelled;
            await _repository.UpdateIntentAsync(intent, ct);
        }

        var categories = (await _categories.GetListForUserAsync(userId, ct)).ToDictionary(c => c.Id, c => c.Name);
        return ToDto(intent, await BankNamesAsync(ct), categories, DateTimeOffset.UtcNow);
    }

    public async Task<CounterpartyOverrideDto?> LookupOverrideAsync(Guid userId, string bankBin, string accountNumber, CancellationToken ct = default)
    {
        var mapping = await _repository.GetOverrideAsync(userId, bankBin, accountNumber.Trim(), ct);
        return mapping is null ? null : await ToOverrideDtoAsync(mapping, userId, ct);
    }

    public async Task<IReadOnlyList<CounterpartyOverrideDto>> ListOverridesAsync(Guid userId, CancellationToken ct = default)
    {
        var mappings = await _repository.ListOverridesAsync(userId, ct);
        var banks = await BankNamesAsync(ct);
        var categories = (await _categories.GetListForUserAsync(userId, ct)).ToDictionary(c => c.Id, c => c.Name);
        return mappings.Select(m => ToOverrideDto(m, banks, categories)).ToList();
    }

    public async Task<CounterpartyOverrideDto> UpdateOverrideAsync(Guid userId, Guid id, Guid categoryId, CancellationToken ct = default)
    {
        var mapping = await _repository.GetOverrideByIdAsync(id, userId, ct) ?? throw new NotFoundException("CounterpartyOverride", id);
        var category = await RequireUsableCategoryAsync(userId, categoryId, ct);

        mapping.CategoryId = category.Id;
        mapping.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateOverrideAsync(mapping, ct);
        return await ToOverrideDtoAsync(mapping, userId, ct);
    }

    public async Task DeleteOverrideAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var mapping = await _repository.GetOverrideByIdAsync(id, userId, ct) ?? throw new NotFoundException("CounterpartyOverride", id);
        await _repository.RemoveOverrideAsync(mapping, ct);
    }

    // ------------------------------------------------------------------ nội bộ
    private async Task<Dictionary<string, string>> BankNamesAsync(CancellationToken ct)
        => (await _repository.GetActiveBanksAsync(ct)).ToDictionary(v => v.Bank.Bin, v => v.Bank.DisplayName);

    private async Task<Category> RequireUsableCategoryAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var category = await _categories.GetByIdAsync(id, ct);
        if (category is null || !category.IsActive || (category.UserId is not null && category.UserId != userId))
        {
            throw new NotFoundException("Category", id);
        }

        return category;
    }

    private async Task UpsertOverrideAsync(Guid userId, string bin, string account, string? name, Guid categoryId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await _repository.GetOverrideAsync(userId, bin, account, ct);
        if (existing is null)
        {
            await _repository.AddOverrideAsync(new CounterpartyCategoryOverride
            {
                UserId = userId, BankBin = bin, AccountNumber = account, DisplayName = name, CategoryId = categoryId, CreatedAt = now, UpdatedAt = now,
            }, ct);
            return;
        }

        existing.CategoryId = categoryId;
        existing.DisplayName = name ?? existing.DisplayName;
        existing.UpdatedAt = now;
        await _repository.UpdateOverrideAsync(existing, ct);
    }

    private async Task<CounterpartyOverrideDto> ToOverrideDtoAsync(CounterpartyCategoryOverride mapping, Guid userId, CancellationToken ct)
    {
        var banks = await BankNamesAsync(ct);
        var categories = (await _categories.GetListForUserAsync(userId, ct)).ToDictionary(c => c.Id, c => c.Name);
        return ToOverrideDto(mapping, banks, categories);
    }

    private static CounterpartyOverrideDto ToOverrideDto(CounterpartyCategoryOverride m, IReadOnlyDictionary<string, string> banks, IReadOnlyDictionary<Guid, string> categories)
        => new(
            m.Id, m.BankBin, banks.GetValueOrDefault(m.BankBin, m.BankBin), m.AccountNumber, m.DisplayName, m.CategoryId,
            categories.GetValueOrDefault(m.CategoryId, "(danh mục đã bị xóa)"), m.UpdatedAt);

    private static QrTransferDto ToDto(
        QrTransferIntent i, IReadOnlyDictionary<string, string> banks, IReadOnlyDictionary<Guid, string>? categories, DateTimeOffset now)
        => new(
            i.Id,
            i.ReceiverBankBin, banks.GetValueOrDefault(i.ReceiverBankBin, i.ReceiverBankBin), i.ReceiverAccountNumber, i.ReceiverName,
            i.SenderBankBin, banks.GetValueOrDefault(i.SenderBankBin, i.SenderBankBin),
            i.AmountCents, i.Message,
            i.CategoryId, i.CategoryId is Guid cid && categories is not null ? categories.GetValueOrDefault(cid) : null,
            i.Status.ToString(),
            i.Status == QrTransferStatus.Pending && now - i.ScannedAt > PendingLifetime,
            i.ScannedAt, i.MatchedAt, i.MatchedTransactionId);
}
