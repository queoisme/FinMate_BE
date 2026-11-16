using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.BackgroundJobs;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Bộ nhớ món cá nhân ở ai-service chỉ tìm được qua băm của người dùng này: phải xóa TRƯỚC khi xóa cứng tài khoản, và xóa không được thì giữ nguyên người dùng.
/// </summary>
public class DataDeletionJobItemMemoryTests
{
    private readonly Guid _userId = Guid.NewGuid();
    private readonly DataDeletionRequest _request;
    private readonly Mock<IDataDeletionRequestRepository> _requests = new();
    private readonly Mock<IUserHardDeleter> _hardDeleter = new();
    private readonly Mock<ITrainingSampleService> _training = new();
    private readonly Mock<IItemMemoryService> _itemMemory = new();
    private readonly List<string> _order = [];

    public DataDeletionJobItemMemoryTests()
    {
        _request = new DataDeletionRequest { Id = Guid.NewGuid(), UserId = _userId };
        _requests.Setup(r => r.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([_request]);
        _hardDeleter.Setup(h => h.HardDeleteAsync(_userId, It.IsAny<CancellationToken>())).Callback(() => _order.Add("hard-delete")).Returns(Task.CompletedTask);
        _training.Setup(t => t.EraseAsync(_userId, It.IsAny<CancellationToken>())).Callback(() => _order.Add("training")).ReturnsAsync(true);
        _itemMemory.Setup(m => m.EraseAsync(_userId, It.IsAny<CancellationToken>())).Callback(() => _order.Add("item-memory")).ReturnsAsync(true);
    }

    private DataDeletionJob Job() => new(_requests.Object, _hardDeleter.Object, _training.Object, itemMemory: _itemMemory.Object);

    [Fact]
    public async Task DeletionJob_ErasesItemMemoryBeforeHardDelete()
    {
        await Job().RunAsync();

        Assert.Equal(["training", "item-memory", "hard-delete"], _order);
        _requests.Verify(r => r.MarkProcessedAsync(_request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletionJob_KeepsTheUserWhenItemMemoryEraseFails()
    {
        _itemMemory.Setup(m => m.EraseAsync(_userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Job().RunAsync();

        _hardDeleter.Verify(h => h.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _requests.Verify(r => r.MarkProcessedAsync(It.IsAny<DataDeletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeletionJob_StillErasesTrainingSamples()
    {
        await Job().RunAsync();

        _training.Verify(t => t.EraseAsync(_userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
