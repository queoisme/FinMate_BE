using FinMate.Application.Common.Interfaces;
using FinMate.Application.Transactions;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.BackgroundJobs;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

/// <summary>
/// Xóa cứng người dùng đến hạn: mẫu huấn luyện người đó đã đóng góp (ai-service) phải bị xóa TRƯỚC khi xóa tài khoản, vì sau đó không còn
/// cách nào tìm ra họ để xóa.
/// </summary>
public class DataDeletionJobTests
{
    [Fact]
    public async Task ContributedTrainingSamplesAreErasedBeforeTheAccountIsHardDeleted()
    {
        var userId = Guid.NewGuid();
        var request = new DataDeletionRequest { Id = Guid.NewGuid(), UserId = userId };
        var requests = new Mock<IDataDeletionRequestRepository>();
        requests.Setup(r => r.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([request]);
        var order = new List<string>();
        var hardDeleter = new Mock<IUserHardDeleter>();
        hardDeleter.Setup(h => h.HardDeleteAsync(userId, It.IsAny<CancellationToken>())).Callback(() => order.Add("hard-delete")).Returns(Task.CompletedTask);
        var training = new Mock<ITrainingSampleService>();
        training.Setup(t => t.EraseAsync(userId, It.IsAny<CancellationToken>())).Callback(() => order.Add("erase")).ReturnsAsync(true);

        await new DataDeletionJob(requests.Object, hardDeleter.Object, training.Object).RunAsync();

        Assert.Equal(["erase", "hard-delete"], order);
        requests.Verify(r => r.MarkProcessedAsync(request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WhenTheEraseFailsTheUserIsKeptSoTheNextRunRetries()
    {
        var userId = Guid.NewGuid();
        var request = new DataDeletionRequest { Id = Guid.NewGuid(), UserId = userId };
        var requests = new Mock<IDataDeletionRequestRepository>();
        requests.Setup(r => r.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([request]);
        var hardDeleter = new Mock<IUserHardDeleter>();
        var training = new Mock<ITrainingSampleService>();
        training.Setup(t => t.EraseAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await new DataDeletionJob(requests.Object, hardDeleter.Object, training.Object).RunAsync();

        // xóa cứng xong thì không còn cách nào tìm ra dữ liệu của họ ở ai-service
        hardDeleter.Verify(h => h.HardDeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        requests.Verify(r => r.MarkProcessedAsync(It.IsAny<DataDeletionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutATrainingServiceTheJobStillHardDeletes()
    {
        var userId = Guid.NewGuid();
        var request = new DataDeletionRequest { Id = Guid.NewGuid(), UserId = userId };
        var requests = new Mock<IDataDeletionRequestRepository>();
        requests.Setup(r => r.GetDueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync([request]);
        var hardDeleter = new Mock<IUserHardDeleter>();

        await new DataDeletionJob(requests.Object, hardDeleter.Object).RunAsync();

        hardDeleter.Verify(h => h.HardDeleteAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
