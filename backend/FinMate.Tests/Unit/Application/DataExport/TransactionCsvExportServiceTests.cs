using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.DataExport;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.DataExport;

/// <summary>Phase 91: kiểm tra trước khi ghi CSV (BR-EXP-01, S1, S3) — cờ, tham số, khoảng ngày, số dòng, giới hạn mỗi giờ, và chỉ lần thành công bị đếm.</summary>
public class TransactionCsvExportServiceTests
{
    private static readonly Guid User = Guid.NewGuid();
    private readonly Mock<IDataExportRepository> _repository = new();
    private readonly Mock<IFinancialAccountRepository> _accounts = new();
    private readonly Mock<ICategoryRepository> _categories = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly JsonMapCache _cache = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 25, 3, 0, 0, TimeSpan.Zero));
    private SystemConfigDocument _document = new();

    private TransactionCsvExportService Service()
    {
        var config = new Mock<ISystemConfigProvider>();
        config.Setup(c => c.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new SystemConfigSnapshot(1, _document, null));
        return new TransactionCsvExportService(_repository.Object, _accounts.Object, _categories.Object, config.Object,
            new CsvExportRateLimiter(_cache, _clock), _audit.Object, _clock);
    }

    private static TransactionCsvRequest Request(DateOnly? from = null, DateOnly? to = null, string? status = null, string? delimiter = null,
        Guid? account = null, Guid? category = null) => new(from, to, account, category, status, delimiter);

    [Fact]
    public async Task Defaults_AreThisVietnamMonth_ConfirmedOnly_AndComma()
    {
        var plan = await Service().PrepareAsync(User, Request(), null);

        plan.Delimiter.Should().Be(CsvDelimiter.Comma);
        plan.Filter.Statuses.Should().Equal(TransactionStatus.Confirmed);
        plan.Filter.Start.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(7)));
        plan.Filter.End.Should().Be(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(7)));
        plan.FileName.Should().Be("finmate-giao-dich-2026-11-01-2026-11-30.csv");
        _audit.Verify(a => a.LogAsync(AuditEvents.DataExportTransactionsCsv, User, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("semicolon", "all", CsvDelimiter.Semicolon, 2)]
    [InlineData("COMMA", "Draft", CsvDelimiter.Comma, 1)]
    public async Task DelimiterAndStatus_AreParsedCaseInsensitively(string delimiter, string status, CsvDelimiter expected, int statuses)
    {
        var plan = await Service().PrepareAsync(User, Request(status: status, delimiter: delimiter), null);

        plan.Delimiter.Should().Be(expected);
        plan.Filter.Statuses.Should().HaveCount(statuses);
    }

    [Theory]
    [InlineData("tab", null)]
    [InlineData(null, "rejected")]
    public async Task UnknownDelimiterOrStatus_Is400(string? delimiter, string? status)
    {
        await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(status: status, delimiter: delimiter), null))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ToBeforeFrom_Is400()
    {
        await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 9)), null))
            .Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task MoreThanMaxRangeDays_Is422_ButExactly366Passes()
    {
        var from = new DateOnly(2025, 11, 25);

        (await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(from, from.AddDays(366)), null))
            .Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(DataExportErrorCodes.RangeTooLarge);
        await Service().PrepareAsync(User, Request(from, from.AddDays(365)), null);
    }

    [Fact]
    public async Task MoreThanMaxRows_Is422_AndDoesNotUseUpTheHourlyAllowance()
    {
        _document = new SystemConfigDocument { DataExport = new DataExportSettings { MaxRows = 1_000, CsvPerHour = 1 } };
        _repository.Setup(r => r.CountTransactionsAsync(User, It.IsAny<TransactionExportFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_001);

        (await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(), null))
            .Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("1.000");

        _repository.Setup(r => r.CountTransactionsAsync(User, It.IsAny<TransactionExportFilter>(), It.IsAny<CancellationToken>())).ReturnsAsync(1_000);
        await Service().PrepareAsync(User, Request(), null);
    }

    [Fact]
    public async Task TheHourlyLimit_Is429_AndFreesUpAfterAnHour()
    {
        _document = new SystemConfigDocument { DataExport = new DataExportSettings { CsvPerHour = 2 } };
        await Service().PrepareAsync(User, Request(), null);
        _clock.Now = _clock.Now.AddMinutes(30);
        await Service().PrepareAsync(User, Request(), null);

        var thrown = await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(), null)).Should().ThrowAsync<TooManyRequestsException>();
        thrown.Which.ErrorCode.Should().Be(DataExportErrorCodes.RateLimited);
        thrown.Which.RetryAt.Should().Be(_clock.Now.AddMinutes(30));

        _clock.Now = _clock.Now.AddMinutes(31);
        await Service().PrepareAsync(User, Request(), null);
        await Service().PrepareAsync(Guid.NewGuid(), Request(), null);   // người khác có hạn mức riêng
    }

    [Fact]
    public async Task TheFeatureSwitch_Off_Is422()
    {
        _document = new SystemConfigDocument { Features = new FeatureSettings { DataExport = false } };

        (await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(), null))
            .Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be(DataExportErrorCodes.Disabled);
    }

    [Fact]
    public async Task AnotherUsersWalletOrCategory_Is404_ButASystemCategoryIsFine()
    {
        var wallet = Guid.NewGuid();
        var mine = new Category { Id = Guid.NewGuid(), UserId = User, Name = "Riêng", Slug = "x" };
        var theirs = new Category { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Name = "Của người khác", Slug = "y" };
        var system = new Category { Id = Guid.NewGuid(), UserId = null, Name = "Ăn uống", Slug = "food" };
        foreach (var c in new[] { mine, theirs, system })
        {
            _categories.Setup(r => r.GetByIdAsync(c.Id, It.IsAny<CancellationToken>())).ReturnsAsync(c);
        }

        await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(account: wallet), null)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Invoking(() => Service().PrepareAsync(User, Request(category: theirs.Id), null)).Should().ThrowAsync<NotFoundException>();
        await Service().PrepareAsync(User, Request(category: mine.Id), null);
        await Service().PrepareAsync(User, Request(category: system.Id), null);
    }
}
