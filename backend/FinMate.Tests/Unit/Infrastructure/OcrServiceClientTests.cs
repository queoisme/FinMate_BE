using System.Net;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// JSON mẫu dưới đây KHÔNG tự viết tay: là phản hồi thật của OCR service (<c>POST /analyze-bill</c>) và của AI Service
/// (<c>POST /api/v1/bill/classify</c>) với ảnh hóa đơn Highlands Coffee, chỉ bỏ bớt các trường backend không dùng.
/// Tên trường lệch một chữ thì System.Text.Json im lặng để null/0 và việc quét hỏng mà không có lỗi nào.
/// </summary>
public class OcrServiceClientTests
{
    private const string RealOcrJson =
        """{"total_amount": 16000, "merchant": "HIGHLANDS COFFEE", "merchant_display": "Highlands Coffee", "lines": ["HIGHLANDS COFFEE", "Mai Hac DeHai Ba Trung.Ha Ho1", "ShopID:245", "Hoa Don Thanh Toan", "In Store", "Pager: 07", "Check:200497", "Ngay[so-dai]:16", "POSO1", "Phin Sua Da L", "45.000", "-Ecode Coupon giam", "-29.000", "Thanh tien:", "16.000", "Tong tien:", "16.000", "VietQRPay", "16.000", "* Chia se y kien cua ban voi chung toi*", "Chao ban moi!Mien phi nuoc ngon", "Tai App thanh vien HighlandsDI!"], "category": "dining", "category_source": "brand", "needs_review": false, "transacted_at": "2026-04-11T13:16:00+07:00", "total_source": "text"}""";

    private const string RealClassifyJson =
        """{"category_slug":"food","local_category":"dining","confidence":0.8,"source":"brand","processing_ms":13}""";

    private static readonly Guid UserId = Guid.NewGuid();

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }
        public string? SeenBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            SeenBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OcrServiceClient Client, StubHandler Handler) ClientReturning(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://ocr-service:8000/") };
        return (new OcrServiceClient(http), handler);
    }

    private static OcrServiceRequest Request() => new(UserId, [1, 2, 3, 4], "hoa-don.jpg", "image/jpeg");

    [Fact]
    public async Task ARealOcrAnswerDeserialisesIntoEveryField()
    {
        var (client, _) = ClientReturning(_ => Task.FromResult(Json(HttpStatusCode.OK, RealOcrJson)));

        var result = await client.AnalyzeAsync(Request());

        result.Readable.Should().BeTrue();
        result.TotalAmount.Should().Be(16_000);
        result.MerchantName.Should().Be("HIGHLANDS COFFEE");
        result.MerchantDisplay.Should().Be("Highlands Coffee");
        result.Lines.Should().HaveCount(22).And.Contain("Phin Sua Da L");
        result.Category.Should().Be("dining");
        result.CategorySource.Should().Be("brand");
        result.NeedsReview.Should().BeFalse();
        result.TotalSource.Should().Be("text");
        result.TransactedAt.Should().Be(new DateTimeOffset(2026, 4, 11, 13, 16, 0, TimeSpan.FromHours(7)));
        result.TransactedAt!.Value.Offset.Should().Be(TimeSpan.FromHours(7));
    }

    [Fact]
    public async Task TheImageGoesAsMultipartAndTheUserOnlyAsAHash()
    {
        var (client, handler) = ClientReturning(_ => Task.FromResult(Json(HttpStatusCode.OK, RealOcrJson)));

        await client.AnalyzeAsync(Request());

        handler.Seen!.Method.Should().Be(HttpMethod.Post);
        handler.Seen.RequestUri!.AbsoluteUri.Should().Be("http://ocr-service:8000/analyze-bill");
        handler.SeenBody.Should().Contain("name=image").And.Contain("hoa-don.jpg");
        var hash = handler.Seen.Headers.GetValues("X-User-Hash").Single();
        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
        hash.Should().NotContain(UserId.ToString("N")[..8]);
        handler.SeenBody.Should().NotContain(UserId.ToString());
    }

    [Fact]
    public async Task NotAReceiptIsAnUnreadableImageNotAnOutage()
    {
        var (client, _) = ClientReturning(_ => Task.FromResult(
            Json(HttpStatusCode.UnprocessableEntity, """{"error":"Ảnh này không giống hóa đơn.","code":"not_a_receipt"}""")));

        var result = await client.AnalyzeAsync(Request());

        result.Readable.Should().BeFalse();
        result.TotalAmount.Should().BeNull();
        result.Lines.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]  // 422 không phải not_a_receipt (vd. thiếu trường) vẫn là lỗi
    public async Task OtherFailuresAreReportedAsUnavailable(HttpStatusCode status)
    {
        var (client, _) = ClientReturning(_ => Task.FromResult(Json(status, """{"error":"x"}""")));

        var act = () => client.AnalyzeAsync(Request());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public async Task NetworkErrorsAndTimeoutsAreReportedAsUnavailable()
    {
        var (down, _) = ClientReturning(_ => throw new HttpRequestException("down"));
        var (slow, _) = ClientReturning(_ => throw new TaskCanceledException("timeout"));

        await ((Func<Task>)(() => down.AnalyzeAsync(Request()))).Should().ThrowAsync<AIServiceUnavailableException>();
        await ((Func<Task>)(() => slow.AnalyzeAsync(Request()))).Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public async Task AGarbledAnswerIsReportedAsUnavailable()
    {
        var (client, _) = ClientReturning(_ => Task.FromResult(Json(HttpStatusCode.OK, "<html>bad gateway</html>")));

        var act = () => client.AnalyzeAsync(Request());

        await act.Should().ThrowAsync<AIServiceUnavailableException>();
    }

    [Fact]
    public void ARealClassifyAnswerDeserialisesIntoEveryField()
    {
        var response = JsonSerializer.Deserialize<BillClassifyApiResponse>(RealClassifyJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        response.CategorySlug.Should().Be("food");
        response.LocalCategory.Should().Be("dining");
        response.Confidence.Should().Be(0.8);
        response.Source.Should().Be("brand");
    }

    [Fact]
    public void TheClassifyRequestIsSentWithTheNamesTheAiServiceExpects()
    {
        var json = JsonSerializer.Serialize(
            new BillClassifyApiRequest(new string('a', 64), "HIGHLANDS COFFEE", ["a", "b"]),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("user_id_hash").GetString().Should().HaveLength(64);
        doc.RootElement.GetProperty("merchant_name").GetString().Should().Be("HIGHLANDS COFFEE");
        doc.RootElement.GetProperty("lines").GetArrayLength().Should().Be(2);
        doc.RootElement.TryGetProperty("remembered_category", out _).Should().BeFalse();  // AI Service tự tra bộ nhớ, backend không gửi gợi ý
    }

    [Fact]
    public async Task TheDisabledClientSaysItIsNotConfigured()
    {
        var disabled = new DisabledOcrServiceClient();

        disabled.IsConfigured.Should().BeFalse();
        await ((Func<Task>)(() => disabled.AnalyzeAsync(Request()))).Should().ThrowAsync<InvalidOperationException>();
    }
}
