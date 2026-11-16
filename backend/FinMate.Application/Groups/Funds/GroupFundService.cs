using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups.Funds;

/// <param name="IsNegative">Có người đang ứng trước cho quỹ (BR-FUND-02).</param>
public sealed record GroupFundDto(
    Guid Id,
    string Name,
    string? Note,
    long BalanceCents,
    bool IsNegative,
    bool IsArchived,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateGroupFundCommand(Guid UserId, Guid GroupId, string Name, string? Note);

public sealed record UpdateGroupFundCommand(Guid UserId, Guid GroupId, Guid FundId, string? Name, string? Note);

internal static class GroupFundRules
{
    public const int NameMaxLength = 100;
    public const int NoteMaxLength = 500;

    public static void Name<T>(IRuleBuilder<T, string?> rule)
        => rule.Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Tên quỹ không được để trống.")
            .Must(n => n is null || n.Trim().Length <= NameMaxLength).WithMessage($"Tên quỹ tối đa {NameMaxLength} ký tự.");

    public static void Note<T>(IRuleBuilder<T, string?> rule)
        => rule.MaximumLength(NoteMaxLength).WithMessage($"Ghi chú tối đa {NoteMaxLength} ký tự.");
}

public class CreateGroupFundCommandValidator : AbstractValidator<CreateGroupFundCommand>
{
    public CreateGroupFundCommandValidator()
    {
        GroupFundRules.Name(RuleFor(x => x.Name));
        GroupFundRules.Note(RuleFor(x => x.Note));
    }
}

public class UpdateGroupFundCommandValidator : AbstractValidator<UpdateGroupFundCommand>
{
    public UpdateGroupFundCommandValidator()
    {
        When(x => x.Name is not null, () => GroupFundRules.Name(RuleFor(x => x.Name)));
        GroupFundRules.Note(RuleFor(x => x.Note));
    }
}

/// <summary>Quỹ chung của nhóm (Phase 85, BR-FUND-01..03). Xem cần <c>ViewOverview</c>; tạo, sửa, lưu trữ cần <c>ManageFunds</c>.</summary>
public interface IGroupFundService
{
    Task<IReadOnlyList<GroupFundDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupFundDto> GetAsync(Guid groupId, Guid userId, Guid fundId, CancellationToken ct = default);
    Task<GroupFundDto> CreateAsync(CreateGroupFundCommand command, CancellationToken ct = default);

    /// <summary>Đổi tên hoặc ghi chú; vẫn làm được với quỹ đã lưu trữ (D16). <c>Note</c> rỗng là xoá ghi chú, null là giữ nguyên.</summary>
    Task<GroupFundDto> UpdateAsync(UpdateGroupFundCommand command, CancellationToken ct = default);

    /// <summary>Chỉ khi số dư bằng 0 (BR-FUND-03). Lưu trữ lại một quỹ đã lưu trữ thì trả nguyên trạng.</summary>
    Task<GroupFundDto> ArchiveAsync(Guid groupId, Guid userId, Guid fundId, CancellationToken ct = default);
}

public class GroupFundService : IGroupFundService
{
    private readonly IGroupFundRepository _funds;
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly ISystemConfigProvider _config;
    private readonly IValidator<CreateGroupFundCommand> _createValidator;
    private readonly IValidator<UpdateGroupFundCommand> _updateValidator;

    public GroupFundService(
        IGroupFundRepository funds,
        IGroupRepository groups,
        IGroupAccessService access,
        ISystemConfigProvider config,
        IValidator<CreateGroupFundCommand> createValidator,
        IValidator<UpdateGroupFundCommand> updateValidator)
    {
        _funds = funds;
        _groups = groups;
        _access = access;
        _config = config;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
    }

    public async Task<IReadOnlyList<GroupFundDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        return (await _funds.ListAsync(groupId, ct)).Select(ToDto).ToList();
    }

    public async Task<GroupFundDto> GetAsync(Guid groupId, Guid userId, Guid fundId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        return ToDto(await RequireFundAsync(groupId, fundId, ct));
    }

    public async Task<GroupFundDto> CreateAsync(CreateGroupFundCommand command, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageFunds, ct);
        var max = (await _config.GetAsync(ct)).Document.Groups.MaxFundsPerGroup;
        if (await _funds.CountActiveAsync(command.GroupId, ct) >= max)
        {
            throw new BusinessRuleException(GroupErrorCodes.TooManyFunds, $"Mỗi nhóm có tối đa {max} quỹ đang dùng.");
        }

        var now = DateTimeOffset.UtcNow;
        var fund = new GroupFund
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            Name = command.Name.Trim(),
            Note = NormalizeNote(command.Note),
            CreatedByUserId = command.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _funds.Add(fund);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.FundCreated, fund.Id, now));
        await _groups.SaveChangesAsync(ct);
        return ToDto(fund);
    }

    public async Task<GroupFundDto> UpdateAsync(UpdateGroupFundCommand command, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageFunds, ct);
        var fund = await RequireFundAsync(command.GroupId, command.FundId, ct);
        var now = DateTimeOffset.UtcNow;
        if (command.Name is not null)
        {
            fund.Name = command.Name.Trim();
        }

        if (command.Note is not null)
        {
            fund.Note = NormalizeNote(command.Note);
        }

        fund.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.FundUpdated, fund.Id, now));
        await _groups.SaveChangesAsync(ct);
        return ToDto(fund);
    }

    public async Task<GroupFundDto> ArchiveAsync(Guid groupId, Guid userId, Guid fundId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ManageFunds, ct);
        var fund = await RequireFundAsync(groupId, fundId, ct);
        if (fund.IsArchived)
        {
            return ToDto(fund);
        }

        // Kiểm trên số đã đọc; một dòng ghi đồng thời làm xmin lệch nên lượt lưu này ném 409, và CHECK của DB chặn thêm một lớp.
        if (fund.BalanceCents != 0)
        {
            throw new BusinessRuleException(GroupErrorCodes.FundNotSettled, "Quỹ còn tiền hoặc đang âm. Hãy góp, rút hoặc điều chỉnh về 0 trước khi lưu trữ.");
        }

        var now = DateTimeOffset.UtcNow;
        fund.ArchivedAt = now;
        fund.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.FundArchived, fund.Id, now));
        await _groups.SaveChangesAsync(ct);
        return ToDto(fund);
    }

    internal static GroupFundDto ToDto(GroupFund fund)
        => new(fund.Id, fund.Name, fund.Note, fund.BalanceCents, fund.BalanceCents < 0, fund.IsArchived, fund.ArchivedAt, fund.CreatedAt, fund.UpdatedAt);

    private async Task<GroupFund> RequireFundAsync(Guid groupId, Guid fundId, CancellationToken ct)
        => await _funds.GetAsync(groupId, fundId, ct) ?? throw new NotFoundException("GroupFund", fundId);

    private static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
