using System.Net;
using FinMate.Application.Auth.Otp;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>Phase 91: yêu cầu gói dữ liệu đầy đủ qua HTTP thật (BR-EXP-06, 10, 11; D14): OTP, hạn mức, 404 với id người khác, audit.</summary>
[Collection("Integration")]
public class DataExportPersonalDataTests : IClassFixture<DataExportApiFactory>
{
    private readonly DataExportApiFactory _factory;
    private readonly HttpClient _client;

    public DataExportPersonalDataTests(DataExportApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private Task<LoanTestApi> UserAsync() => new LoanTestApi(_factory, _client).SignUpAsync();

    [Fact]
    public async Task WithAValidCode_TheRequestIsPending_ListedAndAudited_WithoutAnyContent()
    {
        var api = await UserAsync();

        var id = await api.RequestExportAsync(_factory);

        var one = await api.ExportAsync(id);
        one.GetProperty("status").GetString().Should().Be("Pending");
        one.GetProperty("downloadCount").GetInt32().Should().Be(0);
        one.TryGetProperty("downloadTokenHash", out _).Should().BeFalse();
        var list = await api.ExpectAsync(HttpMethod.Get, DataExportTestApi.Base, null, HttpStatusCode.OK);
        list.EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).Should().Equal(id);

        var audit = await api.InDbAsync(db => db.AuditLogs.Where(a => a.UserId == api.UserId && a.EventType == AuditEvents.DataExportRequested).ToListAsync());
        audit.Should().ContainSingle().Which.Metadata.Should().Contain(id.ToString()).And.NotContain("amount");
    }

    [Fact]
    public async Task WithoutACode_OrAWrongOne_OrACodeForAnotherPurpose_Is403()
    {
        var api = await UserAsync();
        await api.OtpAsync(_factory);

        (await api.ErrorCodeAsync(HttpMethod.Post, DataExportTestApi.Base, new { }, HttpStatusCode.Forbidden)).Should().Be(DataExportErrorCodes.OtpInvalid);
        (await api.ErrorCodeAsync(HttpMethod.Post, DataExportTestApi.Base, new { code = "000000" }, HttpStatusCode.Forbidden)).Should().Be(DataExportErrorCodes.OtpInvalid);

        // Mã xác minh email (mục đích khác) không mở được gói dữ liệu.
        string verifyCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var otp = scope.ServiceProvider.GetRequiredService<IOtpService>();
            await otp.ClearCooldownAsync(api.Email, OtpPurpose.VerifyEmail);
            verifyCode = (await otp.IssueAsync(api.Email, OtpPurpose.VerifyEmail)).Code!;
        }

        (await api.ErrorCodeAsync(HttpMethod.Post, DataExportTestApi.Base, new { code = verifyCode }, HttpStatusCode.Forbidden)).Should().Be(DataExportErrorCodes.OtpInvalid);
        (await api.InDbAsync(db => db.PersonalDataExports.CountAsync(e => e.UserId == api.UserId))).Should().Be(0);
    }

    [Fact]
    public async Task WhileARequestIsRunning_BothOtpAndRequestAre429_AndNoEmailIsSent()
    {
        var api = await UserAsync();
        var id = await api.RequestExportAsync(_factory);
        await api.SetStatusAsync(id, PersonalDataExportStatus.Running);
        var mailBefore = _factory.Email.BodyFor(api.Email);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/otp", null, HttpStatusCode.TooManyRequests)).Should().Be(DataExportErrorCodes.RateLimited);
        (await api.ErrorCodeAsync(HttpMethod.Post, DataExportTestApi.Base, new { code = "123456" }, HttpStatusCode.TooManyRequests)).Should().Be(DataExportErrorCodes.RateLimited);
        _factory.Email.BodyFor(api.Email).Should().Be(mailBefore);
    }

    [Fact]
    public async Task AFinishedRequestStillCountsFor24Hours_ButASystemFailureDoesNot()
    {
        var api = await UserAsync();
        var failed = await api.RequestExportAsync(_factory);
        await api.SetStatusAsync(failed, PersonalDataExportStatus.Failed, DataExportFailureCodes.InternalError, counts: false);

        var ready = await api.RequestExportAsync(_factory);
        await api.SetStatusAsync(ready, PersonalDataExportStatus.Ready);

        (await api.ErrorCodeAsync(HttpMethod.Post, $"{DataExportTestApi.Base}/otp", null, HttpStatusCode.TooManyRequests)).Should().Be(DataExportErrorCodes.RateLimited);
        await api.InDbAsync(async db =>
        {
            var row = await db.PersonalDataExports.SingleAsync(e => e.Id == ready);
            row.RequestedAt = DateTimeOffset.UtcNow.AddHours(-25);
            return await db.SaveChangesAsync();
        });
        await api.RequestExportAsync(_factory);
    }

    [Fact]
    public async Task AnotherUsersRequest_Is404()
    {
        var owner = await UserAsync();
        var id = await owner.RequestExportAsync(_factory);
        var other = await UserAsync();

        (await other.SendAsync(HttpMethod.Get, $"{DataExportTestApi.Base}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.ExpectAsync(HttpMethod.Get, DataExportTestApi.Base, null, HttpStatusCode.OK)).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task TwoInsertsAtOnce_OnlyOneBecomesActive()
    {
        var api = await UserAsync();
        PersonalDataExport New() => new() { Id = Guid.NewGuid(), UserId = api.UserId, FormatVersion = 1, RequestedAt = DateTimeOffset.UtcNow };

        async Task<bool> InsertAsync()
        {
            using var scope = _factory.Services.CreateScope();
            return await new PersonalDataExportRepository(scope.ServiceProvider.GetRequiredService<FinMateDbContext>()).TryAddAsync(New());
        }

        var results = await Task.WhenAll(InsertAsync(), InsertAsync());

        results.Should().BeEquivalentTo([true, false]);
    }
}
