using FinMate.API.Controllers.Admin;
using FinMate.Application.Common;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Api;

/// <summary>Logo tải lên được nhận dạng theo nội dung (không tin đuôi file) và tên bucket phải đúng định dạng.</summary>
public class BrandLogoUploadTests
{
    [Fact]
    public void SniffsPngJpegAndWebpByContent()
    {
        AdminBrandLogoController.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]).Should().Be(("image/png", "png"));
        AdminBrandLogoController.Sniff([0xFF, 0xD8, 0xFF, 0xE0]).Should().Be(("image/jpeg", "jpg"));
        AdminBrandLogoController.Sniff("RIFF\0\0\0\0WEBP"u8.ToArray()).Should().Be(("image/webp", "webp"));
    }

    [Fact]
    public void RejectsSvgAndOtherContent()
    {
        AdminBrandLogoController.Sniff("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8.ToArray()).Should().BeNull();
        AdminBrandLogoController.Sniff([]).Should().BeNull();
    }

    [Theory]
    [InlineData("finmate-1234.firebasestorage.app", true)]
    [InlineData("finmate-1234.appspot.com", true)]
    [InlineData("Finmate Bucket", false)]
    [InlineData("gs://finmate", false)]
    public void ValidatesBucketName(string value, bool valid)
    {
        var definition = BackendSecrets.Find(BackendSecrets.FirebaseStorageBucket)!;
        (definition.Validate(value) is null).Should().Be(valid);
    }
}
