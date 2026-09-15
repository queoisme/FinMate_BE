using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Lỗi thật gặp phải khi cắm Brevo là tài khoản chặn theo IP, mà log chỉ ghi "HTTP 401" nên
/// phải gọi tay sang Brevo mới biết. Bóc lý do ra là để lần sau đọc log là đủ — nhưng KHÔNG
/// được đổi lấy việc nội dung email lọt vào log.
/// </summary>
public class BrevoFailureReasonTests
{
    [Fact]
    public void TheRealRejectionReadsAsItself()
    {
        const string body = """
            {"code":"unauthorized","message":"We have detected you are using an unrecognised IP address 42.112.230.30."}
            """;

        BrevoEmailSender.DescribeFailure(body)
            .Should().StartWith("unauthorized: ")
            .And.Contain("unrecognised IP address");
    }

    [Fact]
    public void NothingOutsideCodeAndMessageIsEverRepeated()
    {
        // Điểm cốt tử. Nếu Brevo đổi và dội payload kèm cả trong phản hồi lỗi, cách bóc theo
        // TÊN TRƯỜNG vẫn không để mã OTP lọt ra — ghi nguyên body thì có.
        const string body = """
            {"code":"bad_request","message":"nope","textContent":"Mã của bạn là: 123456","to":[{"email":"a@b.c"}]}
            """;

        var described = BrevoEmailSender.DescribeFailure(body);

        described.Should().Be("bad_request: nope");
        described.Should().NotContain("123456").And.NotContain("a@b.c");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void AnEmptyOrSilentResponseSaysSoPlainly(string? body)
    {
        BrevoEmailSender.DescribeFailure(body).Should().Be("(no detail)");
    }

    [Fact]
    public void AResponseThatIsNotJsonIsNeverEchoed()
    {
        // Một trang lỗi HTML từ proxy chẳng hạn: không đoán, và không ghi nguyên văn.
        BrevoEmailSender.DescribeFailure("<html>502 Bad Gateway</html>")
            .Should().Be("(unparseable response)");
    }

    [Fact]
    public void OnlyOneOfTheTwoFieldsStillReadsWell()
    {
        BrevoEmailSender.DescribeFailure("""{"code":"unauthorized"}""").Should().Be("unauthorized");
        BrevoEmailSender.DescribeFailure("""{"message":"khong ro"}""").Should().Be("khong ro");
    }
}
