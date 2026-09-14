using FinMate.Infrastructure.ExternalServices;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Khi FCM hỏng trước cả lúc gọi được (credentials sai), SDK nhét exception gốc vào phần đầu
/// của Message chứ không giữ làm InnerException — và <c>ErrorCode</c> chỉ là "Unknown". Lấy
/// tên kiểu ở đầu chuỗi đó là cách duy nhất còn tín hiệu để log.
/// </summary>
public class FcmFailureReasonTests
{
    [Fact]
    public void NamesTheRealCauseOfACredentialsFailure()
    {
        // Nguyên văn chuỗi FCM trả về khi service-account không hợp lệ.
        const string message =
            "Google.Apis.Auth.OAuth2.Responses.TokenResponseException: Error:\"invalid_grant\", "
            + "Description:\"Invalid grant: account not found\", Uri:\"\"";

        FirebaseAdminFcmSender.LeadingExceptionTypeName(message)
            .Should().Be("Google.Apis.Auth.OAuth2.Responses.TokenResponseException");
    }

    [Fact]
    public void NeverCarriesAnythingPastTheTypeName()
    {
        // Điểm cốt tử: lỗi thuộc về chính token thì FCM nhắc lại token trong Message. Token là
        // capability — ai cầm được nó đều đẩy thông báo xuống máy đó — nên nó không bao giờ
        // được phép đi vào log.
        const string message =
            "FirebaseMessagingException: The registration token fGx9aQ-SECRET-TOKEN-VALUE is not valid";

        var reason = FirebaseAdminFcmSender.LeadingExceptionTypeName(message);

        reason.Should().Be("FirebaseMessagingException");
        reason.Should().NotContain("SECRET-TOKEN-VALUE");
    }

    [Fact]
    public void AMessageThatIsNotATypeNameYieldsNothing()
    {
        FirebaseAdminFcmSender.LeadingExceptionTypeName("Something went wrong with token abc123")
            .Should().BeNull();
    }

    [Fact]
    public void AnEmptyMessageYieldsNothing()
    {
        FirebaseAdminFcmSender.LeadingExceptionTypeName("").Should().BeNull();
    }
}
