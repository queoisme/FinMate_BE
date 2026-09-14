using FinMate.Domain.Entities;
using FinMate.Domain.Entities.Gamification;
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
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DataDeletionRequest> DataDeletionRequests => Set<DataDeletionRequest>();
    public DbSet<ProviderConfig> ProviderConfigs => Set<ProviderConfig>();
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
    public DbSet<UserGamification> UserGamifications => Set<UserGamification>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<UserMission> UserMissions => Set<UserMission>();
    public DbSet<MascotItem> MascotItems => Set<MascotItem>();
    public DbSet<UserMascotItem> UserMascotItems => Set<UserMascotItem>();

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
