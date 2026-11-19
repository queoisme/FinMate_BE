using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Infrastructure;

public class LoanDueReminderJobTests
{
    [Fact]
    public async Task Run_PushesOnce_NoAmountInBody_AndMarksNotified()
    {
        var loans = new Mock<ILoanRepository>();
        var users = new Mock<IUserRepository>();
        var push = new Mock<IPushNotificationService>();
        var userId = Guid.NewGuid();
        var loan = new Loan { Id = Guid.NewGuid(), UserId = userId, Title = "Vay xe", Direction = LoanDirection.Borrow, PrincipalCents = 123_456_789, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)) };
        loans.Setup(l => l.GetDueUnnotifiedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync([loan]);
        users.Setup(u => u.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(new User { Id = userId });
        string? body = null;
        push.Setup(p => p.NotifyAsync(userId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string, IReadOnlyDictionary<string, string>?, CancellationToken>((_, _, b, _, _) => body = b)
            .Returns(Task.CompletedTask);

        await new LoanDueReminderJob(loans.Object, users.Object, push.Object).RunAsync();

        body.Should().Contain("Vay xe").And.NotContain("123");
        loan.DueNotifiedAt.Should().NotBeNull();
        loans.Verify(l => l.UpdateAsync(loan, It.IsAny<CancellationToken>()), Times.Once);
    }
}
