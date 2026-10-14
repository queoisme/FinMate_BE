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
        training.Setup(t => t.EraseAsync(userId, It.IsAny<CancellationToken>())).Callback(() => order.Add("erase")).Returns(Task.CompletedTask);

        await new DataDeletionJob(requests.Object, hardDeleter.Object, training.Object).RunAsync();

        Assert.Equal(["erase", "hard-delete"], order);
        requests.Verify(r => r.MarkProcessedAsync(request, It.IsAny<CancellationToken>()), Times.Once);
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
