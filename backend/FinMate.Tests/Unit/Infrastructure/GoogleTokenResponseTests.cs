using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Google trả snake_case. Bộ đọc JSON mặc định chỉ khớp camelCase, nên thiếu khai tên trường
/// thì một phản hồi 200 hoàn toàn bình thường vẫn cho ra id_token rỗng — và log chỉ ghi
/// "HTTP 200, không lỗi gì", nhìn vào không hiểu vì sao đăng nhập hỏng.
///
/// Bộ test tích hợp KHÔNG bắt được lỗi này vì nó thay cả lớp gọi HTTP bằng bản giả; chỉ chạy
/// thật với Google mới lộ ra.
/// </summary>
public class GoogleTokenResponseTests
{
    [Fact]
    public void TheIdTokenIsReadFromGooglesSnakeCaseField()
    {
        const string json = """
            {"access_token":"ya29.abc","expires_in":3599,"scope":"openid email",
             "token_type":"Bearer","id_token":"eyJhbGciOiJSUzI1NiJ9.payload.sig"}
            """;

        GoogleCodeExchanger.ParseTokenResponse(json)!.IdToken
            .Should().Be("eyJhbGciOiJSUzI1NiJ9.payload.sig");
    }

    [Fact]
    public void AnErrorResponseIsReadToo()
    {
        const string json = """
            {"error":"invalid_grant","error_description":"Bad Request"}
            """;

        var parsed = GoogleCodeExchanger.ParseTokenResponse(json)!;

        parsed.IdToken.Should().BeNull();
        parsed.Error.Should().Be("invalid_grant");
        parsed.ErrorDescription.Should().Be("Bad Request");
    }

    [Fact]
    public void CamelCaseIsNotWhatGoogleSends()
    {
        // Ghim chính cái bẫy đã sập: nếu ai đó đổi lại sang khớp theo camelCase thì test này
        // vẫn xanh một cách sai lầm, nên phải khẳng định NGƯỢC LẠI — "idToken" KHÔNG được đọc.
        GoogleCodeExchanger.ParseTokenResponse("""{"idToken":"khong-phai-dang-nay"}""")!
            .IdToken.Should().BeNull();
    }
}
