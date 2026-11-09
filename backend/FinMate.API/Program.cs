using Microsoft.AspNetCore.Authentication;
using FinMate.Infrastructure.ExternalServices.SePay;
using FinMate.Application.Subscriptions;
using FinMate.API.Authorization;
using Microsoft.AspNetCore.Authorization;
using FinMate.API.Configuration;
using FinMate.API.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using FinMate.API.Middleware;
using FinMate.Application.Admin.Commands;
using FinMate.Application.Admin.FinanceCatalog;
using FinMate.Application.Agent;
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
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Application.Devices.Commands;
using FinMate.Application.FinanceMethods;
using FinMate.Application.FinancialAccounts.Commands;
using FinMate.Application.FinancialAccounts.Queries;
using FinMate.Application.Notifications.Commands;
using FinMate.Application.QrTransfers;
using FinMate.Application.Forecasting;
using FinMate.Application.Groups;
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
using FinMate.Application.Community;
using FinMate.Application.MarketData;
using FinMate.Infrastructure.Community;
using FinMate.Application.Monitoring;
using FinMate.Application.Ops;
using FinMate.API.Monitoring;
using FinMate.Infrastructure.MarketData;
using FinMate.Infrastructure.Monitoring;
using FinMate.Application.Support;
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

        builder.Services.AddSingleton<OpsErrorQueue>();
        builder.Services.AddSingleton<OpsErrorSink>();
        var monitoringEnabled = OpsMonitoringOptions.IsEnabled(builder.Configuration["MONITORING_ENABLED"]);
        builder.Services.AddSingleton(new OpsMonitoringOptions(monitoringEnabled));

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .WriteTo.Sink(services.GetRequiredService<OpsErrorSink>())
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
            StartupSecretGuard.ThrowIfEvalEnabled(builder.Configuration, builder.Environment.EnvironmentName);
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
        builder.Services.AddHostedService<OpsErrorWriter>();
        builder.Services.AddSingleton<IRecurringJobReader, HangfireRecurringJobReader>();
        builder.Services.AddSingleton<OpsJobFailureFilter>();

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
            .AddScheme<AuthenticationSchemeOptions, BillingSessionAuthenticationHandler>(BillingSessionAuthenticationHandler.SchemeName, null)
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
                policy => policy.RequireRole(nameof(UserRole.Admin)).AddRequirements(new OpsVerifiedRequirement()));
            options.AddPolicy(
                AuthorizationPolicies.StaffOrAbove,
                policy => policy.RequireRole(nameof(UserRole.Staff), nameof(UserRole.OpsManager), nameof(UserRole.Admin)).AddRequirements(new OpsVerifiedRequirement()));
            options.AddPolicy(
                AuthorizationPolicies.ManagerOrAbove,
                policy => policy.RequireRole(nameof(UserRole.OpsManager), nameof(UserRole.Admin)).AddRequirements(new OpsVerifiedRequirement()));
            options.AddPolicy(
                AuthorizationPolicies.InternalAccount,
                policy => policy.RequireRole(nameof(UserRole.Staff), nameof(UserRole.OpsManager), nameof(UserRole.Admin)));
            options.AddPolicy(
                AuthorizationPolicies.BillingSession,
                policy => policy.AddAuthenticationSchemes(BillingSessionAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
        });

        // Không khai báo domain nào thì policy rỗng, trình duyệt không được phép gọi. Xác thực bằng header Bearer nên không bật AllowCredentials.
        var opsWebOrigins = OpsWebOrigins.Parse(builder.Configuration["OPS_WEB_ORIGINS"]);
        builder.Services.AddCors(options => options.AddPolicy(
            OpsWebOrigins.PolicyName,
            policy => policy.WithOrigins(opsWebOrigins.ToArray()).AllowAnyMethod().WithHeaders("Authorization", "Content-Type")));

        // Tách khỏi web vận hành: domain web thanh toán chỉ được gọi đúng các API thanh toán.
        var subscriptionWebOrigins = OpsWebOrigins.Parse(builder.Configuration["SUBSCRIPTION_WEB_ORIGINS"]);
        builder.Services.AddCors(options => options.AddPolicy(
            SubscriptionConfiguration.CorsPolicyName,
            policy => policy.WithOrigins(subscriptionWebOrigins.ToArray()).AllowAnyMethod().WithHeaders(BillingSessionAuthenticationHandler.HeaderName, "Content-Type")));

        builder.Services.AddSingleton(new AuthOptions(requireEmailVerification));

        // Mặc định bật ngoài Development. Đặt false chỉ khi email đang hỏng và cần vào được hệ thống để sửa.
        var requireOpsTwoFactor = bool.TryParse(builder.Configuration["OPS_REQUIRE_2FA"], out var parsedOpsTwoFactor)
            ? parsedOpsTwoFactor
            : !builder.Environment.IsDevelopment();
        builder.Services.AddSingleton(new OpsAuthOptions(requireOpsTwoFactor));
        builder.Services.AddSingleton<IAuthorizationHandler, OpsVerifiedHandler>();

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
        builder.Services.AddScoped<IEconomicAssumptionRepository, EconomicAssumptionRepository>();
        builder.Services.AddScoped<TicketAssistService>();
        builder.Services.AddScoped<IAssumptionService, AssumptionService>();
        builder.Services.AddScoped<GetAssumptionsTool>();

        // AI Financial Agent: công cụ nằm trong danh sách cho phép (ToolRegistry dựng từ mọi IAgentTool đã đăng ký), mọi dữ liệu đọc qua IFinancialDataSource.
        builder.Services.AddScoped<RepositoryFinancialDataSource>();
        builder.Services.AddScoped<SwitchableFinancialDataSource>();
        builder.Services.AddScoped<IFinancialDataSource>(sp => sp.GetRequiredService<SwitchableFinancialDataSource>());
        builder.Services.AddScoped<AgentRunOptions>();
        builder.Services.AddScoped<IncomeHistory>();
        builder.Services.AddScoped<IAgentTool, ForecastGoalTool>();
        builder.Services.AddScoped<IAgentTool, GetAssumptionsAgentTool>();
        builder.Services.AddScoped<IAgentTool, SearchKnowledgeAgentTool>();
        builder.Services.AddScoped<IAgentTool, SimulatePurchaseTool>();
        builder.Services.AddScoped<IAgentTool, RunwayTool>();
        builder.Services.AddScoped<IAgentTool, StressTestTool>();
        builder.Services.AddScoped<IAgentTool, SensitivityTool>();
        builder.Services.AddScoped<ToolRegistry>();
        builder.Services.AddScoped<ToolExecutor>();
        builder.Services.AddScoped<AgentTraceContext>();
        builder.Services.AddScoped<ConversationStore>();
        builder.Services.AddScoped<AgentMemoryService>();
        builder.Services.AddScoped<IAgentOrchestrator, AgentOrchestrator>();
        // Cổng phạm vi / lập kế hoạch / soạn câu của agent. Timeout 45 giây: lập kế hoạch và soạn câu mỗi lần gọi LLM (tối đa vài chục giây khi nhà cung cấp chậm, cộng lần sinh lại).
        builder.Services.AddHttpClient<IAgentAiClient, AgentAiClient>(client =>
        {
            client.BaseAddress = new Uri(aiServiceUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiServiceApiKey);
            client.Timeout = TimeSpan.FromSeconds(45);
        });
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

        // Dự báo tài chính (Flow 6, đợt 1).
        builder.Services.AddScoped<IConcurrencyRetry, EfConcurrencyRetry>();
        builder.Services.AddScoped<IGroupRepository, GroupRepository>();
        builder.Services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        builder.Services.AddScoped<IRolePermissionService, RolePermissionService>();
        builder.Services.AddScoped<IOpsAccessService, OpsAccessService>();
        builder.Services.AddScoped<IMonitoringRepository, MonitoringRepository>();
        builder.Services.AddScoped<ICommunityRepository, CommunityRepository>();
        builder.Services.AddScoped<ISpentHereVerifier, SpentHereVerifier>();
        builder.Services.AddScoped<ICommunityReadService, CommunityReadService>();
        builder.Services.AddScoped<ICommunityService, CommunityService>();
        builder.Services.AddScoped<ICommunityCommentService, CommunityCommentService>();
        builder.Services.AddScoped<ICommunityImageService, CommunityImageService>();
        builder.Services.AddScoped<ICommunityReportService, CommunityReportService>();
        builder.Services.AddScoped<ICommunitySharePromptService, CommunitySharePromptService>();
        builder.Services.AddScoped<IOpsCommunityService, OpsCommunityService>();
        builder.Services.AddSingleton(SubscriptionConfiguration.Read(builder.Configuration));
        builder.Services.AddHttpClient(SePayClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        builder.Services.AddScoped<ISePayClient, SePayClient>();
        builder.Services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        builder.Services.AddScoped<ISubscriptionEntitlementService, SubscriptionEntitlementService>();
        builder.Services.AddScoped<IOpsSubscriptionService, OpsSubscriptionService>();
        builder.Services.AddScoped<IBillingWebSessionStore, BillingWebSessionStore>();
        builder.Services.AddScoped<ISubscriptionCheckoutService, SubscriptionCheckoutService>();
        builder.Services.AddScoped<ISubscriptionSettlementService, SubscriptionSettlementService>();
        builder.Services.AddScoped<SubscriptionReconcileJob>();
        builder.Services.AddScoped<SubscriptionReminderJob>();
        builder.Services.AddSingleton<ICommunityImageProcessor, SkiaCommunityImageProcessor>();
        builder.Services.AddScoped<IMarketDataRepository, MarketDataRepository>();
        builder.Services.AddScoped<IMarketDataService, MarketDataService>();
        builder.Services.AddScoped<IMarketSignalProcessor, MarketSignalProcessor>();
        builder.Services.AddScoped<IMarketEventService, MarketEventService>();
        builder.Services.AddScoped<IMarketAlertService, MarketAlertService>();
        builder.Services.AddScoped<IMarketAlertTrigger, HangfireMarketAlertTrigger>();

        // Trần Gemini phía ai-service là 30 giây cho cả chuỗi model.
        builder.Services.AddHttpClient<IMarketAiClient, MarketAiClient>(client =>
        {
            client.BaseAddress = new Uri(aiServiceUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aiServiceApiKey);
            client.Timeout = TimeSpan.FromSeconds(45);
        });
        builder.Services.AddHttpClient(SafeWebFetcher.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(SafeWebFetcher.CreateHandler);
        builder.Services.AddSingleton<IWebPageFetcher, SafeWebFetcher>();
        builder.Services.AddScoped<IIncidentService, IncidentService>();
        builder.Services.AddScoped<IIncidentNotifier, IncidentNotifier>();
        builder.Services.AddScoped<IOpsMonitoringService, OpsMonitoringService>();
        builder.Services.AddScoped<ServiceStatusTracker>();
        builder.Services.AddScoped<ISystemMonitor, SystemMonitor>();
        builder.Services.AddScoped<IMonitorSource, HealthMonitorSource>();
        builder.Services.AddScoped<IMonitorSource, ErrorMonitorSource>();
        builder.Services.AddScoped<IMonitorSource, JobMonitorSource>();
        builder.Services.AddScoped<IMonitorSource, ProviderMonitorSource>();
        builder.Services.AddScoped<IMonitorSource, SubscriptionReviewMonitorSource>();
        builder.Services.AddSingleton<IProviderFailureTracker, ProviderFailureTracker>();
        builder.Services.AddSingleton(new ProviderStatusEndpoints(ocrServiceUrl, builder.Configuration["OCR_SERVICE_API_KEY"], aiServiceUrl, aiServiceApiKey));
        builder.Services.AddScoped<IProviderStatusClient, ProviderStatusClient>();
        builder.Services.AddScoped<IAdminErrorEventService, AdminErrorEventService>();
        builder.Services.AddHttpClient(HttpServiceProbe.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(5));
        builder.Services.AddScoped<IServiceProbe, RedisProbe>();
        builder.Services.AddScoped<IServiceProbe>(sp => new HttpServiceProbe(MonitoredServices.AiService, $"{aiServiceUrl.TrimEnd('/')}/api/v1/health", sp.GetRequiredService<IHttpClientFactory>()));
        builder.Services.AddScoped<IServiceProbe>(sp => new HttpServiceProbe(MonitoredServices.OcrService, string.IsNullOrWhiteSpace(ocrServiceUrl) ? null : $"{ocrServiceUrl.TrimEnd('/')}/health", sp.GetRequiredService<IHttpClientFactory>()));
        builder.Services.AddScoped<IServiceProbe>(sp => new HttpServiceProbe(MonitoredServices.SttService, string.IsNullOrWhiteSpace(sttServiceUrl) ? null : $"{sttServiceUrl.TrimEnd('/')}/health", sp.GetRequiredService<IHttpClientFactory>()));
        builder.Services.AddScoped<IOpsTwoFactorService, OpsTwoFactorService>();
        builder.Services.AddScoped<IOpsRepository, OpsRepository>();
        builder.Services.AddScoped<IOpsApprovalService, OpsApprovalService>();
        builder.Services.AddScoped<IOpsUserService, OpsUserService>();
        builder.Services.AddScoped<IOpsManagerService, OpsManagerService>();
        builder.Services.AddScoped<IGroupSupportTransfer, GroupSupportTransfer>();
        builder.Services.AddScoped<ISupportRepository, SupportRepository>();
        builder.Services.AddScoped<ISupportTicketService, SupportTicketService>();
        builder.Services.AddScoped<IOpsTicketService, OpsTicketService>();
        builder.Services.AddScoped<ISupportAccessService, SupportAccessService>();
        builder.Services.AddScoped<IOpsCustomerDataService, OpsCustomerDataService>();
        builder.Services.AddScoped<ISupportRetentionService, SupportRetentionService>();
        builder.Services.AddSingleton<IAttachmentProtector, SecretCipherAttachmentProtector>();
        builder.Services.AddScoped<IGroupAccessService, GroupAccessService>();
        builder.Services.AddScoped<IGroupService, GroupService>();
        builder.Services.AddScoped<IGroupMemberService, GroupMemberService>();
        builder.Services.AddScoped<IGroupInvitationService, GroupInvitationService>();
        builder.Services.AddScoped<IGroupFinanceRepository, GroupFinanceRepository>();
        builder.Services.AddScoped<IGroupAlertNotifier, GroupAlertNotifier>();
        builder.Services.AddScoped<IGroupLedger, GroupLedger>();
        builder.Services.AddScoped<IGroupTransactionService, GroupTransactionService>();
        builder.Services.AddScoped<IGroupBudgetService, GroupBudgetService>();
        builder.Services.AddScoped<IGroupGoalService, GroupGoalService>();
        builder.Services.AddScoped<IGroupCategoryService, GroupCategoryService>();
        builder.Services.AddScoped<IGroupReportService, GroupReportService>();
        builder.Services.AddScoped<IGroupOwnershipHandover, GroupOwnershipHandover>();
        builder.Services.AddScoped<IRecurringItemRepository, RecurringItemRepository>();
        builder.Services.AddScoped<IForecastDataRepository, ForecastDataRepository>();
        builder.Services.AddScoped<IForecastService, ForecastService>();
        builder.Services.AddScoped<IPurchaseCheckService, PurchaseCheckService>();
        builder.Services.AddScoped<IRecurringItemService, RecurringItemService>();
        builder.Services.AddScoped<IRecurringDetectionService, RecurringDetectionService>();
        builder.Services.AddScoped<IForecastSettingsService, ForecastSettingsService>();
        builder.Services.AddScoped<IForecastAlertRepository, ForecastAlertRepository>();
        builder.Services.AddScoped<IForecastAlertDispatcher, ForecastAlertDispatcher>();
        builder.Services.AddScoped<IForecastAlertService, ForecastAlertService>();
        builder.Services.AddScoped<IBudgetForecastService, BudgetForecastService>();
        builder.Services.AddScoped<IIncomeForecastService, IncomeForecastService>();
        builder.Services.AddScoped<IAnomalyRepository, AnomalyRepository>();
        builder.Services.AddScoped<IAnomalyService, AnomalyService>();
        builder.Services.AddScoped<ICategoryForecastService, CategoryForecastService>();
        builder.Services.AddScoped<IGoalForecastService, GoalForecastService>();
        builder.Services.AddScoped<IForecastAlertTrigger, HangfireForecastAlertTrigger>();
        builder.Services.AddScoped<ITransferBudgetLimitCommandHandler, TransferBudgetLimitCommandHandler>();
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
        builder.Services.AddScoped<IAdminAiRulesService, AdminAiRulesService>();
        // Đẩy danh mục hệ thống sang AI Service; AI_CATEGORY_SYNC_ENABLED=false để tắt hẳn (ví dụ bộ test tích hợp không có AI Service).
        builder.Services.AddSingleton(new AiCategorySyncOptions(builder.Configuration.GetValue<bool?>("AI_CATEGORY_SYNC_ENABLED") ?? true));
        builder.Services.AddScoped<IAiCategorySyncService, AiCategorySyncService>();
        builder.Services.AddScoped<IAdminAiConfigService, AdminAiConfigService>();
        builder.Services.AddScoped<IAdminProviderFormatsService, AdminProviderFormatsService>();
        builder.Services.AddScoped<IAdminRasaService, AdminRasaService>();
        builder.Services.AddScoped<IAdminKnowledgeService, AdminKnowledgeService>();
        builder.Services.AddScoped<IAdminLearningService, AdminLearningService>();
        builder.Services.AddScoped<IAdminMethodPromptService, AdminMethodPromptService>();
        builder.Services.AddScoped<IAdminSecretsService, AdminSecretsService>();
        builder.Services.AddScoped<ISystemConfigRepository, SystemConfigRepository>();
        builder.Services.AddScoped<ISystemConfigService, SystemConfigService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ISystemConfigProvider, SystemConfigProvider>();
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

        builder.Services.AddScoped<ConfirmTransactionCommandHandler>();
        builder.Services.AddScoped<IConfirmTransactionCommandHandler>(sp => new RetryingConfirmTransactionCommandHandler(sp.GetRequiredService<ConfirmTransactionCommandHandler>(), sp.GetRequiredService<IConcurrencyRetry>()));
        builder.Services.AddScoped<CreateManualTransactionCommandHandler>();
        builder.Services.AddScoped<ICreateManualTransactionCommandHandler>(sp => new RetryingCreateManualTransactionCommandHandler(sp.GetRequiredService<CreateManualTransactionCommandHandler>(), sp.GetRequiredService<IConcurrencyRetry>()));
        builder.Services.AddScoped<CreateTransferCommandHandler>();
        builder.Services.AddScoped<ICreateTransferCommandHandler>(sp => new RetryingCreateTransferCommandHandler(sp.GetRequiredService<CreateTransferCommandHandler>(), sp.GetRequiredService<IConcurrencyRetry>()));
        builder.Services.AddScoped<UpdateTransactionCommandHandler>();
        builder.Services.AddScoped<IUpdateTransactionCommandHandler>(sp => new RetryingUpdateTransactionCommandHandler(sp.GetRequiredService<UpdateTransactionCommandHandler>(), sp.GetRequiredService<IConcurrencyRetry>()));
        builder.Services.AddScoped<DeleteTransactionCommandHandler>();
        builder.Services.AddScoped<IDeleteTransactionCommandHandler>(sp => new RetryingDeleteTransactionCommandHandler(sp.GetRequiredService<DeleteTransactionCommandHandler>(), sp.GetRequiredService<IConcurrencyRetry>()));
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
        builder.Services.AddScoped<ContributeToGoalCommandHandler>();
        builder.Services.AddScoped<IContributeToGoalCommandHandler>(sp => new RetryingContributeToGoalCommandHandler(sp.GetRequiredService<ContributeToGoalCommandHandler>(), sp.GetRequiredService<IConcurrencyRetry>()));
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
        builder.Services.AddScoped<SystemMonitorJob>();
        builder.Services.AddScoped<MonitoringDigestJob>();
        builder.Services.AddScoped<MarketDataRefreshJob>();
        builder.Services.AddScoped<MarketAlertJob>();
        builder.Services.AddScoped<SupportRetentionJob>();
        builder.Services.AddScoped<RetryFailedNotificationJob>();
        builder.Services.AddScoped<BudgetAlertJob>();
        builder.Services.AddScoped<DailySummaryJob>();
        builder.Services.AddScoped<InsightGeneratorJob>();
        builder.Services.AddScoped<GoalDeadlineCheckJob>();
        builder.Services.AddScoped<RecurringDetectionJob>();
        builder.Services.AddScoped<ForecastDailyAlertJob>();
        builder.Services.AddScoped<ForecastAlertDispatchJob>();
        builder.Services.AddScoped<ForecastUserCheckJob>();
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
            FinMate.Infrastructure.Persistence.Seed.RolePermissionSeeder.SeedAsync(db).GetAwaiter().GetResult();
            if (builder.Environment.IsDevelopment())
            {
                // Tài khoản Staff/OpsManager để thử web vận hành; mật khẩu bỏ qua chính sách đăng ký nên KHÔNG bao giờ chạy ngoài Development.
                FinMate.Infrastructure.Persistence.Seed.DevOpsAccountSeeder.SeedAsync(db, passwordHasher, builder.Configuration).GetAwaiter().GetResult();
            }
            FinMate.Infrastructure.Persistence.Seed.ProviderConfigSeeder.SeedAsync(db).GetAwaiter().GetResult();
            FinMate.Infrastructure.Persistence.Seed.VietQrBankSeeder.SeedAsync(db).GetAwaiter().GetResult();
            var danglingProviderKeys = FinMate.Infrastructure.Persistence.Seed.VietQrBankSeeder.FindDanglingProviderKeysAsync(db).GetAwaiter().GetResult();
            if (danglingProviderKeys.Count > 0)
            {
                app.Logger.LogWarning("vietqr_banks points at provider_key values that do not exist in provider_configs ({Keys}); those banks will never auto-match notifications.", string.Join(", ", danglingProviderKeys));
            }
            FinMate.Infrastructure.Persistence.Seed.CategorySeeder.SeedAsync(db).GetAwaiter().GetResult();
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

        // TRƯỚC chuyển hướng HTTPS và bảo trì: trình duyệt không gửi token khi preflight và không chấp nhận preflight bị chuyển hướng hay 503.
        app.UseWhen(context => SubscriptionConfiguration.IsBillingWebPath(context.Request.Path), branch => branch.UseCors(SubscriptionConfiguration.CorsPolicyName));
        app.UseWhen(context => !SubscriptionConfiguration.IsBillingWebPath(context.Request.Path), branch => branch.UseCors(OpsWebOrigins.PolicyName));

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

        // 03:00 giờ VN. Hangfire đọc cron theo UTC.
        RecurringJob.AddOrUpdate<DataDeletionJob>(
            "data-deletion",
            job => job.RunAsync(CancellationToken.None),
            "0 20 * * *");

        // 03:30 giờ VN, sau data-deletion.
        RecurringJob.AddOrUpdate<SupportRetentionJob>(
            "support-retention",
            job => job.RunAsync(CancellationToken.None),
            "30 20 * * *");

        if (monitoringEnabled)
        {
            GlobalJobFilters.Filters.Add(app.Services.GetRequiredService<OpsJobFailureFilter>());
        }

        // Mặc định bật. AuthApiFactory tắt để job không chạy nền trong bộ test.
        if (monitoringEnabled)
        {
            RecurringJob.AddOrUpdate<SystemMonitorJob>("system-monitor", job => job.RunAsync(CancellationToken.None), "* * * * *");
            RecurringJob.AddOrUpdate<MonitoringDigestJob>("monitoring-digest", job => job.RunAsync(CancellationToken.None), "0 1 * * *");
        }
        else
        {
            RecurringJob.RemoveIfExists("system-monitor");
            RecurringJob.RemoveIfExists("monitoring-digest");
        }

        // Mỗi 3 giờ, lệch phút 5 để không dồn vào đầu giờ của các trang nguồn.
        RecurringJob.AddOrUpdate<MarketDataRefreshJob>(
            "market-data-refresh",
            job => job.RunAsync(CancellationToken.None),
            "5 */3 * * *");

        RecurringJob.AddOrUpdate<SubscriptionReconcileJob>(
            "subscription-reconcile",
            job => job.RunAsync(CancellationToken.None),
            "*/10 * * * *");

        // 09:00 giờ VN.
        RecurringJob.AddOrUpdate<SubscriptionReminderJob>(
            "subscription-reminders",
            job => job.RunAsync(CancellationToken.None),
            "0 2 * * *");

        RecurringJob.AddOrUpdate<RetryFailedNotificationJob>(
            "retry-failed-notifications",
            job => job.RunAsync(CancellationToken.None),
            "*/15 * * * *");

        RecurringJob.AddOrUpdate<BudgetAlertJob>(
            "budget-alerts",
            job => job.RunAsync(CancellationToken.None),
            "0 * * * *");

        // 08:00 giờ VN là khung giờ hợp lý để gửi nhắc nhở. Hangfire đọc cron theo UTC.
        RecurringJob.AddOrUpdate<GoalDeadlineCheckJob>(
            "goal-deadline-check",
            job => job.RunAsync(CancellationToken.None),
            "0 1 * * *");

        // Hangfire đọc cron theo UTC. ARCHITECTURE.md §5 ghi giờ theo ý định vận hành (giờ VN)
        // nên hai job dưới phải trừ 7 tiếng: 17:05 UTC = 00:05 VN, 19:00 UTC = 02:00 VN.
        RecurringJob.AddOrUpdate<DailySummaryJob>(
            "daily-summary",
            job => job.RunAsync(CancellationToken.None),
            "5 17 * * *");

        // 18:30 UTC = 01:30 VN: sau DailySummaryJob, trước InsightGeneratorJob.
        RecurringJob.AddOrUpdate<RecurringDetectionJob>(
            "recurring-detection",
            job => job.RunAsync(CancellationToken.None),
            "30 18 * * *");

        // 01:00 UTC = 08:00 VN: cảnh báo dự báo hằng ngày, sau RecurringDetectionJob và không push giữa đêm.
        RecurringJob.AddOrUpdate<ForecastDailyAlertJob>(
            "forecast-daily-alerts",
            job => job.RunAsync(CancellationToken.None),
            "0 1 * * *");

        RecurringJob.AddOrUpdate<ForecastAlertDispatchJob>(
            "forecast-alert-dispatch",
            job => job.RunAsync(CancellationToken.None),
            "*/15 * * * *");

        RecurringJob.AddOrUpdate<InsightGeneratorJob>(
            "insight-generator",
            job => job.RunAsync(CancellationToken.None),
            "0 19 * * *");

        // Gamification đã gỡ (2026-11-02). Hangfire lưu lịch trong Postgres, nên DB đã từng chạy bản cũ vẫn giữ
        // hai job này và sẽ cố gọi class không còn tồn tại; xóa chúng ở mỗi lần khởi động (không có thì bỏ qua).
        RecurringJob.RemoveIfExists("streak-check");
        RecurringJob.RemoveIfExists("mission-reset");

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
