using FinMate.Application.Common.Interfaces;
using FinMate.Application.QrTransfers;
using FinMate.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.QrTransfers;

public class QrTransferLinkerTests
{
    private readonly Mock<IQrTransferRepository> _repository = new();
    private readonly Mock<ISystemConfigProvider> _config = new();
    private readonly Mock<IMerchantCategoryOverrideRepository> _merchantCategoryOverrides = new();
    private readonly QrTransferLinker _linker;

    public QrTransferLinkerTests()
    {
        _linker = new QrTransferLinker(
            _repository.Object, _config.Object, _merchantCategoryOverrides.Object, new LoggerFactory().CreateLogger<QrTransferLinker>());
    }

    private QrTransferIntent Intent(Guid userId, Guid transactionId, string? receiverName = "NGUYEN VAN A") => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        ReceiverBankBin = "970436",
        ReceiverAccountNumber = "0123456789",
        ReceiverName = receiverName,
        MatchedTransactionId = transactionId,
    };

    [Fact]
    public async Task LearnCategoryAsync_WritesCounterpartyOverride_AndDualWritesMerchantOverride()
    {
        // §16.6: QR flow ghi CounterpartyCategoryOverride (khóa BankBin+AccountNumber) PHẢI kèm
        // dual-write sang MerchantCategoryOverride (khóa tên) để Flow 1 cũng thấy được.
        var userId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var intent = Intent(userId, transactionId);
        _repository.Setup(r => r.GetIntentByTransactionAsync(transactionId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(intent);
        _repository.Setup(r => r.GetOverrideAsync(userId, intent.ReceiverBankBin, intent.ReceiverAccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounterpartyCategoryOverride?)null);
        _merchantCategoryOverrides.Setup(r => r.GetAsync(userId, "nguyen van a", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MerchantCategoryOverride?)null);

        await _linker.LearnCategoryAsync(userId, transactionId, categoryId);

        _repository.Verify(r => r.AddOverrideAsync(
            It.Is<CounterpartyCategoryOverride>(o => o.BankBin == intent.ReceiverBankBin && o.CategoryId == categoryId),
            It.IsAny<CancellationToken>()), Times.Once);
        _merchantCategoryOverrides.Verify(r => r.AddAsync(
            It.Is<MerchantCategoryOverride>(o => o.MerchantKey == "nguyen van a" && o.CategoryId == categoryId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LearnCategoryAsync_WithoutReceiverName_SkipsMerchantDualWrite()
    {
        var userId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var intent = Intent(userId, transactionId, receiverName: null);
        _repository.Setup(r => r.GetIntentByTransactionAsync(transactionId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(intent);
        _repository.Setup(r => r.GetOverrideAsync(userId, intent.ReceiverBankBin, intent.ReceiverAccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounterpartyCategoryOverride?)null);

        await _linker.LearnCategoryAsync(userId, transactionId, categoryId);

        _merchantCategoryOverrides.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LearnCategoryAsync_MerchantDualWriteThrows_DoesNotFailCall_CounterpartyOverrideStillWritten()
    {
        // Lỗi ở nhánh phụ (MerchantCategoryOverride) không được làm hỏng ghi chính
        // (CounterpartyCategoryOverride) — chỉ log, không throw, không rollback.
        var userId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var intent = Intent(userId, transactionId);
        _repository.Setup(r => r.GetIntentByTransactionAsync(transactionId, userId, It.IsAny<CancellationToken>())).ReturnsAsync(intent);
        _repository.Setup(r => r.GetOverrideAsync(userId, intent.ReceiverBankBin, intent.ReceiverAccountNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CounterpartyCategoryOverride?)null);
        _merchantCategoryOverrides.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var act = () => _linker.LearnCategoryAsync(userId, transactionId, categoryId);

        await act.Should().NotThrowAsync();
        _repository.Verify(r => r.AddOverrideAsync(It.IsAny<CounterpartyCategoryOverride>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
