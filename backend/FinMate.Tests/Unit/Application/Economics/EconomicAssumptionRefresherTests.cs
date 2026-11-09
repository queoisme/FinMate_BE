using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Economics;
using FinMate.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Economics;

/// <summary>Job làm mới giả định lạm phát: tự duyệt số nhỏ trong khoảng hợp lý, để Draft số lớn hoặc ngoài khoảng, không tạo bản trùng, lỗi thì giữ bản cũ và báo.</summary>
public class EconomicAssumptionRefresherTests
{
    private const string IndexUrl = "https://www.nso.gov.vn/tin-tuc-khac";
    private const string ReleaseUrl = "https://www.nso.gov.vn/?p=62845";

    private static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string here = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "Fixtures", "cpi-release-2026-10.html"));

    private static string IndexPage(string href = ReleaseUrl)
        => "<html><body><a href=\"/other\">Thị trường lao động</a><a href=\"" + href + "\">Chỉ số giá tiêu dùng, chỉ số giá vàng và chỉ số giá đô la Mỹ tháng Mười năm 2026</a></body></html>";

    private sealed class Config(EconomicFeedsSettings feeds) : ISystemConfigProvider
    {
        public Task<SystemConfigSnapshot> GetAsync(CancellationToken ct = default)
            => Task.FromResult(new SystemConfigSnapshot(1, new SystemConfigDocument { EconomicFeeds = feeds }, DateTimeOffset.UtcNow));

        public void Invalidate() { }
    }

    private sealed class Feed(Dictionary<string, string?> pages) : IEconomicFeedClient
    {
        public List<string> Requested { get; } = [];

        public Task<string?> GetPageAsync(string url, CancellationToken ct = default)
        {
            Requested.Add(url);
            return Task.FromResult(pages.GetValueOrDefault(url));
        }
    }

    private sealed class Assumptions(params EconomicAssumption[] existing) : IAssumptionService
    {
        public List<EconomicAssumption> Items { get; } = [.. existing];

        public Task<EconomicAssumption> CreateDraftAsync(Guid actorId, string key, int rateBps, string unit, string sourceName, string sourceUrl, DateTimeOffset asOf, string? ipAddress, CancellationToken ct = default)
        {
            var item = new EconomicAssumption { Id = Guid.NewGuid(), Key = key, RateBps = rateBps, Unit = unit, SourceName = sourceName, SourceUrl = sourceUrl, AsOf = asOf, Version = Items.Count(i => i.Key == key) + 1, Status = AssumptionStatus.Draft, CreatedBy = actorId };
            Items.Add(item);
            return Task.FromResult(item);
        }

        public Task<EconomicAssumption> ApproveAsync(Guid id, Guid approverId, string? ipAddress, CancellationToken ct = default)
        {
            var item = Items.Single(i => i.Id == id);
            foreach (var old in Items.Where(i => i.Key == item.Key && i.Status == AssumptionStatus.Approved))
            {
                old.Status = AssumptionStatus.Retired;
            }

            item.Status = AssumptionStatus.Approved;
            item.ApprovedBy = approverId;
            return Task.FromResult(item);
        }

        public Task<EconomicAssumption> RetireAsync(Guid id, Guid actorId, string? ipAddress, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default)
            => Task.FromResult(Items.Where(i => key is null || i.Key == key).OrderByDescending(i => i.Version).ToList());
    }

    private sealed class Status : IEconomicFeedStatus
    {
        public List<(bool Healthy, string? Detail)> Reports { get; } = [];

        public Task ReportAsync(bool healthy, string? detail, CancellationToken ct = default)
        {
            Reports.Add((healthy, detail));
            return Task.CompletedTask;
        }
    }

    private static EconomicFeedsSettings Enabled(int maxDelta = 100) => new() { CpiIndexUrl = IndexUrl, AutoApproveMaxDeltaBps = maxDelta };

    private static EconomicAssumption Approved(int bps, DateTimeOffset asOf)
        => new() { Id = Guid.NewGuid(), Key = EconomicAssumptionRefresher.Key, RateBps = bps, Unit = "pct_per_year", Version = 1, Status = AssumptionStatus.Approved, AsOf = asOf };

    private static (EconomicAssumptionRefresher Refresher, Assumptions Assumptions, Status Status, Feed Feed) Build(EconomicFeedsSettings feeds, Dictionary<string, string?> pages, params EconomicAssumption[] existing)
    {
        var assumptions = new Assumptions(existing);
        var status = new Status();
        var feed = new Feed(pages);
        return (new EconomicAssumptionRefresher(new Config(feeds), feed, assumptions, status), assumptions, status, feed);
    }

    [Fact]
    public async Task FirstValueIsAutoApprovedWithSourceAndDate()
    {
        var (refresher, assumptions, status, _) = Build(Enabled(), new() { [IndexUrl] = IndexPage(), [ReleaseUrl] = Fixture() });

        var outcome = await refresher.RunAsync();

        outcome.Should().Be(RefreshOutcome.AutoApproved);
        var item = assumptions.Items.Single();
        item.Key.Should().Be("general_price_growth");
        (item.RateBps, item.Unit, item.Status).Should().Be((457, "pct_per_year", AssumptionStatus.Approved));
        item.SourceUrl.Should().Be(ReleaseUrl);
        item.SourceName.Should().Contain("Chỉ số giá tiêu dùng");
        item.AsOf.Date.Should().Be(new DateTime(2026, 11, 3));
        status.Reports.Should().ContainSingle().Which.Healthy.Should().BeTrue();
    }

    [Fact]
    public async Task SmallChangeIsAutoApprovedAndRetiresTheOldVersion()
    {
        var old = Approved(420, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(7)));
        var (refresher, assumptions, _, _) = Build(Enabled(100), new() { [IndexUrl] = IndexPage(), [ReleaseUrl] = Fixture() }, old);

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.AutoApproved);   // |457-420| = 37 <= 100

        old.Status.Should().Be(AssumptionStatus.Retired);
        assumptions.Items.Single(i => i.Status == AssumptionStatus.Approved).RateBps.Should().Be(457);
    }

    [Fact]
    public async Task LargeChangeStaysDraftAndIsReportedForReview()
    {
        var old = Approved(200, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(7)));
        var (refresher, assumptions, status, _) = Build(Enabled(100), new() { [IndexUrl] = IndexPage(), [ReleaseUrl] = Fixture() }, old);

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.NeedsReview);   // |457-200| = 257 > 100

        old.Status.Should().Be(AssumptionStatus.Approved);
        assumptions.Items.Single(i => i.Status == AssumptionStatus.Draft).RateBps.Should().Be(457);
        status.Reports.Should().ContainSingle().Which.Healthy.Should().BeFalse();
    }

    [Fact]
    public async Task ARateOutsideTheConfiguredBoundsNeverAutoApproves()
    {
        var feeds = new EconomicFeedsSettings { CpiIndexUrl = IndexUrl, AutoApproveMaxDeltaBps = 10000, MinRateBps = 0, MaxRateBps = 400 };
        var (refresher, assumptions, _, _) = Build(feeds, new() { [IndexUrl] = IndexPage(), [ReleaseUrl] = Fixture() });

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.NeedsReview);   // 457 > 400

        assumptions.Items.Single().Status.Should().Be(AssumptionStatus.Draft);
    }

    [Fact]
    public async Task TheSameReleaseTwiceCreatesNothingTheSecondTime()
    {
        var (refresher, assumptions, _, _) = Build(Enabled(), new() { [IndexUrl] = IndexPage(), [ReleaseUrl] = Fixture() });

        await refresher.RunAsync();
        (await refresher.RunAsync()).Should().Be(RefreshOutcome.AlreadyCurrent);

        assumptions.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task AnUnparseablePageKeepsTheOldVersionAndReportsAFailure()
    {
        var old = Approved(420, new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.FromHours(7)));
        var (refresher, assumptions, status, _) = Build(Enabled(), new() { [IndexUrl] = IndexPage(), [ReleaseUrl] = "<html><body>Trang đã đổi giao diện</body></html>" }, old);

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.Failed);

        assumptions.Items.Should().ContainSingle().Which.Status.Should().Be(AssumptionStatus.Approved);
        status.Reports.Should().ContainSingle().Which.Healthy.Should().BeFalse();
    }

    [Fact]
    public async Task AnUnreachableIndexIsAFailureNotAnException()
    {
        var (refresher, assumptions, status, _) = Build(Enabled(), new());

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.Failed);

        assumptions.Items.Should().BeEmpty();
        status.Reports.Should().ContainSingle().Which.Healthy.Should().BeFalse();
    }

    [Fact]
    public async Task DisabledWhenTheUrlIsEmptyAndNothingIsFetched()
    {
        var (refresher, assumptions, status, feed) = Build(new EconomicFeedsSettings(), new());

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.Disabled);

        feed.Requested.Should().BeEmpty();
        assumptions.Items.Should().BeEmpty();
        status.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task ARelativeLinkOnTheIndexIsResolvedAgainstTheIndexAddress()
    {
        var (refresher, assumptions, _, feed) = Build(Enabled(), new() { [IndexUrl] = IndexPage("/?p=62845"), ["https://www.nso.gov.vn/?p=62845"] = Fixture() });

        (await refresher.RunAsync()).Should().Be(RefreshOutcome.AutoApproved);

        feed.Requested.Should().Contain("https://www.nso.gov.vn/?p=62845");
        assumptions.Items.Single().SourceUrl.Should().Be("https://www.nso.gov.vn/?p=62845");
    }
}
