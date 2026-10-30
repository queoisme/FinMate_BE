using FinMate.Infrastructure.Security;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Mã hóa key bên thứ ba: khứ hồi đúng, tên key là dữ liệu xác thực kèm, sai khóa / bị sửa thì trả null thay vì ném, không có khóa hợp lệ thì không mã hóa gì.</summary>
public class SecretCipherTests
{
    private const string Master = "25FD-4k4PLP9WajKvg-hcbqnVhvEcpPi_yswo4AOk4Q=";
    private const string Other = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Fact]
    public void RoundTrip_Works_AndNeverStoresThePlaintext()
    {
        var cipher = new SecretCipher(Master);

        var (bytes, nonce) = cipher.Encrypt("brevo_api_key", "xkeysib-secret-value");

        nonce.Should().HaveCount(SecretCipher.NonceSize);
        System.Text.Encoding.UTF8.GetString(bytes).Should().NotContain("secret-value");
        cipher.Decrypt("brevo_api_key", bytes, nonce).Should().Be("xkeysib-secret-value");
    }

    [Fact]
    public void ACiphertextCopiedToAnotherName_OrOpenedWithAnotherKey_OrTampered_IsRejected()
    {
        var cipher = new SecretCipher(Master);
        var (bytes, nonce) = cipher.Encrypt("brevo_api_key", "value");

        cipher.Decrypt("google_client_secret", bytes, nonce).Should().BeNull();
        new SecretCipher(Other).Decrypt("brevo_api_key", bytes, nonce).Should().BeNull();
        var tampered = (byte[])bytes.Clone();
        tampered[0] ^= 1;
        cipher.Decrypt("brevo_api_key", tampered, nonce).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("c2hvcnQ=")]
    public void WithoutAValid32ByteKey_NothingIsEncrypted(string? master)
    {
        var cipher = new SecretCipher(master);

        cipher.Available.Should().BeFalse();
        cipher.Invoking(c => c.Encrypt("brevo_api_key", "value")).Should().Throw<InvalidOperationException>();
    }
}
