using System.Net;
using System.Reflection;
using System.Text.Json;
using FinMate.API.Authorization;
using FinMate.API.Controllers;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Monitoring;
using FinMate.Application.Subscriptions;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.ExternalServices.SePay;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Subscriptions;

public class SubscriptionPureLogicTests
{
    private static readonly Dictionary<string, string> DocumentedFields = new()
    {
        // Cố ý xáo thứ tự: chuỗi ký phải theo thứ tự của SePay, không theo thứ tự truyền vào.
        ["cancel_url"] = "https://yoursite.com/payment/cancel",
        ["merchant"] = "MERCHANT_123",
        ["order_amount"] = "100000",
        ["currency"] = "VND",
        ["operation"] = "PURCHASE",
        ["order_description"] = "Thanh toán đơn hàng #12345",
        ["order_invoice_number"] = "INV_20231201_001",
        ["success_url"] = "https://yoursite.com/payment/success",
        ["error_url"] = "https://yoursite.com/payment/error",
        ["customer_id"] = string.Empty,
        ["not_signed"] = "bo qua",
    };

    [Fact]
    public void SignedString_MatchesTheExampleInSePaysDocumentation()
        => SePaySignature.BuildSignedString(DocumentedFields).Should().Be(
            "order_amount=100000,merchant=MERCHANT_123,currency=VND,operation=PURCHASE,order_description=Thanh toán đơn hàng #12345,order_invoice_number=INV_20231201_001,success_url=https://yoursite.com/payment/success,error_url=https://yoursite.com/payment/error,cancel_url=https://yoursite.com/payment/cancel");

    /// <summary>Giá trị mong đợi được tính độc lập bằng hmac của Python trên đúng chuỗi trên, gồm cả chữ có dấu dạng UTF-8.</summary>
    [Fact]
    public void Signature_IsBase64OfHmacSha256()
        => SePaySignature.Sign(DocumentedFields, "secret-key").Should().Be("+FrZu5ow+5+6oWzEtwlwVxcG9FTyQAo9UYDdVZCpDLY=");

    [Fact]
    public void SignedString_SkipsZeroLikeSePayDoes()
        => SePaySignature.BuildSignedString(new Dictionary<string, string> { ["order_amount"] = "0", ["merchant"] = "M" }).Should().Be("merchant=M");

    [Fact]
    public void NextStart_StartsNowWithoutAnActivePeriod()
    {
        var now = DateTimeOffset.UtcNow;
        SubscriptionPeriods.NextStart(now, null).Should().Be(now);
        SubscriptionPeriods.NextStart(now, now.AddDays(-1)).Should().Be(now);
    }

    [Fact]
    public void NextStart_QueuesBehindTheLastPeriod()
    {
        var now = DateTimeOffset.UtcNow;
        SubscriptionPeriods.NextStart(now, now.AddDays(5)).Should().Be(now.AddDays(5));
    }

    [Fact]
    public void EndOf_UsesCalendarLengths()
    {
        var start = new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero);
        SubscriptionPeriods.EndOf(start, SubscriptionCycle.Monthly).Should().Be(new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero));
        SubscriptionPeriods.EndOf(start, SubscriptionCycle.Yearly).Should().Be(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-10, 20, 20)] // đang chạy: chỉ phần còn lại
    [InlineData(5, 35, 30)] // chưa bắt đầu: cả kỳ
    [InlineData(-30, -1, 0)] // đã hết: không dời gì
    public void UnusedOf_IsWhatLaterPeriodsMoveUpBy(int startDays, int endDays, int expectedDays)
    {
        var now = DateTimeOffset.UtcNow;
        SubscriptionPeriods.UnusedOf(now.AddDays(startDays), now.AddDays(endDays), now).Should().Be(TimeSpan.FromDays(expectedDays));
    }

    [Theory]
    [InlineData("\"300000.00\"", 300000)]
    [InlineData("300000", 300000)]
    public void SePayAmount_IsReadFromTheStringFormItActuallySends(string raw, long expected)
    {
        var info = Parse($$$"""{"data":{"order_status":"CAPTURED","order_amount":{{{raw}}},"order_currency":"VND"}}""");
        info!.Amount.Should().Be(expected);
    }

    [Fact]
    public void SePayAmount_ThatCannotBeReadIsNull_SoTheOrderNeverMatches()
        => Parse("""{"data":{"order_amount":"abc"}}""")!.Amount.Should().BeNull();

    [Fact]
    public void SePayTransaction_PrefersTheApprovedOneOverAnEarlierFailedAttempt()
        => Parse("""{"data":{"transactions":[{"id":"failed","transaction_status":"DECLINED"},{"id":"ok","transaction_status":"APPROVED"}]}}""")!.TransactionId.Should().Be("ok");

    [Theory]
    [InlineData(HttpStatusCode.OK, 0, true)]
    [InlineData(HttpStatusCode.NotFound, 0, true)]
    [InlineData(HttpStatusCode.Unauthorized, 1, false)]
    [InlineData(HttpStatusCode.InternalServerError, 1, false)]
    public async Task SePayCalls_FeedTheProviderMonitor(HttpStatusCode status, int failures, bool answered)
    {
        var vault = new Mock<ISecretVault>();
        vault.Setup(v => v.GetAsync(BackendSecrets.SepaySecretKey, It.IsAny<CancellationToken>())).ReturnsAsync("key");
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(SePayClient.HttpClientName)).Returns(new HttpClient(new StubHandler(status)));
        var tracker = new ProviderFailureTracker();
        var client = new SePayClient(factory.Object, vault.Object, new SubscriptionOptions("M", "https://c.test", "https://a.test", "https://w.test"), tracker);

        var call = () => client.GetOrderAsync("FM1");

        if (answered)
        {
            await call.Should().NotThrowAsync();
            tracker.Get(MonitoredServices.SePay.Key).HasSucceeded.Should().BeTrue();
        }
        else
        {
            await call.Should().ThrowAsync<SePayUnavailableException>();
        }

        tracker.Get(MonitoredServices.SePay.Key).ConsecutiveFailures.Should().Be(failures);
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"data":{"order_status":"CAPTURED"}}""") });
    }

    private static SePayOrderInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return SePayClient.Parse(document.RootElement);
    }

    /// <summary>Đường nhập bằng AI nào quên gắn attribute là một đường dùng miễn phí không ai thấy.</summary>
    [Theory]
    [InlineData(typeof(TransactionsController), nameof(TransactionsController.Chat), SubscriptionFeatures.AiChat)]
    [InlineData(typeof(TransactionsController), nameof(TransactionsController.Parse), SubscriptionFeatures.AiChat)]
    [InlineData(typeof(TransactionsController), nameof(TransactionsController.Voice), SubscriptionFeatures.VoiceInput)]
    [InlineData(typeof(TransactionsController), nameof(TransactionsController.ScanReceipt), SubscriptionFeatures.ReceiptScan)]
    [InlineData(typeof(ChatAnalyzeController), nameof(ChatAnalyzeController.Analyze), SubscriptionFeatures.AiChat)]
    [InlineData(typeof(ChatAnalyzeController), nameof(ChatAnalyzeController.AnalyzeVoice), SubscriptionFeatures.VoiceInput)]
    public void EveryAiInputEndpoint_IsGated(Type controller, string action, string feature)
        => controller.GetMethod(action)!.GetCustomAttribute<RequireSubscriptionFeatureAttribute>()!.Feature.Should().Be(feature);

    [Fact]
    public void EveryCatalogFeature_IsEnforcedSomewhere()
    {
        var enforced = typeof(TransactionsController).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<RequireSubscriptionFeatureAttribute>()?.Feature)
            .OfType<string>()
            .ToHashSet();

        enforced.Should().BeEquivalentTo(SubscriptionFeatures.All.Select(f => f.Key));
    }
}
