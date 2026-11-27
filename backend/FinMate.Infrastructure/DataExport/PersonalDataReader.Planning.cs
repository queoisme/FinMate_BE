using FinMate.Application.DataExport.Package;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.DataExport;

public sealed partial class PersonalDataReader
{
    private async Task<IEnumerable<PackageSection>> PlanningSectionsAsync(Guid userId, CancellationToken ct) =>
    [
        new PackageSection("budgets.json", await BudgetsAsync(userId, ct), IsList: true),
        new PackageSection("goals.json", await GoalsAsync(userId, ct), IsList: false),
        new PackageSection("recurring_items.json", await RecurringItemsAsync(userId, ct), IsList: true),
        new PackageSection("recurring_item_payments.json", await RecurringItemPaymentsAsync(userId, ct), IsList: true),
        new PackageSection("loans.json", await LoansAsync(userId, ct), IsList: true),
        new PackageSection("finance_methods.json", await FinanceMethodsAsync(userId, ct), IsList: false),
        new PackageSection("insights.json", await InsightsAsync(userId, ct), IsList: true),
        new PackageSection("forecasts.json", await ForecastsAsync(userId, ct), IsList: false),
    ];

    /// <summary>Chỉ ngân sách cá nhân (<c>user_id</c> = user); ngân sách nhóm là của nhóm.</summary>
    private async Task<object> BudgetsAsync(Guid userId, CancellationToken ct)
    {
        var budgets = await db.Budgets.IgnoreQueryFilters().AsNoTracking().Where(b => b.UserId == userId).OrderBy(b => b.CreatedAt)
            .Select(b => new { b.Id, b.CategoryId, CategoryName = b.Category == null ? null : b.Category.Name, b.LimitCents, b.PeriodType, b.CreatedAt, b.UpdatedAt, b.DeletedAt })
            .ToListAsync(ct);
        var ids = budgets.Select(b => b.Id).ToList();
        var periods = (await db.BudgetPeriods.AsNoTracking().Where(p => ids.Contains(p.BudgetId)).OrderBy(p => p.PeriodStart)
            .Select(p => new { p.BudgetId, p.PeriodStart, p.PeriodEnd, p.LimitCents, p.SpentCents }).ToListAsync(ct)).ToLookup(p => p.BudgetId);
        return budgets.Select(b => new
        {
            id = b.Id, categoryId = b.CategoryId, categoryName = b.CategoryName, limitCents = b.LimitCents, periodType = b.PeriodType,
            createdAt = b.CreatedAt, updatedAt = b.UpdatedAt, deletedAt = b.DeletedAt,
            periods = periods[b.Id].Select(p => new { periodStart = p.PeriodStart, periodEnd = p.PeriodEnd, limitCents = p.LimitCents, spentCents = p.SpentCents }),
        }).ToList();
    }

    /// <summary>Mục tiêu cá nhân đầy đủ; khoản góp của chính user kể cả vào mục tiêu nhóm (chỉ id mục tiêu, không tên nhóm đặt).</summary>
    private async Task<object> GoalsAsync(Guid userId, CancellationToken ct) => new
    {
        goals = await db.SavingGoals.IgnoreQueryFilters().AsNoTracking().Where(g => g.UserId == userId).OrderBy(g => g.CreatedAt)
            .Select(g => new
            {
                id = g.Id, name = g.Name, targetCents = g.TargetCents, savedCents = g.SavedCents, initialSavedCents = g.InitialSavedCents, icon = g.Icon,
                contributionFrequency = g.ContributionFrequency, status = g.Status, deadline = g.Deadline, completedAt = g.CompletedAt,
                createdAt = g.CreatedAt, updatedAt = g.UpdatedAt, deletedAt = g.DeletedAt,
            }).ToListAsync(ct),
        contributions = await (from c in db.GoalContributions
                               join g in db.SavingGoals.IgnoreQueryFilters() on c.SavingGoalId equals g.Id
                               where c.UserId == userId
                               orderby c.ContributedAt
                               select new
                               {
                                   goalId = g.Id, isGroupGoal = g.GroupId != null, amountCents = c.AmountCents, note = c.Note,
                                   contributedAt = c.ContributedAt, createdAt = c.CreatedAt,
                               }).AsNoTracking().ToListAsync(ct),
    };

    private async Task<object> RecurringItemsAsync(Guid userId, CancellationToken ct)
        => await db.RecurringItems.IgnoreQueryFilters().AsNoTracking().Where(r => r.UserId == userId).OrderBy(r => r.CreatedAt)
            .Select(r => new
            {
                id = r.Id, direction = r.Direction, name = r.Name, categoryId = r.CategoryId, financialAccountId = r.FinancialAccountId,
                amountCents = r.AmountCents, cadence = r.Cadence, nextDueDate = r.NextDueDate, status = r.Status, source = r.Source,
                occurrences = r.Occurrences, lastSeenAt = r.LastSeenAt, lastSeenAmountCents = r.LastSeenAmountCents,
                totalOccurrences = r.TotalOccurrences, endDate = r.EndDate, anchorDay = r.AnchorDay, prepaidOccurrences = r.PrepaidOccurrences,
                completedAt = r.CompletedAt, settledEarly = r.SettledEarly,
                createdAt = r.CreatedAt, updatedAt = r.UpdatedAt, deletedAt = r.DeletedAt,
            }).ToListAsync(ct);

    /// <summary>Phase 92 (D15): các kỳ đã trả của khoản định kỳ, kể cả đã gỡ; chỉ id giao dịch, chi tiết giao dịch nằm ở transactions.csv.</summary>
    private async Task<object> RecurringItemPaymentsAsync(Guid userId, CancellationToken ct)
        => await db.RecurringItemPayments.IgnoreQueryFilters().AsNoTracking().Where(p => p.UserId == userId).OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                id = p.Id, recurringItemId = p.RecurringItemId, transactionId = p.TransactionId, dueDate = p.DueDate, kind = p.Kind,
                createdAt = p.CreatedAt, deletedAt = p.DeletedAt,
            }).ToListAsync(ct);

    private async Task<object> LoansAsync(Guid userId, CancellationToken ct)
    {
        var loans = await db.Loans.AsNoTracking().Where(l => l.UserId == userId).OrderBy(l => l.CreatedAt).ToListAsync(ct);
        var ids = loans.Select(l => l.Id).ToList();
        var entries = (await db.LoanRepayments.AsNoTracking().Where(r => ids.Contains(r.LoanId)).OrderBy(r => r.PaidAt).ToListAsync(ct)).ToLookup(r => r.LoanId);
        return loans.Select(l => new
        {
            id = l.Id, direction = l.Direction, title = l.Title, counterparty = l.Counterparty, principalCents = l.PrincipalCents,
            repaidCents = l.RepaidCents, interestRatePercentYear = l.InterestRatePercentYear, startDate = l.StartDate, dueDate = l.DueDate, status = l.Status,
            note = l.Note, settledAt = l.SettledAt, writtenOffAt = l.WrittenOffAt, createdAt = l.CreatedAt, updatedAt = l.UpdatedAt, deletedAt = l.DeletedAt,
            entries = entries[l.Id].Select(r => new
            {
                kind = r.Kind, amountCents = r.AmountCents, paidAt = r.PaidAt, transactionId = r.TransactionId,
                interestTransactionId = r.InterestTransactionId, createdAt = r.CreatedAt, deletedAt = r.DeletedAt,
            }),
        }).ToList();
    }

    private async Task<object> FinanceMethodsAsync(Guid userId, CancellationToken ct) => new
    {
        methods = await (from m in db.UserMethods
                         join f in db.FinanceMethods on m.MethodId equals f.Id
                         where m.UserId == userId
                         orderby m.StartedAt
                         select new
                         {
                             methodCode = f.Code, methodName = f.Name, methodVersion = m.MethodVersion, role = m.Role, @params = m.Params,
                             chosenVia = m.ChosenVia, status = m.Status, startedAt = m.StartedAt, endedAt = m.EndedAt,
                         }).AsNoTracking().ToListAsync(ct),
        recommendations = (await (from r in db.MethodRecommendations
                                  join p in db.FinanceMethods on r.PrimaryMethodId equals p.Id
                                  where r.UserId == userId
                                  orderby r.CreatedAt
                                  select new
                                  {
                                      PrimaryCode = p.Code,
                                      SupportCode = db.FinanceMethods.Where(s => s.Id == r.SupportMethodId).Select(s => s.Code).FirstOrDefault(),
                                      r.Reasons, r.SignalsSnapshot, r.Source, r.Outcome, r.DecidedAt, r.CreatedAt,
                                  }).AsNoTracking().ToListAsync(ct))
            .Select(r => new
            {
                primaryMethodCode = r.PrimaryCode, supportMethodCode = r.SupportCode, reasons = ParseJson(r.Reasons), signalsSnapshot = ParseJson(r.SignalsSnapshot),
                source = r.Source, outcome = r.Outcome, decidedAt = r.DecidedAt, createdAt = r.CreatedAt,
            }).ToList(),
        surveyAnswers = await (from a in db.UserSurveyAnswers
                               where a.UserId == userId
                               orderby a.AnsweredAt
                               select new
                               {
                                   questionCode = a.QuestionCode,
                                   questionText = db.SurveyQuestions.Where(q => q.Code == a.QuestionCode).Select(q => q.Text).FirstOrDefault(),
                                   optionCode = a.OptionCode,
                                   optionText = db.SurveyOptions.Where(o => o.Code == a.OptionCode && db.SurveyQuestions.Any(q => q.Id == o.QuestionId && q.Code == a.QuestionCode))
                                       .Select(o => o.Text).FirstOrDefault(),
                                   answeredAt = a.AnsweredAt, updatedAt = a.UpdatedAt,
                               }).AsNoTracking().ToListAsync(ct),
    };

    private async Task<object> InsightsAsync(Guid userId, CancellationToken ct)
        => await db.SpendingInsights.IgnoreQueryFilters().AsNoTracking().Where(i => i.UserId == userId).OrderBy(i => i.CreatedAt)
            .Select(i => new
            {
                insightType = i.InsightType, title = i.Title, body = i.Body, categoryId = i.CategoryId, amountCents = i.AmountCents,
                periodStart = i.PeriodStart, periodEnd = i.PeriodEnd, isRead = i.IsRead, createdAt = i.CreatedAt, deletedAt = i.DeletedAt,
            }).ToListAsync(ct);

    /// <summary>Phản hồi: giữ lý do và ghi chú user gửi; bỏ nhãn của nhân viên (<c>cause</c>, người gắn nhãn).</summary>
    private async Task<object> ForecastsAsync(Guid userId, CancellationToken ct) => new
    {
        snapshots = await db.ForecastSnapshots.AsNoTracking().Where(s => s.UserId == userId).OrderBy(s => s.OriginDate)
            .Select(s => new
            {
                id = s.Id, originDate = s.OriginDate, horizonDays = s.HorizonDays, horizonDate = s.HorizonDate, startBalanceCents = s.StartBalanceCents,
                predictedCents = s.PredictedCents, conservativeCents = s.ConservativeCents, optimisticCents = s.OptimisticCents,
                recurringOnlyCents = s.RecurringOnlyCents, confidence = s.Confidence, actualCents = s.ActualCents, createdAt = s.CreatedAt,
            }).ToListAsync(ct),
        feedback = await db.ForecastFeedback.AsNoTracking().Where(f => f.UserId == userId).OrderBy(f => f.CreatedAt)
            .Select(f => new { snapshotId = f.SnapshotId, reason = f.Reason, note = f.Note, createdAt = f.CreatedAt }).ToListAsync(ct),
    };
}
