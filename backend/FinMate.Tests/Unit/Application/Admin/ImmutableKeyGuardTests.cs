using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

/// <summary>
/// Hai khóa định danh mà hệ thống khác join vào: <c>provider_key</c> và <c>slug</c> của danh mục
/// hệ thống. Đổi chúng không làm hỏng gì ở tầng DB — ràng buộc vẫn thỏa, request vẫn 200 —
/// nhưng liên kết sang AI Service và sang seeder đứt im lặng. Đây là bộ test giữ chúng đứng yên.
/// </summary>
public class ImmutableKeyGuardTests
{
    private readonly Mock<IAuditLogService> _auditLogService = new();
    private readonly Guid _adminId = Guid.NewGuid();

    [Fact]
    public async Task ProviderKeyCannotChange()
    {
        var repository = new Mock<IProviderConfigRepository>();
        var config = new ProviderConfig
        {
            Id = Guid.NewGuid(),
            ProviderKey = "mb_bank",
            DisplayName = "MB Bank",
            PackageName = "com.mbmobile",
        };
        repository
            .Setup(r => r.GetByIdAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var handler = new UpdateProviderConfigCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateProviderConfigCommandValidator());

        var act = () => handler.HandleAsync(new UpdateProviderConfigCommand(
            _adminId, config.Id, "mb_bank_v2", null, null, null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.ImmutableField);
        config.ProviderKey.Should().Be("mb_bank");
    }

    [Fact]
    public async Task ResendingTheSameProviderKeyIsAccepted()
    {
        // Client PATCH thường gửi lại nguyên đối tượng vừa đọc về. Từ chối cả trường hợp đó
        // thì mọi lần sửa tên hiển thị đều hỏng.
        var repository = new Mock<IProviderConfigRepository>();
        var config = new ProviderConfig
        {
            Id = Guid.NewGuid(),
            ProviderKey = "mb_bank",
            DisplayName = "MB Bank",
            PackageName = "com.mbmobile",
        };
        repository
            .Setup(r => r.GetByIdAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var handler = new UpdateProviderConfigCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateProviderConfigCommandValidator());

        await handler.HandleAsync(new UpdateProviderConfigCommand(
            _adminId, config.Id, "mb_bank", "MB Bank mới", null, null, null));

        config.DisplayName.Should().Be("MB Bank mới");
    }

    [Fact]
    public async Task PackageNameTakenByAnotherProviderIsAConflict()
    {
        var repository = new Mock<IProviderConfigRepository>();
        var config = new ProviderConfig
        {
            Id = Guid.NewGuid(),
            ProviderKey = "mb_bank",
            DisplayName = "MB Bank",
            PackageName = "com.mbmobile",
        };
        repository
            .Setup(r => r.GetByIdAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        repository
            .Setup(r => r.ExistsByPackageNameAsync("com.VCB", config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new UpdateProviderConfigCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateProviderConfigCommandValidator());

        var act = () => handler.HandleAsync(new UpdateProviderConfigCommand(
            _adminId, config.Id, null, null, "com.VCB", null, null));

        (await act.Should().ThrowAsync<ConflictException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.PackageNameDuplicate);
    }

    [Fact]
    public async Task SystemCategorySlugCannotChange()
    {
        var repository = new Mock<ICategoryRepository>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Name = "Ăn uống",
            Slug = "food",
            IsSystem = true,
        };
        repository
            .Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var handler = new UpdateSystemCategoryCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateSystemCategoryCommandValidator(), Mock.Of<IAiCategorySyncService>());

        var act = () => handler.HandleAsync(new UpdateSystemCategoryCommand(
            _adminId, category.Id, null, "food_and_drink", null, null));

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .And.ErrorCode.Should().Be(AdminErrorCodes.ImmutableField);
        category.Slug.Should().Be("food");
    }

    [Fact]
    public async Task AUserOwnedCategoryIsInvisibleToTheAdminEndpoint()
    {
        // 404 chứ không phải 403: danh mục riêng của một người dùng không thuộc phạm vi admin,
        // và trả 403 sẽ tiết lộ rằng nó tồn tại.
        var repository = new Mock<ICategoryRepository>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Name = "Quỹ đen",
            Slug = "quy_den",
        };
        repository
            .Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(category);

        var handler = new UpdateSystemCategoryCommandHandler(
            repository.Object, _auditLogService.Object, new UpdateSystemCategoryCommandValidator(), Mock.Of<IAiCategorySyncService>());

        var act = () => handler.HandleAsync(new UpdateSystemCategoryCommand(
            _adminId, category.Id, "Đổi tên", null, null, null));

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
