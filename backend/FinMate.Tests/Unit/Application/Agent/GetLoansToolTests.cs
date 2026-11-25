using System.Text.Json;
using FinMate.Application.Agent;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Agent;

/// <summary>Công cụ Agent <c>get_loans</c> (Phase 90, BR-DEBT-18, D14; chép từ bản sổ nợ Phase 86 cũ).</summary>
public class GetLoansToolTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.FromHours(7));
    private static readonly JsonElement NoArgs = JsonDocument.Parse("{}").RootElement;

    private sealed class Source(IReadOnlyList<LoanVnd> debts) : IFinancialDataSource
    {
        public Task<MonthlySeries> GetMonthlySeriesAsync(Guid userId, int months, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CategorySpendVnd>> GetCategorySpendAsync(Guid userId, DateOnly from, DateOnly to, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<long> GetTotalBalanceAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IncomeSegment?> GetIncomeSegmentOverrideAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LoanVnd>> GetOpenLoansAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(debts);
    }

    [Fact]
    public async Task TotalsOverdueAndRows_SoonestFirst()
    {
        var tool = new GetLoansTool(new Source(
        [
            new LoanVnd(LoanDirection.Borrow, 3_000_000, new DateOnly(2026, 11, 25)),
            new LoanVnd(LoanDirection.Lend, 1_000_000, new DateOnly(2026, 11, 10)),
            new LoanVnd(LoanDirection.Lend, 500_000, null),
        ]));

        var result = await tool.RunAsync(new ToolContext(Guid.NewGuid(), Now), NoArgs);

        result.Status.Should().Be(ToolStatus.Ok);
        var byId = result.Evidence.ToDictionary(e => e.Id);
        byId["loans_i_owe"].ValueVnd.Should().Be(3_000_000);
        byId["loans_owed_to_me"].ValueVnd.Should().Be(1_500_000);
        byId["loans_overdue_count"].ValueVnd.Should().Be(1);
        byId["loan_1"].Text.Should().Contain("quá hạn 5 ngày");
        byId["loan_2"].Text.Should().Contain("hạn 25/11/2026");
        byId["loan_3"].Text.Should().Contain("chưa đặt hạn");
        result.Evidence.Should().OnlyContain(e => e.Source == EvidenceSource.UserEntered);
    }

    [Fact]
    public async Task NoOpenLoan_IsInsufficientData()
    {
        var result = await new GetLoansTool(new Source([])).RunAsync(new ToolContext(Guid.NewGuid(), Now), NoArgs);

        result.Status.Should().Be(ToolStatus.InsufficientData);
        result.Code.Should().Be("no_open_loans");
    }

    [Fact]
    public async Task TheRealSource_DropsTheNameAndTitle_BeforeTheToolSeesIt()
    {
        var userId = Guid.NewGuid();
        var debts = new Mock<ILoanRepository>();
        debts.Setup(d => d.ListOpenAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new Loan
            {
                Id = Guid.NewGuid(), UserId = userId, Title = "Cho Nguyễn Văn Bí Mật vay", Counterparty = "Nguyễn Văn Bí Mật", Direction = LoanDirection.Lend,
                Status = LoanStatus.Open, PrincipalCents = 900_000, RepaidCents = 200_000,
            },
        ]);
        var source = new RepositoryFinancialDataSource(Mock.Of<IReportRepository>(), Mock.Of<IFinancialAccountRepository>(), Mock.Of<IUserRepository>(), debts.Object);

        var result = await new GetLoansTool(source).RunAsync(new ToolContext(userId, Now), NoArgs);

        JsonSerializer.Serialize(result).Should().NotContain("Bí Mật").And.NotContain("Nguyễn").And.NotContain("Cho ");
        result.Evidence.Single(e => e.Id == "loans_owed_to_me").ValueVnd.Should().Be(700_000);
    }

    [Fact]
    public void TheToolIsOnTheAllowlist_AndTakesNoArguments()
    {
        var tool = new GetLoansTool(new Source([]));

        new ToolRegistry([tool]).Allowlist.Should().Contain("get_loans");
        tool.Spec.Args.Should().BeEmpty();
    }
}
