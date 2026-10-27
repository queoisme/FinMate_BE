using System.Text.Json;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Admin;

public class AiCategorySyncServiceTests
{
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IAiRulesAdminClient> _client = new();
    private readonly List<AiRulesRequest> _sent = new();

    public AiCategorySyncServiceTests()
    {
        _client
            .Setup(c => c.SendAsync(It.IsAny<AiRulesRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiRulesRequest, CancellationToken>((r, _) => _sent.Add(r))
            .ReturnsAsync((JsonElement?)null);
    }

    private AiCategorySyncService Service(bool enabled = true, params Category[] categories)
    {
        _categories
            .Setup(r => r.GetSystemListAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories.ToList());
        return new AiCategorySyncService(
            _categories.Object, _client.Object, new AiCategorySyncOptions(enabled), NullLogger<AiCategorySyncService>.Instance);
    }

    private static Category Cat(string slug, string name = "Tên", bool active = true) =>
        new() { Id = Guid.NewGuid(), Slug = slug, Name = name, IsSystem = true, IsActive = active };

    private static List<JsonElement> Items(AiRulesRequest request) =>
        ((JsonElement)request.Body!).GetProperty("categories").EnumerateArray().ToList();

    [Fact]
    public async Task SendsSlugLabelKindAndActiveFlagToTheCategoriesArea()
    {
        var service = Service(true, Cat("food", "Ăn uống"), Cat("income", "Thu nhập"), Cat("shopping", "Mua sắm", active: false));
        var adminId = Guid.NewGuid();

        await service.SyncAsync(adminId, "lý do", default);

        var request = _sent.Should().ContainSingle().Subject;
        (request.Method, request.Path, request.Area, request.ActorId).Should()
            .Be((HttpMethod.Put, "sync", AiRulesRequest.CategoriesArea, (Guid?)adminId));
        var items = Items(request).ToDictionary(i => i.GetProperty("slug").GetString()!);
        items["food"].GetProperty("kind").GetString().Should().Be("expense");
        items["food"].GetProperty("label").GetString().Should().Be("Ăn uống");
        items["income"].GetProperty("kind").GetString().Should().Be("income");
        items["shopping"].GetProperty("is_active").GetBoolean().Should().BeFalse();
        ((JsonElement)request.Body!).GetProperty("reason").GetString().Should().Be("lý do");
    }

    [Fact]
    public async Task LoanAndSlugsTheAiServiceCannotTakeAreSkippedWithoutBlockingTheRest()
    {
        var service = Service(true, Cat(Category.LoanSlug), Cat("pets2"), Cat("Bad Slug"), Cat("pets"));

        await service.SyncAsync(null, null, default);

        Items(_sent.Single()).Select(i => i.GetProperty("slug").GetString()).Should().Equal("pets");
    }

    [Fact]
    public async Task WithoutAdminTheSystemActorIsUsed()
    {
        await Service(true, Cat("food")).SyncAsync(null, null, default);

        _sent.Single().ActorId.Should().Be(AiCategorySyncService.SystemActorId);
    }

    [Fact]
    public async Task NothingToSyncMeansNoCall()
    {
        await Service(true, Cat(Category.LoanSlug)).SyncAsync(null, null, default);

        _sent.Should().BeEmpty(); // AI Service từ chối danh sách rỗng
    }

    [Fact]
    public async Task MoreThan200CategoriesAreSplitIntoBatches()
    {
        // 450 slug hợp lệ khác nhau, chỉ gồm a-z: ba chữ cái đủ cho 17.576 giá trị.
        var many = Enumerable.Range(0, 450)
            .Select(i => Cat($"{(char)('a' + i / 676)}{(char)('a' + i / 26 % 26)}{(char)('a' + i % 26)}"))
            .ToArray();

        await Service(true, many).SyncAsync(null, null, default);

        _sent.Select(r => Items(r).Count).Should().Equal(200, 200, 50);
    }

    [Fact]
    public async Task DisabledSyncDoesNothing()
    {
        var service = Service(false, Cat("food"));

        await service.SyncAsync(null, null, default);
        (await service.TrySyncAsync(null, null, default)).Should().BeFalse();

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TrySyncSwallowsAiFailuresButSyncDoesNot()
    {
        _client
            .Setup(c => c.SendAsync(It.IsAny<AiRulesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));
        var service = Service(true, Cat("food"));

        (await service.TrySyncAsync(null, null, default)).Should().BeFalse();
        await service.Invoking(s => s.SyncAsync(null, null, default)).Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public async Task TrySyncReportsSuccess()
    {
        (await Service(true, Cat("food")).TrySyncAsync(null, null, default)).Should().BeTrue();
    }

    [Fact]
    public async Task JobLogsRejectionsButLetsUnavailabilityReachHangfireForRetry()
    {
        var sync = new Mock<IAiCategorySyncService>();
        var job = new AiCategorySyncJob(sync.Object, NullLogger<AiCategorySyncJob>.Instance);

        sync.Setup(s => s.SyncAsync(null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AiRuleException(409, "category_conflict", "x"));
        await job.Invoking(j => j.RunAsync()).Should().NotThrowAsync();

        sync.Setup(s => s.SyncAsync(null, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AIServiceUnavailableException("down"));
        await job.Invoking(j => j.RunAsync()).Should().ThrowAsync<AIServiceUnavailableException>();
    }
}

public class SystemCategoryHandlersSyncTests
{
    [Theory]
    [InlineData(false, true)]  // đang bật, admin tắt: phải đồng bộ
    [InlineData(true, false)]  // đã bật sẵn, admin bật lại: không đổi gì, không đồng bộ
    public async Task ActivationSyncsOnlyWhenSomethingChanged(bool requested, bool shouldSync)
    {
        var category = new Category { Id = Guid.NewGuid(), Slug = "food", Name = "Ăn uống", IsSystem = true, IsActive = true };
        var repository = new Mock<ICategoryRepository>();
        repository.Setup(r => r.GetByIdAsync(category.Id, It.IsAny<CancellationToken>())).ReturnsAsync(category);
        var sync = new Mock<IAiCategorySyncService>();
        var adminId = Guid.NewGuid();
        var handler = new SetSystemCategoryActivationCommandHandler(repository.Object, Mock.Of<IAuditLogService>(), sync.Object);

        await handler.HandleAsync(new SetSystemCategoryActivationCommand(adminId, category.Id, requested, null));

        sync.Verify(
            s => s.TrySyncAsync(adminId, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            shouldSync ? Times.Once() : Times.Never());
    }
}
