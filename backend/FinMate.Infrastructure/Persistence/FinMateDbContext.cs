using FinMate.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinMate.Infrastructure.Persistence;

public class FinMateDbContext : DbContext
{
    public FinMateDbContext(DbContextOptions<FinMateDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<ReceiptScan> ReceiptScans => Set<ReceiptScan>();
    public DbSet<ReceiptScanItem> ReceiptScanItems => Set<ReceiptScanItem>();
    public DbSet<ReceiptScanLink> ReceiptScanLinks => Set<ReceiptScanLink>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataDeletionRequest> DataDeletionRequests => Set<DataDeletionRequest>();
    public DbSet<ProviderConfig> ProviderConfigs => Set<ProviderConfig>();
    public DbSet<SystemConfigVersion> SystemConfigVersions => Set<SystemConfigVersion>();
    public DbSet<SecretEntry> SecretEntries => Set<SecretEntry>();
    public DbSet<VietQrBank> VietQrBanks => Set<VietQrBank>();
    public DbSet<QrTransferIntent> QrTransferIntents => Set<QrTransferIntent>();
    public DbSet<RecurringItem> RecurringItems => Set<RecurringItem>();
    public DbSet<ForecastAlert> ForecastAlerts => Set<ForecastAlert>();
    public DbSet<TransactionAnomaly> TransactionAnomalies => Set<TransactionAnomaly>();
    public DbSet<CounterpartyCategoryOverride> CounterpartyCategoryOverrides => Set<CounterpartyCategoryOverride>();
    public DbSet<FinancialAccount> FinancialAccounts => Set<FinancialAccount>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<SavingGoal> SavingGoals => Set<SavingGoal>();
    public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetPeriod> BudgetPeriods => Set<BudgetPeriod>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<SpendingInsight> SpendingInsights => Set<SpendingInsight>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<AiResult> AiResults => Set<AiResult>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<FinanceMethod> FinanceMethods => Set<FinanceMethod>();
    public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
    public DbSet<SurveyOption> SurveyOptions => Set<SurveyOption>();
    public DbSet<GoalTemplate> GoalTemplates => Set<GoalTemplate>();
    public DbSet<MethodGoalTemplate> MethodGoalTemplates => Set<MethodGoalTemplate>();
    public DbSet<UserSurveyAnswer> UserSurveyAnswers => Set<UserSurveyAnswer>();
    public DbSet<MethodRecommendation> MethodRecommendations => Set<MethodRecommendation>();
    public DbSet<UserMethod> UserMethods => Set<UserMethod>();
    public DbSet<EconomicAssumption> EconomicAssumptions => Set<EconomicAssumption>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupInvitation> GroupInvitations => Set<GroupInvitation>();
    public DbSet<GroupActivity> GroupActivities => Set<GroupActivity>();
    public DbSet<GroupCategory> GroupCategories => Set<GroupCategory>();
    public DbSet<GroupShareRule> GroupShareRules => Set<GroupShareRule>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportTicketMessage> SupportTicketMessages => Set<SupportTicketMessage>();
    public DbSet<SupportTicketAttachment> SupportTicketAttachments => Set<SupportTicketAttachment>();
    public DbSet<SupportAccessGrant> SupportAccessGrants => Set<SupportAccessGrant>();
    public DbSet<SupportDataAccessLog> SupportDataAccessLogs => Set<SupportDataAccessLog>();
    public DbSet<OpsApprovalRequest> OpsApprovalRequests => Set<OpsApprovalRequest>();
    public DbSet<CannedReply> CannedReplies => Set<CannedReply>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<OpsIncident> OpsIncidents => Set<OpsIncident>();
    public DbSet<OpsServiceStatus> OpsServiceStatuses => Set<OpsServiceStatus>();
    public DbSet<OpsErrorEvent> OpsErrorEvents => Set<OpsErrorEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinMateDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Quy mọi <see cref="DateTimeOffset"/> về UTC trước khi ghi xuống <c>timestamptz</c>.
    ///
    /// Npgsql TỪ CHỐI ghi <see cref="DateTimeOffset"/> có offset khác 0 vào cột
    /// <c>timestamp with time zone</c> — nó ném <see cref="ArgumentException"/> chứ không
    /// tự quy đổi. Client Android chạy ở Việt Nam gửi mốc thời gian kèm offset
    /// <c>+07:00</c>, nên nếu không chuẩn hoá ở đây thì MỌI endpoint nhận
    /// <c>DateTimeOffset</c> từ client (<c>/notifications/analyze</c>,
    /// <c>POST|PUT /transactions</c>, <c>/transactions/transfer</c>, deadline của
    /// saving goal…) đều trả 500.
    ///
    /// Chuẩn hoá ở tầng DbContext thay vì ở từng handler: đây là ràng buộc của tầng lưu
    /// trữ, và bỏ sót một handler mới sẽ tái hiện đúng lỗi này. Phép đổi giữ nguyên thời
    /// điểm nên không ảnh hưởng so sánh hay sắp xếp; chỉ đổi cách biểu diễn.
    /// (Cùng gốc với quyết định "mốc chu kỳ luôn trả về ở offset 0" ghi tại TASKS.md
    /// Phase 5.)
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcDateTimeOffsetConverter>();
        base.ConfigureConventions(configurationBuilder);
    }
}
