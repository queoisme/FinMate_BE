using FinMate.Application.Agent;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Giả định kinh tế (giá xe tăng bao nhiêu %/năm...): luôn có nguồn, có phiên bản, chỉ bản đã duyệt mới được agent dùng.</summary>
public class AssumptionTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.NewGuid();

    private sealed class FakeRepository : IEconomicAssumptionRepository
    {
        public List<EconomicAssumption> Rows { get; } = [];

        public Task<EconomicAssumption?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
        public Task<EconomicAssumption?> GetApprovedAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Rows.Where(r => r.Key == key && r.Status == AssumptionStatus.Approved).OrderByDescending(r => r.Version).FirstOrDefault());
        public Task<IReadOnlyList<string>> ListApprovedKeysAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Rows.Where(r => r.Status == AssumptionStatus.Approved).Select(r => r.Key).Distinct().OrderBy(k => k).ToList());
        public Task<int> GetMaxVersionAsync(string key, CancellationToken ct = default) => Task.FromResult(Rows.Where(r => r.Key == key).Select(r => r.Version).DefaultIfEmpty(0).Max());
        public Task<List<EconomicAssumption>> ListAsync(string? key, CancellationToken ct = default) => Task.FromResult(Rows.Where(r => key == null || r.Key == key).ToList());
        public Task AddAsync(EconomicAssumption assumption, CancellationToken ct = default) { Rows.Add(assumption); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly FakeRepository _repo = new();
    private readonly Mock<IAuditLogService> _audit = new();

    private AssumptionService Service() => new(_repo, _audit.Object);

    private Task<EconomicAssumption> Draft(AssumptionService service, string key = "car_price_growth", int bps = 500, string name = "Tổng cục Thống kê", string url = "https://www.gso.gov.vn/")
        => service.CreateDraftAsync(Admin, key, bps, "pct_per_year", name, url, AsOf, "127.0.0.1");

    [Theory]
    [InlineData("", "https://www.gso.gov.vn/")]
    [InlineData("  ", "https://www.gso.gov.vn/")]
    [InlineData("GSO", "")]
    [InlineData("GSO", "gso.gov.vn")]
    [InlineData("GSO", "javascript:alert(1)")]
    public async Task cannot_create_draft_without_a_real_source(string name, string url)
    {
        var act = () => Draft(Service(), name: name, url: url);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("assumption_source_required");
        _repo.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task cannot_approve_without_source()
    {
        var row = new EconomicAssumption { Id = Guid.NewGuid(), Key = "car_price_growth", RateBps = 500, Unit = "pct_per_year", SourceName = "", SourceUrl = "", AsOf = AsOf, Version = 1, Status = AssumptionStatus.Draft, CreatedBy = Admin };
        _repo.Rows.Add(row);

        var act = () => Service().ApproveAsync(row.Id, Admin, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("assumption_source_required");
        row.Status.Should().Be(AssumptionStatus.Draft);
    }

    [Theory]
    [InlineData("Car Price")]
    [InlineData("1abc")]
    [InlineData("ab")]
    public async Task invalid_key_is_rejected(string key)
        => (await ((Func<Task>)(async () => await Draft(Service(), key: key))).Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_assumption_key");

    [Theory]
    [InlineData(-10_001)]
    [InlineData(100_001)]
    public async Task out_of_range_rate_is_rejected(int bps)
        => (await ((Func<Task>)(async () => await Draft(Service(), bps: bps))).Should().ThrowAsync<BusinessRuleException>()).Which.ErrorCode.Should().Be("invalid_assumption_rate");

    [Fact]
    public async Task only_approved_version_is_returned()
    {
        var service = Service();
        var v1 = await Draft(service, bps: 500);
        await service.ApproveAsync(v1.Id, Admin, null);
        var v2 = await Draft(service, bps: 700); // còn là bản nháp: agent không được thấy

        var found = await new GetAssumptionsTool(_repo).GetApprovedAsync("car_price_growth", AsOf);

        found!.RateBps.Should().Be(500);
        v2.Version.Should().Be(2);
    }

    [Fact]
    public async Task new_version_retires_previous()
    {
        var service = Service();
        var v1 = await Draft(service, bps: 500);
        await service.ApproveAsync(v1.Id, Admin, null);
        var v2 = await Draft(service, bps: 700);
        await service.ApproveAsync(v2.Id, Admin, null);

        v1.Status.Should().Be(AssumptionStatus.Retired);
        v2.Status.Should().Be(AssumptionStatus.Approved);
        _repo.Rows.Count(r => r.Key == "car_price_growth" && r.Status == AssumptionStatus.Approved).Should().Be(1);
        (await new GetAssumptionsTool(_repo).GetApprovedAsync("car_price_growth", AsOf))!.RateBps.Should().Be(700);
    }

    [Fact]
    public async Task versions_increase_per_key_independently()
    {
        var service = Service();
        (await Draft(service, key: "car_price_growth")).Version.Should().Be(1);
        (await Draft(service, key: "car_price_growth")).Version.Should().Be(2);
        (await Draft(service, key: "house_price_growth")).Version.Should().Be(1);
    }

    [Fact]
    public async Task approval_is_audited()
    {
        var service = Service();
        var draft = await Draft(service);
        var approver = Guid.NewGuid();

        await service.ApproveAsync(draft.Id, approver, "10.0.0.1");

        _audit.Verify(a => a.LogAsync(AuditEvents.AdminAssumptionApproved, approver, "10.0.0.1", It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
        draft.ApprovedBy.Should().Be(approver);
    }

    [Fact]
    public async Task only_a_draft_can_be_approved()
    {
        var service = Service();
        var draft = await Draft(service);
        await service.ApproveAsync(draft.Id, Admin, null);

        var act = () => service.ApproveAsync(draft.Id, Admin, null);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task approving_an_unknown_id_is_not_found()
        => await ((Func<Task>)(async () => await Service().ApproveAsync(Guid.NewGuid(), Admin, null))).Should().ThrowAsync<NotFoundException>();

    [Fact]
    public async Task retiring_an_approved_version_stops_the_agent_using_it_and_is_audited()
    {
        var service = Service();
        var draft = await Draft(service);
        await service.ApproveAsync(draft.Id, Admin, null);

        await service.RetireAsync(draft.Id, Admin, null);

        (await new GetAssumptionsTool(_repo).GetApprovedAsync("car_price_growth", AsOf)).Should().BeNull();
        _audit.Verify(a => a.LogAsync(AuditEvents.AdminAssumptionRetired, Admin, null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task evidence_carries_source_date_and_a_confidence_that_ages()
    {
        var service = Service();
        var draft = await Draft(service, bps: 550);
        await service.ApproveAsync(draft.Id, Admin, null);
        var tool = new GetAssumptionsTool(_repo);

        var fresh = (await tool.GetApprovedAsync("car_price_growth", AsOf.AddDays(1)))!;
        var stale = (await tool.GetApprovedAsync("car_price_growth", AsOf.AddDays(60)))!;

        fresh.Evidence.Source.Should().Be(EvidenceSource.Document);
        fresh.Evidence.SourceName.Should().Be("Tổng cục Thống kê");
        fresh.Evidence.SourceUrl.Should().Be("https://www.gso.gov.vn/");
        fresh.Evidence.AsOf.Should().Be(AsOf);
        fresh.Evidence.ValueVnd.Should().BeNull();
        fresh.Evidence.Text.Should().Contain("5,50%");
        fresh.Evidence.Confidence.Should().Be(ConfidenceCalculator.Compute(EvidenceSource.Document, AsOf, AsOf.AddDays(1), 1.0));
        stale.Evidence.Confidence.Should().BeLessThan(fresh.Evidence.Confidence);
    }

    [Fact]
    public async Task unknown_key_returns_null_not_a_default_number()
        => (await new GetAssumptionsTool(_repo).GetApprovedAsync("nope", AsOf)).Should().BeNull();
}
