using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Common.Utils;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentValidation;

namespace FinMate.Application.Groups;

public sealed record GroupCategoryDto(Guid Id, string Name, string Slug, string? IconName);

public sealed record GroupShareRuleDto(Guid Id, Guid? FinancialAccountId, Guid? CategoryId, DateTimeOffset CreatedAt);

public sealed record CreateGroupCategoryCommand(Guid UserId, Guid GroupId, string Name, string? IconName);

public class CreateGroupCategoryCommandValidator : AbstractValidator<CreateGroupCategoryCommand>
{
    private const int NameMaxLength = 100;
    private const int IconMaxLength = 50;

    public CreateGroupCategoryCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tên danh mục không được để trống.").MaximumLength(NameMaxLength).WithMessage($"Tên danh mục tối đa {NameMaxLength} ký tự.");
        RuleFor(x => x.IconName).MaximumLength(IconMaxLength).WithMessage($"Tên biểu tượng tối đa {IconMaxLength} ký tự.");
    }
}

public interface IGroupCategoryService
{
    Task<IReadOnlyList<GroupCategoryDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default);
    Task<GroupCategoryDto> CreateAsync(CreateGroupCategoryCommand command, CancellationToken ct = default);
    Task DeleteAsync(Guid groupId, Guid userId, Guid categoryId, CancellationToken ct = default);

    Task<IReadOnlyList<GroupShareRuleDto>> ListRulesAsync(Guid groupId, Guid userId, CancellationToken ct = default);

    /// <summary>Quy tắc của chính người gọi. Ví phải là ví của họ, danh mục phải là danh mục họ dùng được.</summary>
    Task<GroupShareRuleDto> CreateRuleAsync(Guid groupId, Guid userId, Guid? financialAccountId, Guid? categoryId, CancellationToken ct = default);

    Task DeleteRuleAsync(Guid groupId, Guid userId, Guid ruleId, CancellationToken ct = default);
}

public class GroupCategoryService : IGroupCategoryService
{
    private readonly IGroupFinanceRepository _finance;
    private readonly IGroupRepository _groups;
    private readonly IGroupAccessService _access;
    private readonly IFinancialAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly IValidator<CreateGroupCategoryCommand> _validator;

    public GroupCategoryService(
        IGroupFinanceRepository finance,
        IGroupRepository groups,
        IGroupAccessService access,
        IFinancialAccountRepository accounts,
        ICategoryRepository categories,
        IValidator<CreateGroupCategoryCommand> validator)
    {
        _finance = finance;
        _groups = groups;
        _access = access;
        _accounts = accounts;
        _categories = categories;
        _validator = validator;
    }

    public async Task<IReadOnlyList<GroupCategoryDto>> ListAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ViewOverview, ct);
        return (await _finance.ListCategoriesAsync(groupId, ct)).Select(ToDto).ToList();
    }

    public async Task<GroupCategoryDto> CreateAsync(CreateGroupCategoryCommand command, CancellationToken ct = default)
    {
        await _validator.ValidateAndThrowAsync(command, ct);
        await _access.RequireAsync(command.GroupId, command.UserId, GroupPermission.ManageCategories, ct);
        var slug = Slugify.ToSlug(command.Name);
        if (await _finance.CategorySlugExistsAsync(command.GroupId, slug, ct))
        {
            throw new ConflictException(GroupErrorCodes.CategoryDuplicate, "Nhóm đã có danh mục này.");
        }

        var now = DateTimeOffset.UtcNow;
        var category = new GroupCategory
        {
            Id = Guid.NewGuid(),
            GroupId = command.GroupId,
            Name = command.Name.Trim(),
            Slug = slug,
            IconName = command.IconName,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _finance.AddCategory(category);
        _groups.AddActivity(GroupService.NewActivity(command.GroupId, command.UserId, GroupActivityType.CategoryChanged, category.Id, now));
        await _groups.SaveChangesAsync(ct);
        return ToDto(category);
    }

    public async Task DeleteAsync(Guid groupId, Guid userId, Guid categoryId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ManageCategories, ct);
        var category = await _finance.GetCategoryAsync(groupId, categoryId, ct) ?? throw new NotFoundException("GroupCategory", categoryId);
        if (await _finance.CategoryHasActiveBudgetAsync(categoryId, ct))
        {
            throw new ConflictException(GroupErrorCodes.CategoryInUse, "Danh mục đang có ngân sách. Hãy xóa ngân sách trước.");
        }

        var now = DateTimeOffset.UtcNow;
        category.DeletedAt = now;
        category.UpdatedAt = now;
        _groups.AddActivity(GroupService.NewActivity(groupId, userId, GroupActivityType.CategoryChanged, categoryId, now));
        await _groups.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<GroupShareRuleDto>> ListRulesAsync(Guid groupId, Guid userId, CancellationToken ct = default)
    {
        await _access.RequireMemberAsync(groupId, userId, ct);
        return (await _finance.ListRulesAsync(groupId, userId, ct)).Select(ToDto).ToList();
    }

    public async Task<GroupShareRuleDto> CreateRuleAsync(Guid groupId, Guid userId, Guid? financialAccountId, Guid? categoryId, CancellationToken ct = default)
    {
        await _access.RequireAsync(groupId, userId, GroupPermission.ShareTransactions, ct);
        await EnsureRuleTargetsAsync(userId, financialAccountId, categoryId, ct);

        var now = DateTimeOffset.UtcNow;
        var rule = new GroupShareRule
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            UserId = userId,
            FinancialAccountId = financialAccountId,
            CategoryId = categoryId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        _finance.AddRule(rule);
        await _groups.SaveChangesAsync(ct);
        return ToDto(rule);
    }

    public async Task DeleteRuleAsync(Guid groupId, Guid userId, Guid ruleId, CancellationToken ct = default)
    {
        await _access.RequireMemberAsync(groupId, userId, ct);
        var rule = await _finance.GetRuleAsync(groupId, userId, ruleId, ct) ?? throw new NotFoundException("GroupShareRule", ruleId);
        rule.IsActive = false;
        rule.UpdatedAt = DateTimeOffset.UtcNow;
        await _groups.SaveChangesAsync(ct);
    }

    private async Task EnsureRuleTargetsAsync(Guid userId, Guid? financialAccountId, Guid? categoryId, CancellationToken ct)
    {
        if (financialAccountId is { } accountId && await _accounts.GetByIdAsync(accountId, userId, ct) is null)
        {
            throw new NotFoundException("FinancialAccount", accountId);
        }

        if (categoryId is { } id)
        {
            var category = await _categories.GetByIdAsync(id, ct);
            if (category is null || (category.UserId is not null && category.UserId != userId))
            {
                throw new NotFoundException("Category", id);
            }
        }
    }

    private static GroupCategoryDto ToDto(GroupCategory category) => new(category.Id, category.Name, category.Slug, category.IconName);

    private static GroupShareRuleDto ToDto(GroupShareRule rule) => new(rule.Id, rule.FinancialAccountId, rule.CategoryId, rule.CreatedAt);
}
