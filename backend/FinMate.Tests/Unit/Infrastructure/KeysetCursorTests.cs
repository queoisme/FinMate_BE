using FinMate.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using FluentValidation;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class KeysetCursorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoCursorMeansFirstPage(string? cursor)
    {
        KeysetCursor.DecodeOrThrow(cursor, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ACursorWeIssuedDecodes()
    {
        var at = new DateTimeOffset(2026, 11, 28, 1, 2, 3, TimeSpan.Zero);
        var id = Guid.NewGuid();

        KeysetCursor.DecodeOrThrow(KeysetCursor.Encode(at, id), out var gotAt, out var gotId).Should().BeTrue();

        gotAt.Should().Be(at);
        gotId.Should().Be(id);
    }

    [Theory]
    [InlineData("@@@")]
    [InlineData("aGVsbG8=")]            // base64 hợp lệ nhưng không phải "thời điểm|id"
    [InlineData("MjAyNnxub3QtYS1ndWlk")] // đúng dạng, id hỏng
    public void AGarbageCursorIsAValidationError_NotASilentFirstPage(string cursor)
    {
        // Bỏ qua cursor hỏng làm "Trang tiếp" quay về trang đầu mà không ai biết lý do.
        var act = () => KeysetCursor.DecodeOrThrow(cursor, out _, out _);

        act.Should().Throw<ValidationException>();
    }
}
