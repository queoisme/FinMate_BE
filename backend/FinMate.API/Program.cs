using FinMate.API.Middleware;
using FinMate.Application.Auth.Commands;
using FinMate.Application.Auth.Queries;
using FinMate.Application.Budgets;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Budgets.Queries;
using FinMate.Application.Categories.Commands;
using FinMate.Application.Categories.Queries;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Application.FinancialAccounts.Queries;
using FinMate.Application.Notifications.Commands;
using FinMate.Application.Reports;
using FinMate.Application.Reports.Commands;
using FinMate.Application.Reports.Queries;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Application.SavingGoals.Queries;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Queries;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Caching;
using FinMate.Infrastructure.ExternalServices;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Refit;
using Serilog;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;

namespace FinMate.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, _, configuration) => configuration
            .MinimumLevel.Is(context.HostingEnvironment.IsDevelopment() ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter())
            .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(), "logs/finmate-api-.json",
                rollingInterval: RollingInterval.Day));

        var databaseUrl = builder.Configuration["DATABASE_URL"]
            ?? throw new InvalidOperationException("DATABASE_URL is not configured.");
        var redisUrl = builder.Configuration["REDIS_URL"]
            ?? throw new InvalidOperationException("REDIS_URL is not configured.");
        var jwtSecret = builder.Configuration["JWT_SECRET"]
            ?? throw new InvalidOperationException("JWT_SECRET is not configured.");
        if (jwtSecret.Length < 64)
        {
            throw new InvalidOperationException("JWT_SECRET must be at least 64 characters long.");
        }
        _ = builder.Configuration["GOOGLE_CLIENT_ID"]
            ?? throw new InvalidOperationException("GOOGLE_CLIENT_ID is not configured.");
        var aiServiceUrl = builder.Configuration["AI_SERVICE_URL"]
            ?? throw new InvalidOperationException("AI_SERVICE_URL is not configured.");
        var aiServiceApiKey = builder.Configuration["AI_SERVICE_API_KEY"]
            ?? throw new InvalidOperationException("AI_SERVICE_API_KEY is not configured.");

        builder.Services.AddControllers()
            .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(options =>
        {
            options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Description = "JWT Authorization header using the Bearer scheme.",
            });
            options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
            {
                {
                    new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                    {
                        Reference = new Microsoft.OpenApi.Models.OpenApiReference
                        {
                            Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                            Id = "Bearer",
                        },
                    },
                    Array.Empty<string>()
                },
            });
        });

        builder.Services.AddDbContext<FinMateDbContext>(options =>
            options.UseNpgsql(databaseUrl));

        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisUrl;
        });

        builder.Services.AddValidatorsFromAssemblyContaining<Application.Common.Exceptions.FinMateException>();
        builder.Services.AddFluentValidationAutoValidation();

        builder.Services.AddAutoMapper(cfg => { }, typeof(Application.Common.Exceptions.FinMateException).Assembly);

        builder.Services.AddHangfire(config => config
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(databaseUrl)));
        builder.Services.AddHangfireServer();

        builder.Services.AddRefitClient<IAIServiceApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(aiServiceUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiServiceApiKey);
            });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };
            });

        // Every controller requires auth by default (AGENTS.md §3.2) — Auth's public
        // endpoints opt out individually with [AllowAnonymous].
        builder.Services.AddAuthorization(options =>
        {
            options.FallbackPolicy = options.DefaultPolicy;
        });

        builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<IDataDeletionRequestRepository, DataDeletionRequestRepository>();
        builder.Services.AddScoped<IUserHardDeleter, UserHardDeleter>();
        builder.Services.AddScoped<ICacheService, RedisCacheService>();
        builder.Services.AddScoped<IFinancialAccountRepository, FinancialAccountRepository>();
        builder.Services.AddScoped<IProviderConfigRepository, ProviderConfigRepository>();
        builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
        builder.Services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
        builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
        builder.Services.AddScoped<IBudgetRepository, BudgetRepository>();
        builder.Services.AddScoped<IBudgetPeriodService, BudgetPeriodService>();
        builder.Services.AddScoped<ISavingGoalRepository, SavingGoalRepository>();
        builder.Services.AddScoped<IReportRepository, ReportRepository>();
        builder.Services.AddScoped<ISpendingForecaster, StatisticalSpendingForecaster>();
        builder.Services.AddScoped<IAIServiceClient, AIServiceClient>();
        builder.Services.AddScoped<IPushNotificationService, LoggingPushNotificationService>();
        builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();

        builder.Services.AddScoped<IRegisterCommandHandler, RegisterCommandHandler>();
        builder.Services.AddScoped<ILoginCommandHandler, LoginCommandHandler>();
        builder.Services.AddScoped<IGoogleLoginCommandHandler, GoogleLoginCommandHandler>();
        builder.Services.AddScoped<IRefreshTokenCommandHandler, RefreshTokenCommandHandler>();
        builder.Services.AddScoped<ILogoutCommandHandler, LogoutCommandHandler>();
        builder.Services.AddScoped<ILogoutAllDevicesCommandHandler, LogoutAllDevicesCommandHandler>();
        builder.Services.AddScoped<IChangePasswordCommandHandler, ChangePasswordCommandHandler>();
        builder.Services.AddScoped<IDeleteAccountCommandHandler, DeleteAccountCommandHandler>();
        builder.Services.AddScoped<IGetUserProfileQueryHandler, GetUserProfileQueryHandler>();
        builder.Services.AddScoped<IUpdateUserProfileCommandHandler, UpdateUserProfileCommandHandler>();
        builder.Services.AddScoped<IUpdateNotificationPrefsCommandHandler, UpdateNotificationPrefsCommandHandler>();

        builder.Services.AddScoped<ICreateFinancialAccountCommandHandler, CreateFinancialAccountCommandHandler>();
        builder.Services.AddScoped<IUpdateFinancialAccountCommandHandler, UpdateFinancialAccountCommandHandler>();
        builder.Services.AddScoped<IToggleAccountMonitoringCommandHandler, ToggleAccountMonitoringCommandHandler>();
        builder.Services.AddScoped<IDeleteFinancialAccountCommandHandler, DeleteFinancialAccountCommandHandler>();
        builder.Services.AddScoped<IGetAccountListQueryHandler, GetAccountListQueryHandler>();
        builder.Services.AddScoped<IGetAccountBalanceQueryHandler, GetAccountBalanceQueryHandler>();

        builder.Services.AddScoped<ICreateCategoryCommandHandler, CreateCategoryCommandHandler>();
        builder.Services.AddScoped<IUpdateCategoryCommandHandler, UpdateCategoryCommandHandler>();
        builder.Services.AddScoped<IDeleteCategoryCommandHandler, DeleteCategoryCommandHandler>();
        builder.Services.AddScoped<IGetCategoryListQueryHandler, GetCategoryListQueryHandler>();

        builder.Services.AddScoped<IAnalyzeNotificationCommandHandler, AnalyzeNotificationCommandHandler>();

        builder.Services.AddScoped<IConfirmTransactionCommandHandler, ConfirmTransactionCommandHandler>();
        builder.Services.AddScoped<ICreateManualTransactionCommandHandler, CreateManualTransactionCommandHandler>();
        builder.Services.AddScoped<IUpdateTransactionCommandHandler, UpdateTransactionCommandHandler>();
        builder.Services.AddScoped<IDeleteTransactionCommandHandler, DeleteTransactionCommandHandler>();
        builder.Services.AddScoped<IParseNaturalLanguageCommandHandler, ParseNaturalLanguageCommandHandler>();
        builder.Services.AddScoped<IGetTransactionListQueryHandler, GetTransactionListQueryHandler>();
        builder.Services.AddScoped<IGetTransactionDetailQueryHandler, GetTransactionDetailQueryHandler>();

        builder.Services.AddScoped<ICreateBudgetCommandHandler, CreateBudgetCommandHandler>();
        builder.Services.AddScoped<IUpdateBudgetLimitCommandHandler, UpdateBudgetLimitCommandHandler>();
        builder.Services.AddScoped<IDeleteBudgetCommandHandler, DeleteBudgetCommandHandler>();
        builder.Services.AddScoped<IGetBudgetSummaryQueryHandler, GetBudgetSummaryQueryHandler>();

        builder.Services.AddScoped<ICreateSavingGoalCommandHandler, CreateSavingGoalCommandHandler>();
        builder.Services.AddScoped<IUpdateSavingGoalCommandHandler, UpdateSavingGoalCommandHandler>();
        builder.Services.AddScoped<IContributeToGoalCommandHandler, ContributeToGoalCommandHandler>();
        builder.Services.AddScoped<ICancelSavingGoalCommandHandler, CancelSavingGoalCommandHandler>();
        builder.Services.AddScoped<IGetSavingGoalListQueryHandler, GetSavingGoalListQueryHandler>();
        builder.Services.AddScoped<IGetGoalProgressQueryHandler, GetGoalProgressQueryHandler>();

        builder.Services.AddScoped<IGetMonthlySummaryQueryHandler, GetMonthlySummaryQueryHandler>();
        builder.Services.AddScoped<IGetCategoryBreakdownQueryHandler, GetCategoryBreakdownQueryHandler>();
        builder.Services.AddScoped<IGetTransactionTimelineQueryHandler, GetTransactionTimelineQueryHandler>();
        builder.Services.AddScoped<IGetSpendingForecastQueryHandler, GetSpendingForecastQueryHandler>();
        builder.Services.AddScoped<IGetSpendingInsightsQueryHandler, GetSpendingInsightsQueryHandler>();
        builder.Services.AddScoped<IMarkInsightReadCommandHandler, MarkInsightReadCommandHandler>();

        builder.Services.AddScoped<DataDeletionJob>();
        builder.Services.AddScoped<RetryFailedNotificationJob>();
        builder.Services.AddScoped<BudgetAlertJob>();
        builder.Services.AddScoped<DailySummaryJob>();
        builder.Services.AddScoped<InsightGeneratorJob>();
        builder.Services.AddScoped<GoalDeadlineCheckJob>();

        builder.Services.AddFinMateRateLimiting();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            db.Database.Migrate();

            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            FinMate.Infrastructure.Persistence.Seed.AdminUserSeeder.SeedAsync(db, passwordHasher, builder.Configuration).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.ProviderConfigSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.CategorySeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.MissionSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.MascotItemSeeder.SeedAsync(db).GetAwaiter().GetResult();
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();
        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        var hangfireUser = builder.Configuration["HANGFIRE_DASHBOARD_USER"];
        var hangfirePass = builder.Configuration["HANGFIRE_DASHBOARD_PASS"];
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = new[]
            {
                new HangfireBasicAuthFilter(hangfireUser, hangfirePass),
            },
        });

        RecurringJob.AddOrUpdate<DataDeletionJob>(
            "data-deletion",
            job => job.RunAsync(CancellationToken.None),
            "0 3 * * *");

        RecurringJob.AddOrUpdate<RetryFailedNotificationJob>(
            "retry-failed-notifications",
            job => job.RunAsync(CancellationToken.None),
            "*/15 * * * *");

        RecurringJob.AddOrUpdate<BudgetAlertJob>(
            "budget-alerts",
            job => job.RunAsync(CancellationToken.None),
            "0 * * * *");

        // ARCHITECTURE.md §5 không quy định giờ cho job này — 08:00 là khung giờ hợp lý để
        // gửi nhắc nhở, không trùng với các job nặng chạy đêm.
        RecurringJob.AddOrUpdate<GoalDeadlineCheckJob>(
            "goal-deadline-check",
            job => job.RunAsync(CancellationToken.None),
            "0 8 * * *");

        // Hangfire đọc cron theo UTC. ARCHITECTURE.md §5 ghi giờ theo ý định vận hành (giờ VN)
        // nên hai job dưới phải trừ 7 tiếng: 17:05 UTC = 00:05 VN, 19:00 UTC = 02:00 VN.
        RecurringJob.AddOrUpdate<DailySummaryJob>(
            "daily-summary",
            job => job.RunAsync(CancellationToken.None),
            "5 17 * * *");

        RecurringJob.AddOrUpdate<InsightGeneratorJob>(
            "insight-generator",
            job => job.RunAsync(CancellationToken.None),
            "0 19 * * *");

        app.MapControllers();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

        app.Run();
    }
}
