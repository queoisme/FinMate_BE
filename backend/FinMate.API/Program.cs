using FinMate.API.Authorization;
using FinMate.API.Configuration;
using FinMate.API.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using FinMate.API.Middleware;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.FinanceCatalog;
using FinMate.Application.Admin.Queries;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Auth;
using FinMate.Application.Auth.Commands;
using FinMate.Application.Auth.GoogleOAuth;
using FinMate.Application.Auth.Otp;
using FinMate.Application.Auth.Queries;
using FinMate.Application.Budgets;
using FinMate.Application.Budgets.Commands;
using FinMate.Application.Budgets.Queries;
using FinMate.Application.Categories.Commands;
using FinMate.Application.Categories.Queries;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Devices.Commands;
using FinMate.Application.FinanceMethods;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Application.FinancialAccounts.Queries;
using FinMate.Application.Gamification;
using FinMate.Application.Gamification.Commands;
using FinMate.Application.Gamification.Queries;
using FinMate.Application.Notifications.Commands;
using FinMate.Application.QrTransfers;
using FinMate.Application.Reports;
using FinMate.Application.Reports.Commands;
using FinMate.Application.Reports.Queries;
using FinMate.Application.SavingGoals.Commands;
using FinMate.Application.SavingGoals.Queries;
using FinMate.Application.Transactions;
using FinMate.Application.Transactions.Commands;
using FinMate.Application.Transactions.Queries;
using FinMate.Domain.Enums;
using FinMate.Infrastructure.BackgroundJobs;
using FinMate.Infrastructure.Caching;
using FinMate.Infrastructure.ExternalServices;
using FinMate.Infrastructure.Persistence;
using FinMate.Infrastructure.Persistence.Repositories;
using FinMate.Infrastructure.Security;
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

        builder.Host.UseSerilog((context, _, configuration) =>
        {
            configuration
                .MinimumLevel.Is(context.HostingEnvironment.IsDevelopment()
                    ? Serilog.Events.LogEventLevel.Debug
                    : Serilog.Events.LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());

            // Sink file CHỈ ở Development. Trong container thì ghi log vào ổ đĩa riêng của nó
            // là không ai đọc được và đầy dần cho tới khi hết chỗ; production thu log từ
            // stdout. Còn một lý do cụ thể nữa: image chạy bằng user không đặc quyền và /app
            // không cho user đó ghi.
            if (context.HostingEnvironment.IsDevelopment())
            {
                configuration.WriteTo.File(
                    new Serilog.Formatting.Json.JsonFormatter(),
                    "logs/finmate-api-.json",
                    rollingInterval: RollingInterval.Day);
            }
        });

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

        // Ngoài Development, từ chối khởi động nếu còn bí mật mẫu — xem StartupSecretGuard.
        if (!builder.Environment.IsDevelopment())
        {
            StartupSecretGuard.ThrowIfPlaceholdersRemain(
                builder.Configuration, builder.Environment.EnvironmentName);
        }

        // Luồng OAuth qua trình duyệt là TUỲ CHỌN và TÁCH RIÊNG khỏi POST /auth/google:
        // đường native chỉ xác minh token có sẵn nên chỉ cần CLIENT_ID, còn đường này phải tự
        // đổi code nên cần thêm secret. Thiếu secret thì chỉ ba endpoint mới tắt, đường cũ
        // vẫn chạy bình thường.
        var googleClientSecret = builder.Configuration["GOOGLE_CLIENT_SECRET"];
        var googleRedirectUri = builder.Configuration["GOOGLE_REDIRECT_URI"];
        var googleOAuthEnabled = !string.IsNullOrWhiteSpace(googleClientSecret)
            && !string.IsNullOrWhiteSpace(googleRedirectUri);

        // Email, Google OAuth và FCM là TUỲ CHỌN: key của chúng đọc TẠI LÚC DÙNG qua ISecretVault (key admin nhập trên trang quản trị trước, biến môi trường sau),
        // nên không còn chốt bật/tắt một lần lúc khởi động và đổi key không cần khởi động lại.

        // Mặc định BẬT. AuthApiFactory tắt đi — xem AuthOptions.
        var requireEmailVerification =
            !bool.TryParse(builder.Configuration["REQUIRE_EMAIL_VERIFICATION"], out var parsed) || parsed;

        // Mặc định TRỐNG = không bật forwarded headers — xem TrustedProxies.
        var trustedProxies = builder.Configuration["TRUSTED_PROXIES"];

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

        // OCR service riêng (PaddleOCR + VietOCR): có URL thì backend gọi nó rồi mới gọi AI Service phân loại; không có thì quét qua
        // POST /api/v1/ocr của AI Service như trước. Khóa nội bộ bắt buộc đi kèm URL.
        var ocrServiceUrl = builder.Configuration["OCR_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(ocrServiceUrl))
        {
            builder.Services.AddSingleton<IOcrServiceClient, DisabledOcrServiceClient>();
        }
        else
        {
            var ocrServiceApiKey = builder.Configuration["OCR_SERVICE_API_KEY"]
                ?? throw new InvalidOperationException("OCR_SERVICE_API_KEY is not configured.");
            builder.Services.AddHttpClient<IOcrServiceClient, OcrServiceClient>(client =>
            {
                client.BaseAddress = new Uri(ocrServiceUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ocrServiceApiKey);
                client.Timeout = TimeSpan.FromSeconds(60);
            });
        }

        // STT service riêng (GIPFormer): có URL thì POST /transactions/voice nghe được; không có thì endpoint trả 503.
        var sttServiceUrl = builder.Configuration["STT_SERVICE_URL"];
        if (string.IsNullOrWhiteSpace(sttServiceUrl))
        {
            builder.Services.AddSingleton<ISttServiceClient, DisabledSttServiceClient>();
        }
        else
        {
            var sttServiceApiKey = builder.Configuration["STT_SERVICE_API_KEY"]
                ?? throw new InvalidOperationException("STT_SERVICE_API_KEY is not configured.");
            builder.Services.AddHttpClient<ISttServiceClient, SttServiceClient>(client =>
            {
                client.BaseAddress = new Uri(sttServiceUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sttServiceApiKey);
                client.Timeout = TimeSpan.FromSeconds(60);
            });
        }

        builder.Services.AddRefitClient<IAIServiceApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(aiServiceUrl);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiServiceApiKey);
            });

        // Quản trị luật phân loại của AI: chuyển tiếp nguyên văn tới /api/v1/admin/rules của AI Service (cùng khóa nội bộ).
        builder.Services.AddHttpClient<IAiRulesAdminClient, AiRulesAdminClient>(client =>
        {
            client.BaseAddress = new Uri(aiServiceUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiServiceApiKey);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // Chọn cách quản lý tài chính qua AI (docs/ai-finance-method-recommend-api.md). Timeout 30 giây = trần cả route của ai-service (24 giây: Gemini tối đa 14 giây rồi tới OpenRouter) cộng vài giây dư;
        // quá hạn hay lỗi thì MethodRecommendationService dùng rule. Thường chỉ mất vài giây; 30 giây là trường hợp xấu nhất khi cả hai nhà cung cấp đều chậm.
        builder.Services.AddHttpClient<IMethodAiClient, MethodAiClient>(client =>
        {
            client.BaseAddress = new Uri(aiServiceUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiServiceApiKey);
            client.Timeout = TimeSpan.FromSeconds(30);
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

            // Vai trò được đọc từ claim trong access token, không tra lại DB mỗi request —
            // xem ghi chú về cửa sổ 15 phút ở AuthorizationPolicies.
            options.AddPolicy(
                AuthorizationPolicies.AdminOnly,
                policy => policy.RequireRole(nameof(UserRole.Admin)));
        });

        builder.Services.AddSingleton(new AuthOptions(requireEmailVerification));

        builder.Services.AddSingleton(new GoogleOAuthOptions(
            googleOAuthEnabled,
            builder.Configuration["GOOGLE_CLIENT_ID"]!,
            googleRedirectUri ?? "",
            builder.Configuration["MOBILE_DEEP_LINK"]));

        builder.Services.AddScoped<IGoogleOAuthStore, GoogleOAuthStore>();

        // Key bên thứ ba của backend: khóa chủ ở SECRETS_MASTER_KEY (khác khóa chủ của AI Service). Không có khóa thì không lưu được key vào DB, key ở biến môi trường vẫn chạy.
        builder.Services.AddSingleton(new SecretCipher(builder.Configuration["SECRETS_MASTER_KEY"]));
        builder.Services.AddSingleton<ISecretVault, SecretVault>();
        builder.Services.AddSingleton<FcmApp>();
        builder.Services.AddScoped<ISecretTester, BackendSecretTester>();
        builder.Services.AddHttpClient(SwitchingEmailSender.HttpClientName, client => client.BaseAddress = new Uri("https://api.brevo.com/"));
        builder.Services.AddHttpClient("google-oauth");
        builder.Services.AddScoped<IGoogleCodeExchanger, VaultGoogleCodeExchanger>();
        builder.Services.AddScoped<IOtpService, OtpService>();

        builder.Services.AddScoped<LoggingEmailSender>();
        builder.Services.AddScoped<IEmailSender, SwitchingEmailSender>();

        builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        builder.Services.AddScoped<ITokenService, TokenService>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();
        builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        builder.Services.AddScoped<IAiStatsRepository, AiStatsRepository>();
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
        builder.Services.AddScoped<IBudgetAlertNotifier, BudgetAlertNotifier>();
        builder.Services.AddScoped<IAlertThresholdProvider, AlertThresholdProvider>();
        builder.Services.AddScoped<IAlertThresholdSettingsService, AlertThresholdSettingsService>();

        // Cách quản lý tài chính, khảo sát đầu vào, mục tiêu theo phương pháp.
        builder.Services.AddScoped<IFinanceMethodRepository, FinanceMethodRepository>();
        builder.Services.AddScoped<ISurveyRepository, SurveyRepository>();
        builder.Services.AddScoped<IUserMethodRepository, UserMethodRepository>();
        builder.Services.AddScoped<IFinanceSnapshotRepository, FinanceSnapshotRepository>();
        builder.Services.AddScoped<ISurveyService, SurveyService>();
        builder.Services.AddScoped<IMethodRecommendationService, MethodRecommendationService>();
        builder.Services.AddScoped<IUserMethodService, UserMethodService>();
        builder.Services.AddScoped<IGoalPlanningService, GoalPlanningService>();
        builder.Services.AddScoped<IJarService, JarService>();
        builder.Services.AddScoped<IAdminFinanceCatalogService, AdminFinanceCatalogService>();
        builder.Services.AddScoped<IDeviceTokenRepository, DeviceTokenRepository>();
        builder.Services.AddScoped<IQrTransferRepository, QrTransferRepository>();
        builder.Services.AddScoped<IQrTransferService, QrTransferService>();
        builder.Services.AddScoped<IQrTransferLinker, QrTransferLinker>();
        builder.Services.AddScoped<IReceiptScanRepository, ReceiptScanRepository>();
        builder.Services.AddScoped<ITrainingSampleService, TrainingSampleService>();
        builder.Services.AddScoped<IAiConsentReader, AiConsentReader>();
        builder.Services.AddScoped<ISavingGoalRepository, SavingGoalRepository>();
        builder.Services.AddScoped<IReportRepository, ReportRepository>();
        builder.Services.AddScoped<ISpendingForecaster, StatisticalSpendingForecaster>();
        builder.Services.AddScoped<IGamificationRepository, GamificationRepository>();
        builder.Services.AddScoped<IMissionRepository, MissionRepository>();
        builder.Services.AddScoped<IGamificationService, GamificationService>();
        builder.Services.AddScoped<IAIServiceClient, AIServiceClient>();
        builder.Services.AddSingleton<IFcmSender, FirebaseAdminFcmSender>();
        builder.Services.AddScoped<FcmPushNotificationService>();
        builder.Services.AddScoped<LoggingPushNotificationService>();
        builder.Services.AddScoped<IPushNotificationService, SwitchingPushNotificationService>();
        builder.Services.AddScoped<IGoogleTokenVerifier, GoogleTokenVerifier>();

        // Admin (Phase 8) — mọi controller ở đây nằm sau policy AdminOnly.
        builder.Services.AddScoped<IGetAuditLogListQueryHandler, GetAuditLogListQueryHandler>();
        builder.Services.AddScoped<IGetAdminUserListQueryHandler, GetAdminUserListQueryHandler>();
        builder.Services.AddScoped<IGetAdminUserDetailQueryHandler, GetAdminUserDetailQueryHandler>();
        builder.Services.AddScoped<ISetUserLockCommandHandler, SetUserLockCommandHandler>();
        builder.Services.AddScoped<ISetUserRoleCommandHandler, SetUserRoleCommandHandler>();
        builder.Services.AddScoped<IGetDataDeletionRequestListQueryHandler, GetDataDeletionRequestListQueryHandler>();
        builder.Services.AddScoped<ICancelDataDeletionRequestCommandHandler, CancelDataDeletionRequestCommandHandler>();
        builder.Services.AddScoped<IGetProviderConfigListQueryHandler, GetProviderConfigListQueryHandler>();
        builder.Services.AddScoped<ICreateProviderConfigCommandHandler, CreateProviderConfigCommandHandler>();
        builder.Services.AddScoped<IUpdateProviderConfigCommandHandler, UpdateProviderConfigCommandHandler>();
        builder.Services.AddScoped<ISetProviderConfigActivationCommandHandler, SetProviderConfigActivationCommandHandler>();
        builder.Services.AddScoped<IGetSystemCategoryListQueryHandler, GetSystemCategoryListQueryHandler>();
        builder.Services.AddScoped<ICreateSystemCategoryCommandHandler, CreateSystemCategoryCommandHandler>();
        builder.Services.AddScoped<IUpdateSystemCategoryCommandHandler, UpdateSystemCategoryCommandHandler>();
        builder.Services.AddScoped<ISetSystemCategoryActivationCommandHandler, SetSystemCategoryActivationCommandHandler>();
        builder.Services.AddScoped<IGetMissionListQueryHandler, GetMissionListQueryHandler>();
        builder.Services.AddScoped<ICreateMissionCommandHandler, CreateMissionCommandHandler>();
        builder.Services.AddScoped<IAdminAiRulesService, AdminAiRulesService>();
        // Đẩy danh mục hệ thống sang AI Service; AI_CATEGORY_SYNC_ENABLED=false để tắt hẳn (ví dụ bộ test tích hợp không có AI Service).
        builder.Services.AddSingleton(new AiCategorySyncOptions(builder.Configuration.GetValue<bool?>("AI_CATEGORY_SYNC_ENABLED") ?? true));
        builder.Services.AddScoped<IAiCategorySyncService, AiCategorySyncService>();
        builder.Services.AddScoped<IAdminAiConfigService, AdminAiConfigService>();
        builder.Services.AddScoped<IAdminProviderFormatsService, AdminProviderFormatsService>();
        builder.Services.AddScoped<IAdminRasaService, AdminRasaService>();
        builder.Services.AddScoped<IAdminMethodPromptService, AdminMethodPromptService>();
        builder.Services.AddScoped<IAdminSecretsService, AdminSecretsService>();
        builder.Services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
        builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ISystemConfigProvider, SystemConfigProvider>();
        builder.Services.AddScoped<IUpdateMissionCommandHandler, UpdateMissionCommandHandler>();
        builder.Services.AddScoped<ISetMissionActivationCommandHandler, SetMissionActivationCommandHandler>();
        builder.Services.AddScoped<IGetAiStatsQueryHandler, GetAiStatsQueryHandler>();

        builder.Services.AddScoped<IRegisterCommandHandler, RegisterCommandHandler>();
        builder.Services.AddScoped<ILoginCommandHandler, LoginCommandHandler>();
        builder.Services.AddScoped<IGoogleLoginCommandHandler, GoogleLoginCommandHandler>();
        builder.Services.AddScoped<IRefreshTokenCommandHandler, RefreshTokenCommandHandler>();
        builder.Services.AddScoped<ISendEmailVerificationCommandHandler, SendEmailVerificationCommandHandler>();
        builder.Services.AddScoped<IVerifyEmailCommandHandler, VerifyEmailCommandHandler>();
        builder.Services.AddScoped<IForgotPasswordCommandHandler, ForgotPasswordCommandHandler>();
        builder.Services.AddScoped<IResetPasswordCommandHandler, ResetPasswordCommandHandler>();
        builder.Services.AddScoped<ILogoutCommandHandler, LogoutCommandHandler>();
        builder.Services.AddScoped<ILogoutAllDevicesCommandHandler, LogoutAllDevicesCommandHandler>();
        builder.Services.AddScoped<IChangePasswordCommandHandler, ChangePasswordCommandHandler>();
        builder.Services.AddScoped<IDeleteAccountCommandHandler, DeleteAccountCommandHandler>();
        builder.Services.AddScoped<IGetUserProfileQueryHandler, GetUserProfileQueryHandler>();
        builder.Services.AddScoped<IUpdateUserProfileCommandHandler, UpdateUserProfileCommandHandler>();
        builder.Services.AddScoped<IUpdateNotificationPrefsCommandHandler, UpdateNotificationPrefsCommandHandler>();
        builder.Services.AddScoped<IConsentCommandHandler, ConsentCommandHandler>();

        builder.Services.AddScoped<IRegisterDeviceTokenCommandHandler, RegisterDeviceTokenCommandHandler>();
        builder.Services.AddScoped<IUnregisterDeviceTokenCommandHandler, UnregisterDeviceTokenCommandHandler>();

        builder.Services.AddScoped<ICreateFinancialAccountCommandHandler, CreateFinancialAccountCommandHandler>();
        builder.Services.AddScoped<IUpdateFinancialAccountCommandHandler, UpdateFinancialAccountCommandHandler>();
        builder.Services.AddScoped<IToggleAccountMonitoringCommandHandler, ToggleAccountMonitoringCommandHandler>();
        builder.Services.AddScoped<IDeleteFinancialAccountCommandHandler, DeleteFinancialAccountCommandHandler>();
        builder.Services.AddScoped<IGetAccountListQueryHandler, GetAccountListQueryHandler>();
        builder.Services.AddScoped<IGetAccountBalanceQueryHandler, GetAccountBalanceQueryHandler>();
        builder.Services.AddScoped<IGetProviderListQueryHandler, GetProviderListQueryHandler>();

        builder.Services.AddScoped<ICreateCategoryCommandHandler, CreateCategoryCommandHandler>();
        builder.Services.AddScoped<IUpdateCategoryCommandHandler, UpdateCategoryCommandHandler>();
        builder.Services.AddScoped<IDeleteCategoryCommandHandler, DeleteCategoryCommandHandler>();
        builder.Services.AddScoped<IGetCategoryListQueryHandler, GetCategoryListQueryHandler>();

        builder.Services.AddScoped<IAnalyzeNotificationCommandHandler, AnalyzeNotificationCommandHandler>();

        builder.Services.AddScoped<IConfirmTransactionCommandHandler, ConfirmTransactionCommandHandler>();
        builder.Services.AddScoped<ICreateManualTransactionCommandHandler, CreateManualTransactionCommandHandler>();
        builder.Services.AddScoped<ICreateTransferCommandHandler, CreateTransferCommandHandler>();
        builder.Services.AddScoped<IUpdateTransactionCommandHandler, UpdateTransactionCommandHandler>();
        builder.Services.AddScoped<IDeleteTransactionCommandHandler, DeleteTransactionCommandHandler>();
        builder.Services.AddScoped<IParseNaturalLanguageCommandHandler, ParseNaturalLanguageCommandHandler>();
        // Tách bill theo món: mặc định TẮT, giá trị sai cũng coi là tắt (Receipt__SplitByItemsEnabled khi đặt qua biến môi trường).
        builder.Services.AddSingleton(new FinMate.Application.Transactions.ReceiptOptions(
            bool.TryParse(builder.Configuration["Receipt:SplitByItemsEnabled"], out var splitByItems) && splitByItems));
        builder.Services.AddScoped<IScanReceiptCommandHandler, ScanReceiptCommandHandler>();
        builder.Services.AddScoped<IChatCommandHandler, ChatCommandHandler>();
        builder.Services.AddScoped<IVoiceChatCommandHandler, VoiceChatCommandHandler>();
        builder.Services.AddScoped<IChatAnalyzeCommandHandler, ChatAnalyzeCommandHandler>();
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

        builder.Services.AddScoped<IGetGamificationProfileQueryHandler, GetGamificationProfileQueryHandler>();
        builder.Services.AddScoped<IGetActiveMissionsQueryHandler, GetActiveMissionsQueryHandler>();
        builder.Services.AddScoped<IGetMissionHistoryQueryHandler, GetMissionHistoryQueryHandler>();
        builder.Services.AddScoped<IGetMascotInventoryQueryHandler, GetMascotInventoryQueryHandler>();
        builder.Services.AddScoped<IUpdateMascotOutfitCommandHandler, UpdateMascotOutfitCommandHandler>();

        builder.Services.AddScoped<DataDeletionJob>();
        builder.Services.AddScoped<RetryFailedNotificationJob>();
        builder.Services.AddScoped<BudgetAlertJob>();
        builder.Services.AddScoped<DailySummaryJob>();
        builder.Services.AddScoped<InsightGeneratorJob>();
        builder.Services.AddScoped<StreakCheckJob>();
        builder.Services.AddScoped<MissionResetJob>();
        builder.Services.AddScoped<GoalDeadlineCheckJob>();
        builder.Services.AddScoped<AiCategorySyncJob>();

        builder.Services.AddFinMateRateLimiting(builder.Configuration);

        builder.Services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres")
            .AddCheck<CacheHealthCheck>("redis");

        var app = builder.Build();

        app.Logger.LogInformation(
            app.Services.GetRequiredService<ISecretVault>().CanStore
                ? "Third-party key storage: SECRETS_MASTER_KEY set, admins can store keys in the database."
                : "Third-party key storage: SECRETS_MASTER_KEY absent, keys come from environment variables only.");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            db.Database.Migrate();

            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            FinMate.Infrastructure.Persistence.Seed.AdminUserSeeder.SeedAsync(db, passwordHasher, builder.Configuration).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.ProviderConfigSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.VietQrBankSeeder.SeedAsync(db).GetAwaiter().GetResult();
            var danglingProviderKeys = FinMate.Infrastructure.Persistence.Seed.VietQrBankSeeder.FindDanglingProviderKeysAsync(db).GetAwaiter().GetResult();
            if (danglingProviderKeys.Count > 0)
            {
                app.Logger.LogWarning("vietqr_banks points at provider_key values that do not exist in provider_configs ({Keys}); those banks will never auto-match notifications.", string.Join(", ", danglingProviderKeys));
            }
            FinMate.Infrastructure.Persistence.Seed.CategorySeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.MissionSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.MascotItemSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.SurveySeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.FinanceMethodSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.GoalTemplateSeeder.SeedAsync(db).GetAwaiter().GetResult();   // SAU FinanceMethodSeeder: bảng nối tham chiếu phương pháp
        }

        // TRƯỚC mọi middleware đọc IP hay scheme — nó viết lại chính hai thứ đó.
        if (TrustedProxies.BuildOptions(trustedProxies) is { } forwardedHeaders)
        {
            app.UseForwardedHeaders(forwardedHeaders);
        }

        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseMiddleware<RequestLoggingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        // SAU xác thực (cần biết người gọi có phải Admin không), TRƯỚC phân quyền: bảo trì chặn cả request chưa đăng nhập.
        app.UseMiddleware<MaintenanceMiddleware>();
        app.UseAuthorization();

        // SAU UseAuthentication có chủ ý: rate limiter phân vùng theo user id khi đã đăng nhập
        // (xem RateLimitingMiddleware), mà context.User chỉ được điền sau bước xác thực. Đánh
        // đổi: một cú flood ẩn danh vẫn phải đi qua bước kiểm chữ ký JWT trước khi bị chặn —
        // rẻ hơn nhiều so với chạm database, và các endpoint đăng nhập đều ẩn danh nên chúng
        // vốn không được lợi gì từ việc chặn sớm hơn.
        app.UseRateLimiter();

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

        // 16:55 UTC = 23:55 VN, 17:01 UTC = 00:01 VN — xem ghi chú quy đổi ở trên.
        RecurringJob.AddOrUpdate<StreakCheckJob>(
            "streak-check",
            job => job.RunAsync(CancellationToken.None),
            "55 16 * * *");

        RecurringJob.AddOrUpdate<MissionResetJob>(
            "mission-reset",
            job => job.RunAsync(CancellationToken.None),
            "1 17 * * *");

        // Bù cho mọi lần đồng bộ hụt (AI Service khởi động sau backend, được dựng lại với DB trống): mỗi giờ, và một lần ngay lúc khởi động.
        // Lần khởi động đi qua Hangfire nên AI Service chưa lên thì được thử lại thay vì làm hỏng việc khởi động của backend.
        RecurringJob.AddOrUpdate<AiCategorySyncJob>(
            "ai-category-sync",
            job => job.RunAsync(CancellationToken.None),
            "30 * * * *");
        BackgroundJob.Enqueue<AiCategorySyncJob>(job => job.RunAsync(CancellationToken.None));

        app.MapControllers();
        // Tách sống/sẵn-sàng vì hai bên hành động khác nhau: liveness hỏng thì khởi động lại
        // container, readiness hỏng thì ngừng đẩy traffic vào. Gộp làm một là Postgres sập kéo
        // theo cả đàn app restart vô ích, mà restart thì không cứu được Postgres.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous();

        app.MapHealthChecks("/health/ready").AllowAnonymous();

        // Giữ /health cho thứ đang gọi nó; nay nó nói thật thay vì luôn trả "healthy".
        app.MapHealthChecks("/health").AllowAnonymous();

        app.Run();
    }
}
