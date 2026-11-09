using FinMate.Infrastructure.Community;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Community;

public class SkiaCommunityImageProcessorTests
{
    private readonly SkiaCommunityImageProcessor _processor = new();

    [Fact]
    public async Task ABigImageIsScaledDownKeepingItsShape()
    {
        var result = await _processor.ProcessAsync(TestImages.Jpeg(3200, 1600));
        result!.Width.Should().Be(1600);
        result.Height.Should().Be(800);
        TestImages.SizeOf(result.Data).Should().Be((1600, 800));
    }

    [Fact]
    public async Task ASmallImageIsNotEnlarged()
    {
        var result = await _processor.ProcessAsync(TestImages.Jpeg(300, 200));
        (result!.Width, result.Height).Should().Be((300, 200));
    }

    [Theory]
    [InlineData(1, 800, 400)]
    [InlineData(3, 800, 400)]
    [InlineData(6, 400, 800)]
    [InlineData(8, 400, 800)]
    public async Task ThePhoneRotationIsAppliedBeforeExifIsDropped(ushort orientation, int width, int height)
    {
        var result = await _processor.ProcessAsync(TestImages.JpegWithGps(800, 400, orientation));
        (result!.Width, result.Height).Should().Be((width, height));
        TestImages.ContainsGpsMarker(result.Data).Should().BeFalse();
        TestImages.HasExifSegment(result.Data).Should().BeFalse();
    }

    // Theo bảng chiều xoay của EXIF: góc (0, 0) của ảnh gốc đi tới đâu trong ảnh đứng.
    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 0)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(8, 3)]
    public async Task EveryRotationPutsThePictureTheRightWayUp(ushort orientation, int expectedCorner)
    {
        var result = await _processor.ProcessAsync(TestImages.JpegWithGps(100, 50, orientation));
        TestImages.RedCorner(result!.Data).Should().Be(expectedCorner);
        TestImages.CenterIsBlue(result.Data).Should().BeTrue();
    }

    [Theory]
    [InlineData("fake")]
    [InlineData("heic")]
    [InlineData("huge")]
    [InlineData("truncated")]
    public async Task BadFilesReturnNullInsteadOfThrowing(string kind)
    {
        var content = kind switch
        {
            "fake" => "không phải ảnh"u8.ToArray(),
            "heic" => TestImages.HeicHeader(),
            "huge" => TestImages.PngClaiming(9000, 9000),
            _ => TestImages.Jpeg(100, 100).Take(12).ToArray(),
        };

        (await _processor.ProcessAsync(content)).Should().BeNull();
    }
}
