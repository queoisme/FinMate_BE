using System.Net;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Integration.Controllers;

/// <summary>
/// Phase 82: <c>POST /transactions/{id}/not-duplicate</c>, lọc <c>duplicateStatus</c>, và các trường nghi trùng trên DTO giao dịch
/// (<c>duplicateStatus</c>, <c>duplicateOf</c>, <c>suggestedDuplicates</c> chỉ cho nháp).
/// </summary>
[Collection("Integration")]
public class TransactionDuplicateControllerTests : IClassFixture<AuthApiFactory>
{
    private readonly DuplicateApi _api;

    public TransactionDuplicateControllerTests(AuthApiFactory factory)
    {
        _api = new DuplicateApi(factory);
    }

    private async Task<(RefundApi.Session S, Guid Chat, Guid Draft)> SuspectedPairAsync()
    {
        var s = await _api.Base.StartAsync();
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);
        var chat = await _api.InsertAsync(s, 50_000, at.AddMinutes(-1), TransactionSource.Manual, TransactionStatus.Confirmed, category: s.Food, merchant: "trà sữa");
        var draft = await _api.InsertAsync(s, 50_000, at, duplicateOf: chat, duplicateStatus: DuplicateStatus.Suspected);
        return (s, chat, draft);
    }

    [Fact]
    public async Task Detail_ShowsTheLinkAndTheSuggestions()
    {
        var (s, chat, draft) = await SuspectedPairAsync();

        var dto = await _api.GetAsync(s, draft);

        dto.DuplicateStatus.Should().Be("Suspected");
        dto.DuplicateOf!.Id.Should().Be(chat);
        dto.DuplicateOf.Source.Should().Be("Manual");
        dto.DuplicateOf.CategoryName.Should().NotBeNullOrEmpty();
        dto.DuplicateOf.FinancialAccountName.Should().NotBeNullOrEmpty();
        dto.SuggestedDuplicates.Should().ContainSingle().Which.Id.Should().Be(chat);
    }

    [Fact]
    public async Task SuggestedDuplicates_AreOnlyForDrafts()
    {
        var (s, chat, _) = await SuspectedPairAsync();

        (await _api.GetAsync(s, chat)).SuggestedDuplicates.Should().BeNull("khoản đã xác nhận không có gợi ý tính khi đọc");
    }

    [Fact]
    public async Task List_FiltersSuspected()
    {
        var (s, _, draft) = await SuspectedPairAsync();
        await _api.InsertAsync(s, 99_000, DateTimeOffset.UtcNow);

        var suspected = await _api.ListAsync(s, "duplicateStatus=Suspected");

        suspected.Select(t => t.Id).Should().Equal(draft);
    }

    [Fact]
    public async Task NotDuplicate_DismissesKeepsTheLinkAndStopsSuggesting()
    {
        var (s, chat, draft) = await SuspectedPairAsync();

        var response = await _api.NotDuplicateAsync(s, draft);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await DuplicateApi.DataAsync(response);
        dto.DuplicateStatus.Should().Be("Dismissed");
        dto.DuplicateOf!.Id.Should().Be(chat, "giữ liên kết để không gợi ý lại cặp này");
        (await _api.GetAsync(s, draft)).SuggestedDuplicates.Should().BeNull();
        (await _api.ListAsync(s, "duplicateStatus=Suspected")).Should().BeEmpty();
    }

    [Fact]
    public async Task NotDuplicate_Twice_IsStill200()
    {
        var (s, _, draft) = await SuspectedPairAsync();
        await _api.NotDuplicateAsync(s, draft);

        var again = await _api.NotDuplicateAsync(s, draft);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DuplicateApi.DataAsync(again)).DuplicateStatus.Should().Be("Dismissed");
    }

    [Fact]
    public async Task NotDuplicate_ThenConfirm_Works()
    {
        var (s, _, draft) = await SuspectedPairAsync();
        await _api.NotDuplicateAsync(s, draft);

        var confirm = await _api.Base.ConfirmAsync(s, draft, new { categoryId = s.Food });

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NotDuplicate_OnATransactionWithoutLink_Is422()
    {
        var s = await _api.Base.StartAsync();
        var plain = await _api.InsertAsync(s, 50_000, DateTimeOffset.UtcNow);

        var response = await _api.NotDuplicateAsync(s, plain);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await RefundApi.ErrorAsync(response)).Should().Be("NOT_SUSPECTED_DUPLICATE");
    }

    [Fact]
    public async Task NotDuplicate_SomeoneElsesOrUnknown_Is404()
    {
        var (_, _, draft) = await SuspectedPairAsync();
        var stranger = await _api.Base.StartAsync();

        (await _api.NotDuplicateAsync(stranger, draft)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _api.NotDuplicateAsync(stranger, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NotDuplicate_WithoutToken_Is401()
    {
        var response = await _api.Base.Client.PostAsync($"/api/v1/transactions/{Guid.NewGuid()}/not-duplicate", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
