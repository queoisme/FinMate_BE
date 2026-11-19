using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Tests.Integration.Controllers;

[Collection("Integration")]
public class DashboardDevelopmentSeederTests
{
    private const string Password = "DashboardFixtureTests2026!";
    private static IConfiguration Config(bool enabled = true, string? password = Password) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            [DashboardDevelopmentSeeder.EnabledKey] = enabled.ToString(), [DashboardDevelopmentSeeder.PasswordKey] = password }).Build();

    private static async Task WithDatabase(Func<FinMateDbContext, IPasswordHasher, Task> check)
    {
        using var factory = new AuthApiFactory();
        await factory.InitializeAsync();
        try
        {
            using var scope = factory.Services.CreateScope();
            await check(scope.ServiceProvider.GetRequiredService<FinMateDbContext>(), scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
        }
        finally { await factory.DisposeAsync(); }
    }

    [Fact]
    public Task RequiresBothDevelopmentAndAnExplicitOptIn() => WithDatabase(async (db, hasher) =>
    {
        var before = await db.Users.CountAsync();
        (await DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(), false)).Should().BeFalse();
        (await DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(false), true)).Should().BeFalse();
        (await db.Users.CountAsync()).Should().Be(before);
        await AssertNoMarker(db);
    });

    [Fact]
    public Task RefusesMissingCredentialsAndExistingReservedAccountsWithoutChangingThem() => WithDatabase(async (db, hasher) =>
    {
        var before = await db.Users.CountAsync();
        var missing = () => DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(password: null), true);
        await missing.Should().ThrowAsync<InvalidOperationException>();
        var existing = new User { Id = Guid.NewGuid(), Email = DashboardDevelopmentSeeder.Email("an"), DisplayName = "Existing account",
            Role = UserRole.User, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(existing);
        await db.SaveChangesAsync();
        var collision = () => DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(), true);
        await collision.Should().ThrowAsync<InvalidOperationException>();
        (await db.Users.CountAsync()).Should().Be(before + 1);
        (await db.Users.SingleAsync(user => user.Id == existing.Id)).DisplayName.Should().Be("Existing account");
        await AssertNoMarker(db);
    });

    [Fact]
    public Task CreatesLinkedConsistentScenariosAndPreservesEditsOnRerun() => WithDatabase(async (db, hasher) =>
    {
        var before = await db.Users.CountAsync();
        var configurationVersions = await db.SystemConfigVersions.CountAsync();
        (await DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(), true)).Should().BeTrue();
        (await db.Users.IgnoreQueryFilters().CountAsync()).Should().Be(before + 9);
        var an = await db.Users.SingleAsync(user => user.Email == DashboardDevelopmentSeeder.Email("an"));
        var staff = await db.Users.SingleAsync(user => user.Email == DashboardDevelopmentSeeder.Email("staff"));
        staff.Role.Should().Be(UserRole.Staff);
        hasher.Verify(Password, an.PasswordHash!).Should().BeTrue();
        await AssertLedger(db, an.Id, 12_000_000L);
        var binh = await db.Users.SingleAsync(user => user.Email == DashboardDevelopmentSeeder.Email("binh"));
        var chi = await db.Users.SingleAsync(user => user.Email == DashboardDevelopmentSeeder.Email("chi"));
        await AssertLedger(db, binh.Id, 12_000_000L);
        await AssertLedger(db, chi.Id, 3_000_000L);
        await AssertAggregateIntegrity(db);
        (await db.SupportTickets.CountAsync()).Should().Be(8);
        (await db.SupportTickets.CountAsync(ticket => ticket.Status == SupportTicketStatus.New)).Should().Be(2);
        (await db.EconomicAssumptions.ToListAsync()).Should().OnlyContain(row => row.Status == AssumptionStatus.Draft && row.SourceUrl.StartsWith("https://www.nso.gov.vn/"));
        (await db.SubscriptionOrders.ToListAsync()).Should().NotContain(row => row.Status == SubscriptionOrderStatus.Paid || row.SepayTransactionId != null);
        (await db.SystemConfigVersions.CountAsync()).Should().Be(configurationVersions);
        (await db.SupportTickets.ToListAsync()).Should().OnlyContain(row => row.CreatedAt <= row.UpdatedAt && row.UpdatedAt <= DateTimeOffset.UtcNow);
        an.DisplayName = "Edited by the developer";
        await db.SaveChangesAsync();
        var count = await db.Transactions.CountAsync();
        (await DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(), true)).Should().BeFalse();
        (await db.Transactions.CountAsync()).Should().Be(count);
        (await db.Users.SingleAsync(user => user.Id == an.Id)).DisplayName.Should().Be("Edited by the developer");
    });

    [Fact]
    public Task RollsBackTheEntireDatasetIfAReferenceConflicts() => WithDatabase(async (db, hasher) =>
    {
        var before = await db.Users.CountAsync();
        db.SubscriptionPlans.Add(new SubscriptionPlan { Id = Guid.NewGuid(), Code = "dev_dashboard_personal", Name = "Existing plan",
            IsActive = false, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var seed = () => DashboardDevelopmentSeeder.SeedAsync(db, hasher, Config(), true);
        await seed.Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();
        (await db.Users.CountAsync()).Should().Be(before);
        (await db.SupportTickets.CountAsync()).Should().Be(0);
        await AssertNoMarker(db);
    });

    private static async Task AssertNoMarker(FinMateDbContext db) =>
        (await db.AuditLogs.AnyAsync(row => row.Id == DashboardDevelopmentSeeder.Id("complete"))).Should().BeFalse();

    private static async Task AssertLedger(FinMateDbContext db, Guid userId, long openingBankCents)
    {
        var transactions = await db.Transactions.Where(row => row.UserId == userId && row.Status == TransactionStatus.Confirmed).ToListAsync();
        transactions.Count.Should().BeGreaterThan(150);
        transactions.Should().OnlyContain(row => row.Source == TransactionSource.Manual && row.TransactedAt < DateTimeOffset.UtcNow);
        foreach (var account in await db.FinancialAccounts.Where(row => row.UserId == userId).ToListAsync())
        {
            var opening = account.AccountType == AccountType.Cash ? 1_000_000L : openingBankCents;
            var credit = transactions.Where(row => row.FinancialAccountId == account.Id && row.TransactionType == TransactionType.Credit).Sum(row => row.AmountCents);
            var debit = transactions.Where(row => row.FinancialAccountId == account.Id && row.TransactionType != TransactionType.Credit).Sum(row => row.AmountCents);
            var incoming = transactions.Where(row => row.CounterAccountId == account.Id).Sum(row => row.AmountCents);
            account.BalanceCents.Should().Be(opening + credit - debit + incoming);
        }
    }

    private static async Task AssertAggregateIntegrity(FinMateDbContext db)
    {
        foreach (var period in await db.BudgetPeriods.Include(row => row.Budget).ToListAsync())
        {
            var spending = await db.Transactions.Where(row => row.UserId == period.Budget!.UserId && row.CategoryId == period.Budget.CategoryId
                && row.Status == TransactionStatus.Confirmed && row.TransactionType == TransactionType.Debit
                && row.TransactedAt >= period.PeriodStart && row.TransactedAt < period.PeriodEnd).SumAsync(row => row.AmountCents);
            period.SpentCents.Should().Be(spending);
        }
        foreach (var goal in await db.SavingGoals.ToListAsync())
            goal.SavedCents.Should().Be(await db.GoalContributions.Where(row => row.SavingGoalId == goal.Id).SumAsync(row => row.AmountCents));
    }
}
