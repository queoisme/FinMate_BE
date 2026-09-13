using FinMate.Application.Transactions.Commands;
using FinMate.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FinMate.Tests.Unit.Application.Transactions;

/// <summary>
/// <c>Source</c> không chỉ là nhãn thống kê: <c>Notification</c> là điều kiện lọc của tỉ lệ
/// "người dùng sửa lại danh mục AI đoán" ở <c>/api/v1/admin/ai-stats</c>. Client tự khai được
/// giá trị đó thì thước đo chất lượng AI bị bóp méo bởi dữ liệu người dùng tự nhập — và không
/// ai nhìn ra, vì con số vẫn trông hoàn toàn hợp lý.
/// </summary>
public class TransactionSourceGuardTests
{
    private static readonly CreateManualTransactionCommandValidator Validator = new();

    private static CreateManualTransactionCommand Command(TransactionSource source)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            50_000,
            TransactionType.Debit,
            DateTimeOffset.UtcNow,
            "Highlands Coffee",
            null,
            source);

    [Theory]
    [InlineData(TransactionSource.Manual)]
    [InlineData(TransactionSource.Voice)]
    [InlineData(TransactionSource.Receipt)]
    public void ChannelsAUserCanActuallyUseAreAccepted(TransactionSource source)
    {
        Validator.Validate(Command(source)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AClientCannotClaimATransactionCameFromTheAi()
    {
        var result = Validator.Validate(Command(TransactionSource.Notification));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateManualTransactionCommand.Source));
    }

    [Fact]
    public void TheDefaultChannelIsManual()
    {
        // Client cũ không biết tới trường này vẫn phải chạy như trước.
        var command = new CreateManualTransactionCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            50_000,
            TransactionType.Debit,
            DateTimeOffset.UtcNow,
            null,
            null);

        command.Source.Should().Be(TransactionSource.Manual);
        Validator.Validate(command).IsValid.Should().BeTrue();
    }
}
