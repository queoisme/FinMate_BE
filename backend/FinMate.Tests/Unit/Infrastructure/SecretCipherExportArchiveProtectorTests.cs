using FinMate.Infrastructure.Security;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>Phase 91 (BR-EXP-09): gói dữ liệu mã hoá khi lưu, gắn với id yêu cầu, không lẫn với ngữ cảnh ảnh ticket.</summary>
public class SecretCipherExportArchiveProtectorTests
{
    private const string Master = "25FD-4k4PLP9WajKvg-hcbqnVhvEcpPi_yswo4AOk4Q=";

    [Fact]
    public void RoundTrip_ReturnsTheZipBytes_AndTheCiphertextDoesNotContainThem()
    {
        var protector = new SecretCipherExportArchiveProtector(new SecretCipher(Master));
        var id = Guid.NewGuid();
        var zip = System.Text.Encoding.UTF8.GetBytes("PK-zip-with-secret-content");

        var (cipher, nonce) = protector.Protect(id, zip);

        System.Text.Encoding.UTF8.GetString(cipher).Should().NotContain("secret-content");
        protector.Unprotect(id, cipher, nonce).Should().Equal(zip);
    }

    [Fact]
    public void ACiphertextMovedToAnotherExport_OrReadAsATicketAttachment_IsRejected()
    {
        var cipher = new SecretCipher(Master);
        var protector = new SecretCipherExportArchiveProtector(cipher);
        var id = Guid.NewGuid();

        var (bytes, nonce) = protector.Protect(id, [1, 2, 3]);

        protector.Unprotect(Guid.NewGuid(), bytes, nonce).Should().BeNull();
        new SecretCipherAttachmentProtector(cipher).Unprotect(id, bytes, nonce).Should().BeNull("cùng id nhưng khác ngữ cảnh");
    }

    [Fact]
    public void WithoutAMasterKey_ItIsUnavailable()
    {
        new SecretCipherExportArchiveProtector(new SecretCipher(null)).Available.Should().BeFalse();
    }
}
