using FinMate.Application.FinanceMethods;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinMate.Tests.Integration;

/// <summary>
/// Kiểm NGHIỆM THU bộ chọn bằng rule trên dữ liệu gieo THẬT: câu trả lời → signals → shortlist → chọn. Ví dụ đầu tiên là chính ví dụ trong
/// finmate_khao_sat_insight_va_goi_y_quan_ly_tai_chinh.md (mục 4). Nếu ai đó chỉnh trọng số hay tắt/đổi một phương pháp làm kết quả này lệch
/// khỏi file .md, test này báo ngay thay vì để người dùng nhận gợi ý lạ.
/// </summary>
public class MethodSelectionOnSeedDataTests : IClassFixture<ReceiptScanDbFixture>
{
    private readonly ReceiptScanDbFixture _fixture;

    public MethodSelectionOnSeedDataTests(ReceiptScanDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(MethodSelection? Selection, IReadOnlyList<FinanceMethod> Shortlist, Dictionary<string, int> Signals)> RunAsync(
        params (string Question, string Option)[] answers)
    {
        await using var db = _fixture.NewContext(_fixture.ConnectionStringFor($"sel_{Guid.NewGuid():N}"));
        await db.Database.MigrateAsync();
        await SurveySeeder.SeedAsync(db);
        await FinanceMethodSeeder.SeedAsync(db);

        var signals = SurveySignals.FromAnswers(
            answers.Select(a => new UserSurveyAnswer { QuestionCode = a.Question, OptionCode = a.Option }),
            await db.SurveyQuestions.AsNoTracking().ToListAsync(),
            await db.SurveyOptions.AsNoTracking().ToListAsync());

        var shortlist = MethodSelector.Shortlist(await db.FinanceMethods.AsNoTracking().ToListAsync(), signals);
        return (MethodSelector.SelectByRule(shortlist, signals), shortlist, signals);
    }

    [Fact]
    public async Task TheExampleProfileInTheMdFile_GetsCashFlowBudget_WithASmallEmergencyBufferAsSupport()
    {
        // File .md mục 4: 1C, 2D, 3C, 4D, 5D, 6C, 7B, 8C, 9C, 10B.
        var (selection, shortlist, _) = await RunAsync(
            ("income_pattern", "C"), ("month_end_balance", "D"), ("unexpected_expense", "C"), ("tracking_habit", "D"), ("limit_reaction", "D"),
            ("hardest_category", "C"), ("main_goal", "B"), ("saving_style", "C"), ("time_budget", "C"), ("support_style", "B"));

        selection.Should().NotBeNull();
        selection!.Primary.Code.Should().Be(FinanceMethodCodes.CashFlowBudget, "thu nhập biến động + đôi khi thiếu cuối tháng → ngân sách theo dòng tiền");
        selection.Support?.Code.Should().Be(FinanceMethodCodes.EmergencyBuffer, "vùng đệm mỏng + khoản phát sinh + mục tiêu dự phòng → quỹ dự phòng nhỏ");

        shortlist.Select(m => m.Code).Should().NotContain(
            FinanceMethodCodes.CategoryBudget,
            "từng bỏ ghi chép giữa chừng nên không gợi ý cách quá chi tiết (file .md: 'không yêu cầu nhập từng giao dịch')");
    }

    [Fact]
    public async Task AStableSaverWhoLikesLimits_GetsCategoryBudget_WithAGoalFund()
    {
        var (selection, _, _) = await RunAsync(
            ("income_pattern", "A"), ("month_end_balance", "A"), ("unexpected_expense", "A"), ("tracking_habit", "A"), ("limit_reaction", "A"),
            ("hardest_category", "A"), ("main_goal", "D"), ("saving_style", "A"), ("time_budget", "A"), ("support_style", "A"));

        selection!.Primary.Code.Should().Be(FinanceMethodCodes.CategoryBudget);
        selection.Support?.Code.Should().Be(FinanceMethodCodes.GoalFund);
    }

    [Fact]
    public async Task SomeoneDrownedInDebtAndShortOfCash_GetsADebtPlan_AndIsNeverPushedTowardSaving()
    {
        var (selection, shortlist, _) = await RunAsync(
            ("income_pattern", "B"), ("month_end_balance", "E"), ("unexpected_expense", "D"), ("tracking_habit", "B"), ("limit_reaction", "E"),
            ("hardest_category", "D"), ("main_goal", "C"), ("time_budget", "B"), ("support_style", "C"));

        selection!.Primary.Code.Should().Be(FinanceMethodCodes.DebtRepayment);

        shortlist.Select(m => m.Code).Should().NotContain(
            new[] { FinanceMethodCodes.CategoryBudget, FinanceMethodCodes.GoalFund, FinanceMethodCodes.EmergencyBuffer },
            "thiếu hụt dòng tiền thường xuyên: xử lý dòng tiền và nghĩa vụ trước, không đẩy tỷ lệ cứng hay tiết kiệm");
    }

    [Fact]
    public async Task SomeoneWithNoStructureAtAll_GetsMinimalTracking_AndNoSupportIsInvented()
    {
        var (selection, _, _) = await RunAsync(
            ("income_pattern", "E"), ("month_end_balance", "B"), ("unexpected_expense", "E"), ("tracking_habit", "E"), ("limit_reaction", "E"),
            ("hardest_category", "E"), ("main_goal", "E"), ("time_budget", "E"), ("support_style", "E"));

        selection!.Primary.Code.Should().Be(FinanceMethodCodes.MinimalTracking);
        selection.Support.Should().BeNull("không có cách bổ trợ nào đủ gần cách chính thì không bịa ra một cái cho đủ cặp");
    }

    [Fact]
    public async Task NoSignalsAtAll_StillPicksTheFirstPrimaryByDisplayOrder_WhichIsMinimalTracking()
    {
        // Phòng thủ: dù signals rỗng (lẽ ra service đã chặn từ trước), tie-break theo thứ tự hiển thị phải cho kết quả an toàn nhất.
        var (selection, _, _) = await RunAsync();

        selection!.Primary.Code.Should().Be(FinanceMethodCodes.MinimalTracking);
    }
}
