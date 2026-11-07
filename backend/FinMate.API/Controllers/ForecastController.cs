using System.Security.Claims;
using FinMate.Application.Common.Models;
using FinMate.Application.Forecasting;
using FinMate.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinMate.API.Controllers;

/// <summary>Dự báo tài chính (Flow 6, đợt 1): dòng tiền theo ngày, thử kịch bản, số tiền có thể chi. Mọi số liệu theo người dùng trong JWT.</summary>
[ApiController]
[Route("api/v1/forecast")]
[Authorize]
public class ForecastController : ControllerBase
{
    private readonly IForecastService _forecast;
    private readonly IPurchaseCheckService _purchases;
    private readonly IForecastAlertService _alerts;
    private readonly IBudgetForecastService _budgets;
    private readonly IIncomeForecastService _income;
    private readonly IAnomalyService _anomalies;
    private readonly ICategoryForecastService _categories;
    private readonly IGoalForecastService _goals;

    public ForecastController(
        IForecastService forecast,
        IPurchaseCheckService purchases,
        IForecastAlertService alerts,
        IBudgetForecastService budgets,
        IIncomeForecastService income,
        IAnomalyService anomalies,
        ICategoryForecastService categories,
        IGoalForecastService goals)
    {
        _forecast = forecast;
        _purchases = purchases;
        _alerts = alerts;
        _budgets = budgets;
        _income = income;
        _anomalies = anomalies;
        _categories = categories;
        _goals = goals;
    }

    private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Số dư dự kiến từng ngày từ hôm nay tới <paramref name="to"/> (mặc định cuối tháng). Có <paramref name="accountId"/> thì chỉ xem ví đó.</summary>
    [HttpGet("cashflow")]
    public async Task<IActionResult> Cashflow([FromQuery] Guid? accountId, [FromQuery] DateOnly? to, CancellationToken ct)
        => Ok(ApiResponse<CashflowForecastDto>.Ok(await _forecast.GetCashflowAsync(CurrentUserId, accountId, to, ct)));

    /// <summary>Tính lại số dư với các khoản thu/chi giả định. Không lưu gì.</summary>
    [HttpPost("cashflow/scenario")]
    public async Task<IActionResult> Scenario([FromBody] CashflowScenarioRequest request, CancellationToken ct)
    {
        var items = (request.Items ?? []).Select(i => new ScenarioItem(i.Date, i.AmountCents, i.Direction, i.Label ?? string.Empty)).ToList();
        var result = await _forecast.RunScenarioAsync(new CashflowScenarioCommand(CurrentUserId, request.AccountId, request.To, items), ct);
        return Ok(ApiResponse<CashflowForecastDto>.Ok(result));
    }

    /// <summary>
    /// "Mua món này có ổn không?": số dư thấp nhất trước/sau khoản mua trong kỳ tới kỳ thu nhập kế tiếp, tác động ngân sách và kế hoạch tháng,
    /// phương án khác và bảng cách tính. Chỉ đọc, không tạo giao dịch.
    /// </summary>
    [HttpPost("purchase-check")]
    public async Task<IActionResult> PurchaseCheck([FromBody] PurchaseCheckRequest request, CancellationToken ct)
        => Ok(ApiResponse<PurchaseCheckDto>.Ok(await _purchases.CheckAsync(
            new PurchaseCheckCommand(CurrentUserId, request.AmountCents, request.PurchaseDate, request.Item, request.CategoryId, request.AccountId, request.HorizonDays, request.CategorySlug), ct)));

    /// <summary>Số tiền có thể chi trong kỳ và mỗi ngày, kèm bảng cách tính.</summary>
    [HttpGet("spendable")]
    public async Task<IActionResult> Spendable(CancellationToken ct)
        => Ok(ApiResponse<SafeToSpendDto>.Ok(await _forecast.GetSpendableAsync(CurrentUserId, ct)));

    /// <summary>Tiến độ, mức chi dự kiến và trạng thái (<c>ok|at_risk|over</c>) của từng ngân sách tháng này.</summary>
    [HttpGet("budgets")]
    public async Task<IActionResult> Budgets(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<BudgetForecastDto>>.Ok(await _budgets.GetAsync(CurrentUserId, ct)));

    /// <summary>Lịch thu nhập dự kiến, khoản thu trễ, ước tính thu nhập không đều.</summary>
    [HttpGet("income")]
    public async Task<IActionResult> Income(CancellationToken ct)
        => Ok(ApiResponse<IncomeForecastDto>.Ok(await _income.GetAsync(CurrentUserId, ct)));

    /// <summary>Các cảnh báo dự báo gần nhất (mới nhất trước), để app hiển thị chi tiết.</summary>
    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] int limit = 20, CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<ForecastAlertDto>>.Ok(await _alerts.ListAsync(CurrentUserId, limit, ct)));

    /// <summary>Báo cảnh báo không chính xác. Hiện chỉ nhận <c>"inaccurate"</c>.</summary>
    [HttpPost("alerts/{id:guid}/feedback")]
    public async Task<IActionResult> Feedback(Guid id, [FromBody] AlertFeedbackRequest request, CancellationToken ct)
        => Ok(ApiResponse<ForecastAlertDto>.Ok(await _alerts.SetFeedbackAsync(CurrentUserId, id, request.Feedback, ct)));

    /// <summary>Khoản chi bị đánh dấu bất thường, mới nhất trước. <paramref name="status"/>: <c>open</c> (mặc định, chưa phản hồi) hoặc <c>all</c>.</summary>
    [HttpGet("anomalies")]
    public async Task<IActionResult> Anomalies([FromQuery] string? status = null, [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var openOnly = !string.Equals(status, AllAnomalies, StringComparison.OrdinalIgnoreCase);
        return Ok(ApiResponse<IReadOnlyList<AnomalyDto>>.Ok(await _anomalies.ListAsync(CurrentUserId, openOnly, limit, ct)));
    }

    /// <summary>Phản hồi một khoản bất thường: <c>normal</c> | <c>one_off</c> | <c>wrong_entry</c> | <c>suspicious</c>.</summary>
    [HttpPost("anomalies/{id:guid}/feedback")]
    public async Task<IActionResult> AnomalyFeedback(Guid id, [FromBody] AnomalyFeedbackRequest request, CancellationToken ct)
        => Ok(ApiResponse<AnomalyFeedbackResultDto>.Ok(await _anomalies.SetFeedbackAsync(CurrentUserId, id, request.Resolution, ct)));

    /// <summary>Chi tiêu dự kiến theo danh mục cho một tháng tới (<c>yyyy-MM</c>, mặc định tháng tới), kèm hạn mức đề xuất.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> Categories([FromQuery] string? month, CancellationToken ct)
        => Ok(ApiResponse<CategoryForecastDto>.Ok(await _categories.GetAsync(CurrentUserId, month, ct)));

    /// <summary>Lưu hạn mức (đã xem, chỉnh) làm ngân sách kỳ tới. Danh mục đã có ngân sách: áp từ tháng sau; chưa có: tạo mới, áp ngay.</summary>
    [HttpPost("categories/apply")]
    public async Task<IActionResult> ApplyCategories([FromBody] ApplyCategoryBudgetsRequest request, CancellationToken ct)
    {
        var items = (request.Items ?? []).Select(i => new CategoryBudgetItem(i.CategoryId, i.LimitCents)).ToList();
        return Ok(ApiResponse<IReadOnlyList<AppliedCategoryBudgetDto>>.Ok(await _categories.ApplyAsync(CurrentUserId, items, ct)));
    }

    /// <summary>Ngày dự kiến đạt mục tiêu tiết kiệm (trung bình và thận trọng), kịp hạn chưa, cần góp bao nhiêu. Thử mức góp khác qua query, không lưu.</summary>
    [HttpGet("goals/{goalId:guid}")]
    public async Task<IActionResult> Goal(Guid goalId, [FromQuery] long? monthlyContributionCents, CancellationToken ct)
        => Ok(ApiResponse<GoalForecastDto>.Ok(await _goals.GetAsync(CurrentUserId, goalId, monthlyContributionCents, ct)));

    private const string AllAnomalies = "all";
}

public record AnomalyFeedbackRequest(string? Resolution);

public record CategoryBudgetItemRequest(Guid CategoryId, long LimitCents);

public record ApplyCategoryBudgetsRequest(IReadOnlyList<CategoryBudgetItemRequest>? Items);

public record AlertFeedbackRequest(string? Feedback);

public record ScenarioItemRequest(DateOnly Date, long AmountCents, RecurringDirection Direction, string? Label);

/// <param name="AmountCents">Giá bằng đồng VND (không chia 100).</param>
/// <param name="HorizonDays">Chỉ dùng khi chưa biết kỳ thu nhập kế tiếp: đánh giá tới chừng này ngày sau ngày mua.</param>
/// <param name="CategorySlug">Slug danh mục hệ thống (như chat trả về) khi app chưa có <c>CategoryId</c>.</param>
public record PurchaseCheckRequest(
    long AmountCents, DateOnly? PurchaseDate, string? Item, Guid? CategoryId, Guid? AccountId, int? HorizonDays, string? CategorySlug = null);

public record CashflowScenarioRequest(Guid? AccountId, DateOnly? To, IReadOnlyList<ScenarioItemRequest>? Items);
