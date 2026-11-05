using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinMate.API.Controllers;
using FinMate.API.Controllers.Ops;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Support;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Không ai trong đội vận hành xem được dữ liệu khách nếu thiếu dù chỉ một điều kiện: đúng người phụ trách, khách đồng ý, đúng phần dữ liệu, còn hạn.</summary>
[Collection("Integration")]
public class SupportConsentedAccessTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private const string Tickets = "/api/v1/support/tickets";
    private const string Ops = "/api/v1/ops/tickets";
    private const long Balance = 1_000_000;
    private const long Spent = 123_456;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Fixture dùng chung cho cả lớp, nên chỉ bật cờ một lần.
    private static bool _supportEnabled;

    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public SupportConsentedAccessTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private record Account(Guid Id, string Token);
    private record Case(Account Customer, Account Staff, Guid TicketId, Guid WalletId);

    public async Task InitializeAsync()
    {
        if (_supportEnabled)
        {
            return;
        }

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == AuthApiFactory.AdminEmail);
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(admin);
        const string config = "/api/v1/admin/system-config";
        var version = (await DataAsync(await SendAsync(HttpMethod.Get, config, token))).GetProperty("version").GetInt32();
        await DataAsync(await SendAsync(HttpMethod.Put, config, token, new { expectedVersion = version, content = new { features = new { support = true } }, note = "kiem thu" }));
        _supportEnabled = true;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Account> AccountAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Id = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@finmate.local", DisplayName = "Người", Role = role, CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return new Account(user.Id, scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessToken(user));
    }

    /// <summary>Khách có ví và một giao dịch đã xác nhận, một ca đã có người nhận.</summary>
    private async Task<Case> CaseAsync(UserRole handlerRole = UserRole.Staff)
    {
        var customer = await AccountAsync(UserRole.User);
        var staff = await AccountAsync(handlerRole);
        var walletId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.FinancialAccounts.Add(new FinancialAccount { Id = walletId, UserId = customer.Id, AccountType = AccountType.Cash, AccountName = "Ví riêng", BalanceCents = Balance, CreatedAt = now, UpdatedAt = now });
            db.Transactions.Add(new Transaction
            {
                Id = Guid.NewGuid(),
                UserId = customer.Id,
                FinancialAccountId = walletId,
                AmountCents = Spent,
                TransactionType = TransactionType.Debit,
                Source = TransactionSource.Manual,
                Status = TransactionStatus.Confirmed,
                MerchantName = "Quán cà phê",
                BalanceAfterCents = 777_777,
                ClientRequestId = Guid.NewGuid(),
                TransactedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var ticket = await DataAsync(await SendAsync(HttpMethod.Post, Tickets, customer.Token, new CreateSupportTicketRequest(SupportTicketCategory.Transaction, "Sai số dư", "Nhờ kiểm tra")));
        var ticketId = ticket.GetProperty("ticket").GetProperty("id").GetGuid();
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{ticketId}/claim", staff.Token));
        return new Case(customer, staff, ticketId, walletId);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? payload = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: JsonOptions);
        }

        return _client.SendAsync(request);
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").Clone();
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetProperty("code").GetString()!;

    private async Task<Guid> RequestAsync(Case c, params SupportAccessScope[] scopes)
        => (await DataAsync(await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/access-requests", c.Staff.Token, new OpsAccessRequest(scopes, "Cần kiểm tra số dư")))).GetProperty("id").GetGuid();

    private async Task<Guid> GrantAsync(Case c, params SupportAccessScope[] scopes)
    {
        var requestId = await RequestAsync(c, scopes);
        var walletId = scopes.Contains(SupportAccessScope.Draft) ? c.WalletId : (Guid?)null;
        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/approve", c.Customer.Token, new ApproveSupportAccessRequest(walletId)));
        return requestId;
    }

    private Task<HttpResponseMessage> ReadAsync(Case c, string scope) => ReadAsync(c.Staff, c.TicketId, scope);

    private Task<HttpResponseMessage> ReadAsync(Account actor, Guid ticketId, string scope)
        => SendAsync(HttpMethod.Get, $"{Ops}/{ticketId}/customer-data/{scope}", actor.Token);

    private static object Draft(Guid? clientRequestId = null, Guid? categoryId = null, long amount = 50_000)
        => new OpsDraftTransactionRequest(amount, TransactionType.Debit, DateTimeOffset.UtcNow, categoryId, "Siêu thị", null, clientRequestId);

    [Theory]
    [InlineData(UserRole.Staff)]
    [InlineData(UserRole.OpsManager)]
    [InlineData(UserRole.Admin)]
    public async Task WithoutCustomerConsentNobodyReadsAnything_WhateverTheirRole(UserRole role)
    {
        var c = await CaseAsync(role);

        foreach (var scope in new[] { "accounts", "transactions", "budgets" })
        {
            (await ReadAsync(c, scope)).StatusCode.Should().Be(HttpStatusCode.Forbidden, scope);
        }

        (await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/draft-transactions", c.Staff.Token, Draft())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task APendingRequestGrantsNothing()
    {
        var c = await CaseAsync();
        await RequestAsync(c, SupportAccessScope.Accounts);

        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApprovedAccessCoversExactlyTheApprovedScopes()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Transactions);

        (await ReadAsync(c, "transactions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(c, "budgets")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/draft-transactions", c.Staff.Token, Draft())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TransactionViewShowsWhatIsNeededAndNoPrivateFields()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Transactions, SupportAccessScope.Accounts, SupportAccessScope.Budgets);

        var text = await (await ReadAsync(c, "transactions")).Content.ReadAsStringAsync();

        text.Should().Contain("Quán cà phê").And.Contain(Spent.ToString());
        text.Should().NotContain("balanceAfter").And.NotContain("777777");
        text.Should().NotContain("clientRequestId").And.NotContain("notificationLogId");

        (await DataAsync(await ReadAsync(c, "accounts")))[0].GetProperty("balanceCents").GetInt64().Should().Be(Balance);
        (await ReadAsync(c, "budgets")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryViewIsLoggedForTheCustomerAndInTheAuditTrailWithoutAmounts()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Transactions);

        await DataAsync(await ReadAsync(c, "transactions"));
        await DataAsync(await ReadAsync(c, "transactions"));

        var log = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/support/access-log", c.Customer.Token));
        log.GetArrayLength().Should().Be(2);
        log[0].GetProperty("ticketId").GetGuid().Should().Be(c.TicketId);
        log[0].GetProperty("scope").GetString().Should().Be("Transactions");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var audit = await db.AuditLogs.AsNoTracking().Where(a => a.UserId == c.Staff.Id && a.EventType == "Ops.CustomerData.Viewed").ToListAsync();
        audit.Should().HaveCount(2);
        audit.Should().OnlyContain(a => !a.Metadata!.Contains(Spent.ToString()));

        // Khách khác không thấy lịch sử này.
        var stranger = await AccountAsync(UserRole.User);
        (await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/support/access-log", stranger.Token))).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task OnlyTheAssignedPersonMayUseTheGrant()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Accounts);
        var colleague = await AccountAsync(UserRole.Staff);
        var manager = await AccountAsync(UserRole.OpsManager);
        var admin = await AccountAsync(UserRole.Admin);

        (await ReadAsync(colleague, c.TicketId, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(manager, c.TicketId, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(admin, c.TicketId, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReassigningTheTicketEndsTheGrant_EvenIfItComesBack()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Accounts);
        var manager = await AccountAsync(UserRole.OpsManager);
        var replacement = await AccountAsync(UserRole.Staff);

        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{c.TicketId}/assignee", manager.Token, new OpsTicketAssignRequest(replacement.Id)));
        (await ReadAsync(replacement, c.TicketId, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await DataAsync(await SendAsync(HttpMethod.Patch, $"{Ops}/{c.TicketId}/assignee", manager.Token, new OpsTicketAssignRequest(c.Staff.Id)));
        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CustomerCanRevokeAtAnyTime()
    {
        var c = await CaseAsync();
        var requestId = await GrantAsync(c, SupportAccessScope.Accounts);
        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.OK);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/revoke", c.Customer.Token));

        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeniedRequestGrantsNothing_AndCannotBeApprovedLater()
    {
        var c = await CaseAsync();
        var requestId = await RequestAsync(c, SupportAccessScope.Accounts);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/deny", c.Customer.Token));

        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var late = await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/approve", c.Customer.Token, new ApproveSupportAccessRequest(null));
        (await ErrorCodeAsync(late)).Should().Be(SupportErrorCodes.AccessRequestNotPending);
    }

    [Fact]
    public async Task GrantExpiresAfter24Hours()
    {
        var c = await CaseAsync();
        var requestId = await GrantAsync(c, SupportAccessScope.Accounts);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var grant = await db.SupportAccessGrants.AsNoTracking().SingleAsync(g => g.Id == requestId);
            grant.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(24), TimeSpan.FromMinutes(1));
            await db.SupportAccessGrants.Where(g => g.Id == requestId).ExecuteUpdateAsync(s => s.SetProperty(g => g.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ARequestUnansweredPastItsDeadlineCannotBeApproved_AndANewOneCanBeSent()
    {
        var c = await CaseAsync();
        var requestId = await RequestAsync(c, SupportAccessScope.Accounts);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            await db.SupportAccessGrants.Where(g => g.Id == requestId).ExecuteUpdateAsync(s => s.SetProperty(g => g.RequestExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        var late = await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/approve", c.Customer.Token, new ApproveSupportAccessRequest(null));
        (await ErrorCodeAsync(late)).Should().Be(SupportErrorCodes.AccessRequestNotPending);

        await RequestAsync(c, SupportAccessScope.Accounts);
    }

    [Fact]
    public async Task ClosingTheTicketEndsTheGrant()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Accounts);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/close", c.Customer.Token));

        (await ReadAsync(c, "accounts")).IsSuccessStatusCode.Should().BeFalse();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.SupportAccessGrants.AsNoTracking().SingleAsync(g => g.TicketId == c.TicketId)).Status.Should().Be(SupportAccessStatus.Revoked);
    }

    [Fact]
    public async Task OnlyOnePendingRequestAtATime_AndOnlyTheHandlerMayAsk()
    {
        var c = await CaseAsync();
        var colleague = await AccountAsync(UserRole.Staff);

        (await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/access-requests", colleague.Token, new OpsAccessRequest([SupportAccessScope.Accounts], "tò mò"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await RequestAsync(c, SupportAccessScope.Accounts);
        var second = await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/access-requests", c.Staff.Token, new OpsAccessRequest([SupportAccessScope.Budgets], "thêm"));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorCodeAsync(second)).Should().Be(SupportErrorCodes.AccessRequestPending);
    }

    [Fact]
    public async Task ApprovingWithoutADraftScopeNeedsNoBody()
    {
        var c = await CaseAsync();
        var requestId = await RequestAsync(c, SupportAccessScope.Accounts);

        await DataAsync(await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/approve", c.Customer.Token));

        (await ReadAsync(c, "accounts")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnotherCustomerCannotApproveARequest()
    {
        var c = await CaseAsync();
        var stranger = await AccountAsync(UserRole.User);
        var requestId = await RequestAsync(c, SupportAccessScope.Accounts);

        (await SendAsync(HttpMethod.Post, $"{Tickets}/{c.TicketId}/access-requests/{requestId}/approve", stranger.Token, new ApproveSupportAccessRequest(null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApprovingDraftAccessRequiresTheCustomersOwnWallet()
    {
        var c = await CaseAsync();
        var other = await CaseAsync();
        var requestId = await RequestAsync(c, SupportAccessScope.Draft);
        var url = $"{Tickets}/{c.TicketId}/access-requests/{requestId}/approve";

        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, url, c.Customer.Token, new ApproveSupportAccessRequest(null)))).Should().Be(SupportErrorCodes.DraftAccountRequired);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, url, c.Customer.Token, new ApproveSupportAccessRequest(other.WalletId)))).Should().Be(SupportErrorCodes.DraftAccountRequired);
        await DataAsync(await SendAsync(HttpMethod.Post, url, c.Customer.Token, new ApproveSupportAccessRequest(c.WalletId)));
    }

    [Fact]
    public async Task DraftChangesNoMoneyUntilTheCustomerConfirms()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Draft);

        async Task<long> BalanceAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<FinMateDbContext>().FinancialAccounts.AsNoTracking().SingleAsync(a => a.Id == c.WalletId)).BalanceCents;
        }

        var created = await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/draft-transactions", c.Staff.Token, Draft(amount: 50_000));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var transactionId = (await DataAsync(created)).GetProperty("transactionId").GetGuid();
        (await BalanceAsync()).Should().Be(Balance);

        // Nhân viên không có đường nào xác nhận thay khách.
        (await SendAsync(HttpMethod.Post, $"/api/v1/transactions/{transactionId}/confirm", c.Staff.Token, new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await BalanceAsync()).Should().Be(Balance);

        var draft = await DataAsync(await SendAsync(HttpMethod.Get, "/api/v1/transactions?status=Draft", c.Customer.Token));
        draft.ToString().Should().Contain(transactionId.ToString());

        await DataAsync(await SendAsync(HttpMethod.Post, $"/api/v1/transactions/{transactionId}/confirm", c.Customer.Token, new { }));
        (await BalanceAsync()).Should().Be(Balance - 50_000);

        using var check = _factory.Services.CreateScope();
        var row = await check.ServiceProvider.GetRequiredService<FinMateDbContext>().Transactions.AsNoTracking().SingleAsync(t => t.Id == transactionId);
        row.Source.Should().Be(TransactionSource.Support);
        row.SupportTicketId.Should().Be(c.TicketId);
        row.UserId.Should().Be(c.Customer.Id);
        row.Status.Should().Be(TransactionStatus.Confirmed);
    }

    [Fact]
    public async Task ResendingADraftDoesNotCreateASecondOne()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Draft);
        var requestId = Guid.NewGuid();

        var first = await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/draft-transactions", c.Staff.Token, Draft(requestId));
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await SendAsync(HttpMethod.Post, $"{Ops}/{c.TicketId}/draft-transactions", c.Staff.Token, Draft(requestId));
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        (await DataAsync(second)).GetProperty("transactionId").GetGuid().Should().Be((await DataAsync(first)).GetProperty("transactionId").GetGuid());
    }

    [Fact]
    public async Task DraftRefusesACustomersPrivateCategoryAndInvalidAmounts()
    {
        var c = await CaseAsync();
        await GrantAsync(c, SupportAccessScope.Draft);
        var privateCategory = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            db.Categories.Add(new Category { Id = privateCategory, UserId = c.Customer.Id, Name = "Riêng", Slug = "rieng-" + Guid.NewGuid().ToString("N") });
            await db.SaveChangesAsync();
        }

        var url = $"{Ops}/{c.TicketId}/draft-transactions";
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, url, c.Staff.Token, Draft(categoryId: privateCategory)))).Should().Be(SupportErrorCodes.InvalidInput);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, url, c.Staff.Token, Draft(amount: 0)))).Should().Be(SupportErrorCodes.InvalidInput);
        (await ErrorCodeAsync(await SendAsync(HttpMethod.Post, url, c.Staff.Token, new OpsDraftTransactionRequest(1000, TransactionType.Transfer, DateTimeOffset.UtcNow, null, null, null, null)))).Should().Be(SupportErrorCodes.InvalidInput);
    }

    [Fact]
    public async Task AClientCannotClaimATransactionCameFromSupport()
    {
        var c = await CaseAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/transactions", c.Customer.Token, new
        {
            financialAccountId = c.WalletId,
            amountCents = 1000,
            transactionType = "Debit",
            transactedAt = DateTimeOffset.UtcNow,
            source = "Support",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task HardDeletingTheCustomerRemovesGrantsAndLogs()
    {
        var c = await CaseAsync();
        var requestId = await GrantAsync(c, SupportAccessScope.Accounts);
        await DataAsync(await ReadAsync(c, "accounts"));

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IUserHardDeleter>().HardDeleteAsync(c.Customer.Id);

        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        (await db.SupportAccessGrants.AsNoTracking().AnyAsync(g => g.Id == requestId)).Should().BeFalse();
        (await db.SupportDataAccessLogs.AsNoTracking().AnyAsync(l => l.CustomerUserId == c.Customer.Id)).Should().BeFalse();
    }
}
