using FinMate.Backtest.Generation;
using FinMate.Backtest.Simulation;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Tests.Integration.Backtest;

/// <summary>
/// Phase 96, D7: repository giả của backtest phải trả đúng những gì <see cref="ForecastDataRepository"/> trả trên Postgres thật, nếu không số đo
/// không phản ánh production. Gieo một người tổng hợp có vay, quỹ nhóm, ví bị loại khỏi dự báo, chuyển ví và hoàn tiền, rồi so ba hàm ở 5 mốc.
/// </summary>
public class FakeForecastDataContractTests : IClassFixture<ReceiptScanDbFixture>
{
    private static readonly DateOnly[] Origins = [new(2025, 4, 2), new(2025, 7, 1), new(2025, 11, 11), new(2026, 2, 18), new(2026, 6, 30)];
    private const int Window = 120;

    private readonly ReceiptScanDbFixture _fixture;

    public FakeForecastDataContractTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TheFakeReturnsExactlyWhatTheRealRepositoryReturns()
    {
        var world = FamilyWithRefunds();
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"backtest_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        await SeedAsync(db, world);
        var real = new ForecastDataRepository(db);
        var clock = new BacktestClock();
        var fake = new FakeForecastData(new WorldView(world, clock));
        var counted = world.Accounts.Where(a => a.IncludeInForecast).Select(a => a.Id).ToHashSet();

        foreach (var origin in Origins)
        {
            clock.SetOrigin(origin);
            var (from, to) = (origin.AddDays(-Window), origin);
            var yesterday = origin.AddDays(-1);   // repository thật không cắt theo giờ; "ngày < T" = tới hết hôm qua

            var raw = Sorted(await real.GetRawTransactionsAsync(world.UserId, from, yesterday));
            raw.Should().HaveCountGreaterThan(100, "phép so phải có dữ liệu thật ở mỗi mốc");
            Sorted(await fake.GetRawTransactionsAsync(world.UserId, from, to)).Should().Equal(raw);
            Sorted(await fake.GetTransactionsAsync(world.UserId, from, to)).Should().Equal(Sorted(await real.GetTransactionsAsync(world.UserId, from, yesterday)));
            var boundary = Sorted(await real.GetBoundaryTransfersAsync(world.UserId, counted, from, yesterday));
            boundary.Should().NotBeEmpty();
            Sorted(await fake.GetBoundaryTransfersAsync(world.UserId, counted, from, to)).Should().Equal(boundary);
        }

        (await real.GetRawTransactionsAsync(world.UserId, GeneratorSpec.DataStart, GeneratorSpec.DataEnd)).Should().Contain(t => t.IsLoan);
        (await real.GetBoundaryTransfersAsync(world.UserId, counted, GeneratorSpec.DataStart, GeneratorSpec.DataEnd)).Should().Contain(t => t.MerchantName == "Quỹ gia đình");
    }

    private static SynthWorld FamilyWithRefunds()
    {
        foreach (var seed in Enumerable.Range(0, GeneratorSpec.SeedsPerPersona))
        {
            var world = WorldBuilder.Build(Persona.Family, seed, ScenarioCodes.K0);
            var refunds = world.Transactions.Where(t => t.Label == TxLabel.Refund).ToList();
            var byId = world.Transactions.ToDictionary(t => t.Id);
            if (refunds.Any(r => r.AmountCents == byId[r.RelatedTransactionId!.Value].AmountCents) && refunds.Any(r => r.AmountCents < byId[r.RelatedTransactionId!.Value].AmountCents))
            {
                return world;   // có cả hoàn đủ lẫn hoàn một phần
            }
        }

        throw new InvalidOperationException("Không có người gia đình nào có cả hoàn đủ và hoàn một phần.");
    }

    private static List<Application.Forecasting.ForecastTransaction> Sorted(IEnumerable<Application.Forecasting.ForecastTransaction> rows)
        => rows.OrderBy(r => r.Id).ThenBy(r => r.AccountId).ToList();

    private static async Task SeedAsync(FinMateDbContext db, SynthWorld world)
    {
        var now = DateTimeOffset.UtcNow;
        var slugs = world.Transactions.Where(t => t.CategorySlug is not null).Select(t => t.CategorySlug!).Distinct().ToList();
        db.Categories.AddRange(slugs.Select(s => new Category { Id = GeneratorSpec.CategoryId(s), Name = s, Slug = s, CreatedAt = now, UpdatedAt = now }));
        db.Users.Add(new User
        {
            Id = world.UserId, Email = $"{world.UserId:N}@backtest.local", DisplayName = "Backtest", Role = UserRole.User, CreatedAt = now, UpdatedAt = now,
        });
        db.FinancialAccounts.AddRange(world.Accounts.Select(a => new FinancialAccount
        {
            Id = a.Id, UserId = world.UserId, AccountType = AccountType.Bank, AccountName = a.Name, BalanceCents = a.OpeningBalanceCents,
            IncludeInForecast = a.IncludeInForecast, CreatedAt = now, UpdatedAt = now,
        }));

        var funds = SeedFund(db, world, now);
        foreach (var t in world.Transactions)
        {
            db.Transactions.Add(new Transaction
            {
                Id = t.Id, UserId = world.UserId, FinancialAccountId = t.AccountId, CounterAccountId = t.CounterAccountId,
                CategoryId = t.CategorySlug is null ? null : GeneratorSpec.CategoryId(t.CategorySlug), AmountCents = t.AmountCents, TransactionType = t.Type,
                Source = TransactionSource.Manual, Status = TransactionStatus.Confirmed, MerchantName = t.Merchant, TransactedAt = t.At,
                Relation = t.Relation, RelatedTransactionId = t.RelatedTransactionId, CreatedAt = now, UpdatedAt = now,
                GroupFundEntryId = funds.GetValueOrDefault(t.Id), GroupFundDirection = funds.ContainsKey(t.Id) ? GroupFundDirection.Out : null,
            });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Mỗi giao dịch góp quỹ cần một dòng quỹ riêng (<c>group_fund_entry_id</c> là duy nhất).</summary>
    private static Dictionary<Guid, Guid?> SeedFund(FinMateDbContext db, SynthWorld world, DateTimeOffset now)
    {
        var contributions = world.Transactions.Where(t => t.Label == TxLabel.Fund).ToList();
        var group = new Group { Id = Guid.NewGuid(), Name = "Gia đình", CreatedByUserId = world.UserId, CreatedAt = now, UpdatedAt = now };
        var fund = new GroupFund
        {
            Id = Guid.NewGuid(), GroupId = group.Id, Name = contributions[0].FundName!, CreatedByUserId = world.UserId, CreatedAt = now, UpdatedAt = now,
            BalanceCents = contributions.Sum(t => t.AmountCents),
        };
        db.Groups.Add(group);
        db.GroupMembers.Add(new GroupMember { Id = Guid.NewGuid(), GroupId = group.Id, UserId = world.UserId, Role = GroupRole.Owner, JoinedAt = now, UpdatedAt = now });
        db.GroupFunds.Add(fund);
        var links = new Dictionary<Guid, Guid?>();
        foreach (var t in contributions)
        {
            var entry = new GroupFundEntry
            {
                Id = Guid.NewGuid(), FundId = fund.Id, GroupId = group.Id, MemberUserId = world.UserId, Kind = GroupFundEntryKind.Contribution,
                AmountCents = t.AmountCents, TransactedAt = t.At, CreatedAt = now, UpdatedAt = now,
            };
            db.GroupFundEntries.Add(entry);
            links[t.Id] = entry.Id;
        }

        return links;
    }
}
