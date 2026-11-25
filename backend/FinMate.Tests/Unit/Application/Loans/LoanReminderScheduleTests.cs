using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Loans;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using FluentAssertions;
using Moq;
using Xunit;

namespace FinMate.Tests.Unit.Application.Loans;

/// <summary>Lịch nhắc khoản vay (Phase 90; chép từ bản sổ nợ Phase 86 cũ, BR-DEBT-14, D25) và cổng huỷ nhắc đã hoãn.</summary>
public class LoanReminderScheduleTests
{
    private static readonly DateOnly Due = new(2026, 11, 20);
    private static readonly LoanSettings Settings = new();   // 3 ngày trước, lặp 7 ngày, tối đa 3 lần

    private static Loan Open(DateOnly? due = null) => new()
    {
        Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Title = "Khoản Lan", Counterparty = "Lan", Direction = LoanDirection.Lend, Status = LoanStatus.Open,
        PrincipalCents = 1_000_000, DueDate = due ?? Due,
    };

    [Theory]
    [InlineData(-4, null)]
    [InlineData(-3, "before")]
    [InlineData(0, "before")]
    [InlineData(1, "overdue:0")]
    [InlineData(7, "overdue:0")]
    [InlineData(8, "overdue:1")]
    [InlineData(15, "overdue:2")]
    [InlineData(21, "overdue:2")]
    [InlineData(22, null)]
    public void TheScheduleFollowsTheDueDate(int daysAfterDue, string? expectedSuffix)
    {
        var debt = Open();

        var reminder = LoanReminderSchedule.For(debt, Due.AddDays(daysAfterDue), Settings);

        if (expectedSuffix is null)
        {
            reminder.Should().BeNull();
            return;
        }

        reminder!.DedupKey.Should().Be($"loan_due:{debt.Id}:2026-11-20:{expectedSuffix}");
        reminder.Phase.Should().Be(expectedSuffix.StartsWith("before") ? "before" : "overdue");
    }

    [Fact]
    public void ChangingTheDueDate_StartsANewCycle()
    {
        var debt = Open();
        var first = LoanReminderSchedule.For(debt, Due, Settings)!.DedupKey;

        debt.DueDate = Due.AddDays(10);

        LoanReminderSchedule.For(debt, Due.AddDays(10), Settings)!.DedupKey.Should().NotBe(first);
    }

    [Fact]
    public void ClosedUndatedOrPaidDebts_AreNotReminded()
    {
        var settled = Open();
        settled.Status = LoanStatus.Settled;
        var writtenOff = Open();
        writtenOff.Status = LoanStatus.WrittenOff;
        var undated = Open();
        undated.DueDate = null;
        var deleted = Open();
        deleted.DeletedAt = DateTimeOffset.UtcNow;
        var paid = Open();
        paid.RepaidCents = 1_000_000;

        new[] { settled, writtenOff, undated, deleted, paid }.Select(d => LoanReminderSchedule.For(d, Due, Settings)).Should().OnlyContain(r => r == null);
    }

    [Fact]
    public void TheTextNamesNoOneAndNoAmount()
    {
        LoanReminderText.Body("before").Should().Be("Bạn có một khoản nợ sắp đến hạn");
        LoanReminderText.Body("overdue").Should().Be("Bạn có một khoản nợ đã quá hạn");
        LoanReminderText.Title.Should().Be("Sổ vay");
    }

    private static ForecastAlert Alert(Loan debt, string key) => new()
    {
        Id = Guid.NewGuid(), UserId = debt.UserId, Kind = ForecastAlertKind.LoanDue, DedupKey = key, Title = "t", Body = "b", Data = "{}",
    };

    [Fact]
    public async Task TheGate_DropsADeferredReminderOnceTheDebtIsClosedOrItsDueDateMoved()
    {
        var debt = Open();
        var repo = new Mock<ILoanRepository>();
        repo.Setup(r => r.GetOwnedAsync(debt.Id, debt.UserId, It.IsAny<CancellationToken>())).ReturnsAsync(debt);
        var gate = new LoanReminderGate(repo.Object);
        var data = new Dictionary<string, string> { ["loanId"] = debt.Id.ToString(), ["phase"] = "before" };
        var key = LoanReminderSchedule.For(debt, Due, Settings)!.DedupKey;

        (await gate.StillRelevantAsync(Alert(debt, key), data)).Should().BeTrue();

        debt.DueDate = Due.AddDays(5);
        (await gate.StillRelevantAsync(Alert(debt, key), data)).Should().BeFalse("hạn đã đổi");

        debt.DueDate = Due;
        debt.Status = LoanStatus.Settled;
        (await gate.StillRelevantAsync(Alert(debt, key), data)).Should().BeFalse("đã trả xong");

        var other = new ForecastAlert { Kind = ForecastAlertKind.LowBalance, DedupKey = "x", Title = "t", Body = "b", Data = "{}" };
        (await gate.StillRelevantAsync(other, new Dictionary<string, string>())).Should().BeTrue("loại khác luôn gửi");
    }
}
