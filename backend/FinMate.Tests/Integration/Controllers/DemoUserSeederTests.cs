using System.Net;
using System.Net.Http.Json;
using FinMate.API.Controllers;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Người dùng demo của DEV: có đủ lịch sử để các công cụ của agent (nhất là "mua món này có ổn không") chạy trên dữ liệu thật thay vì nói "chưa đủ dữ liệu".</summary>
[Collection("Integration")]
public class DemoUserSeederTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "123456";
    private readonly AuthApiFactory _factory;

    public DemoUserSeederTests(AuthApiFactory factory)
    {
        _factory = factory;
    }

    private static IConfiguration Config(string? email) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DemoUserSeeder.EmailKey] = email,
            [DemoUserSeeder.PasswordKey] = Password,
        }).Build();

    private static string NewEmail() => $"demo-{Guid.NewGuid():N}@finmate.local";

    [Fact]
    public async Task DoesNothingWithoutAConfiguredEmail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var before = await db.Users.CountAsync();

        var created = await DemoUserSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), Config(null));

        created.Should().BeFalse();
        (await db.Users.CountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task SeedsOnceAndALaterRunChangesNothing()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var email = NewEmail();

        (await DemoUserSeeder.SeedAsync(db, hasher, Config(email))).Should().BeTrue();
        var userId = (await db.Users.AsNoTracking().SingleAsync(u => u.Email == email)).Id;
        var transactions = await db.Transactions.CountAsync(t => t.UserId == userId);
        var items = await db.RecurringItems.CountAsync(r => r.UserId == userId);

        (await DemoUserSeeder.SeedAsync(db, hasher, Config(email))).Should().BeFalse();

        (await db.Users.CountAsync(u => u.Email == email)).Should().Be(1);
        (await db.Transactions.CountAsync(t => t.UserId == userId)).Should().Be(transactions);
        (await db.RecurringItems.CountAsync(r => r.UserId == userId)).Should().Be(items);
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, Password));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SeedsSixMonthsOfConfirmedHistoryWithABalanceThatMatchesIt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var email = NewEmail();

        await DemoUserSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), Config(email));

        var userId = (await db.Users.AsNoTracking().SingleAsync(u => u.Email == email)).Id;
        var transactions = await db.Transactions.AsNoTracking().Where(t => t.UserId == userId).ToListAsync();
        transactions.Should().OnlyContain(t => t.Status == TransactionStatus.Confirmed);
        transactions.Select(t => (t.TransactedAt.Year, t.TransactedAt.Month)).Distinct().Count().Should().BeGreaterThanOrEqualTo(6);
        transactions.Should().Contain(t => t.TransactionType == TransactionType.Credit).And.Contain(t => t.TransactionType == TransactionType.Debit);
        var wallet = await db.FinancialAccounts.AsNoTracking().SingleAsync(a => a.UserId == userId);
        var net = transactions.Sum(t => t.TransactionType == TransactionType.Credit ? t.AmountCents : -t.AmountCents);
        (wallet.BalanceCents - net).Should().BeGreaterThan(0, "số dư gồm cả số dư ban đầu của ví, nên lớn hơn tổng thu chi đã ghi");
        var items = await db.RecurringItems.AsNoTracking().Where(r => r.UserId == userId).ToListAsync();
        items.Should().Contain(r => r.Direction == RecurringDirection.Inflow).And.Contain(r => r.Direction == RecurringDirection.Outflow);
        items.Should().OnlyContain(r => r.Status == RecurringStatus.Active);
    }

    [Fact]
    public async Task ThePurchaseCheckIsAssessableAndSeparatesASmallBuyFromAnImpossibleOne()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var email = NewEmail();
        await DemoUserSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher>(), Config(email));
        var userId = (await db.Users.AsNoTracking().SingleAsync(u => u.Email == email)).Id;
        var check = scope.ServiceProvider.GetRequiredService<IPurchaseCheckService>();

        var small = await check.CheckAsync(new PurchaseCheckCommand(userId, 1_000_000, null, "tai nghe", null, null, null));
        var huge = await check.CheckAsync(new PurchaseCheckCommand(userId, 900_000_000, null, "xe hơi", null, null, null));

        small.Status.Should().Be(PurchaseStatuses.Assessable);
        small.Verdict.Should().NotBe(PurchaseVerdicts.ShortfallRisk);
        huge.Status.Should().Be(PurchaseStatuses.Assessable);
        huge.Verdict.Should().Be(PurchaseVerdicts.ShortfallRisk);
    }
}
