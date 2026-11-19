using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FinMate.Application.Admin.SystemConfig;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace FinMate.Infrastructure.Persistence.Seed;

/// <summary>Opt-in, atomic development dataset. Real reference facts and explicitly labelled fictional scenarios; never overwrites existing data.</summary>
public static partial class DashboardDevelopmentSeeder
{
    public const string EnabledKey = "DEV_SEED_DASHBOARD_ENABLED";
    public const string PasswordKey = "DEV_SEED_DASHBOARD_PASSWORD";
    public const string SeedOnlyArgument = "--seed-dashboard-dev-only";
    public const string Version = "dashboard-development-v1";
    public const string Label = "[Kiểm thử]";
    private static readonly string[] PeopleKeys = ["admin", "staff", "manager", "an", "binh", "chi", "dung", "ha", "linh"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Email(string key) => $"dashboard-{key}@finmate.local";
    public static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{Version}:{key}"))[..16]);

    public static async Task<bool> SeedAsync(FinMateDbContext db, IPasswordHasher hasher, IConfiguration config,
        bool isDevelopment, CancellationToken ct = default)
    {
        if (!isDevelopment || !bool.TryParse(config[EnabledKey], out var enabled) || !enabled) return false;
        if (await db.AuditLogs.AnyAsync(row => row.Id == Id("complete"), ct)) return false;
        var password = config[PasswordKey];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            throw new InvalidOperationException($"{PasswordKey} must contain at least 12 characters.");
        var reservedEmails = PeopleKeys.Select(Email).ToArray();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(user => reservedEmails.Contains(user.Email), ct))
            throw new InvalidOperationException("A reserved dashboard test account already exists. No existing accounts will be changed.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = DateTimeOffset.UtcNow;
        var categories = await db.Categories.Where(row => row.UserId == null).ToDictionaryAsync(row => row.Slug, ct);
        var providers = await db.ProviderConfigs.ToDictionaryAsync(row => row.ProviderKey, ct);
        RequireReferences(categories, providers);
        var settings = await ReadSettingsAsync(db, ct);
        var termsVersion = await db.LegalVersions.Where(row => row.Status == LegalVersionStatus.Published).MaxAsync(row => row.Version, ct);
        var people = CreatePeople(db, hasher, password, now, termsVersion);
        var ledgers = AddFinances(db, people, categories, providers, now);
        AddSupport(db, people, ledgers, settings.Support, now);
        AddCommunity(db, people, now);
        AddSubscriptions(db, people, now);
        AddPublicReferences(db, people["admin"].Id, now);
        AddForecastFeedback(db, people, now);
        db.AuditLogs.Add(new AuditLog { Id = Id("complete"), UserId = people["admin"].Id,
            EventType = "Dev.Dashboard.DatasetCreated", CreatedAt = now,
            Metadata = JsonSerializer.Serialize(new { dataset = Version, fictionalScenarios = true, anchorDate = VietnamTime.Today() }, JsonOptions) });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private static void RequireReferences(Dictionary<string, Category> categories, Dictionary<string, ProviderConfig> providers)
    {
        foreach (var key in new[] { "income", "food", "housing", "bills", "transport", "shopping", "entertainment" })
            if (!categories.ContainsKey(key)) throw new InvalidOperationException($"Missing system category: {key}");
        if (!providers.ContainsKey("mb_bank")) throw new InvalidOperationException("Missing MB Bank reference configuration.");
    }

    private static async Task<SystemConfigDocument> ReadSettingsAsync(FinMateDbContext db, CancellationToken ct)
    {
        var current = await db.SystemConfigVersions.AsNoTracking().Where(row => row.Name == SystemConfigNames.App
            && row.Status == SystemConfigStatus.Active).OrderByDescending(row => row.Version).FirstOrDefaultAsync(ct);
        return current == null ? new SystemConfigDocument() : JsonSerializer.Deserialize<SystemConfigDocument>(current.Content, JsonOptions)
            ?? throw new InvalidOperationException("Cannot read current system settings.");
    }

    private static Dictionary<string, User> CreatePeople(FinMateDbContext db, IPasswordHasher hasher, string password, DateTimeOffset now, int? termsVersion)
    {
        string[] names = ["Quản trị dashboard", "Nhân viên hỗ trợ", "Quản lý vận hành", "An — lương ổn định",
            "Bình — thu nhập tự do", "Chi — sinh viên", "Dũng — chờ mở khóa", "Hà — chờ hủy xóa", "Linh — người mới"];
        var people = new Dictionary<string, User>();
        for (var i = 0; i < PeopleKeys.Length; i++)
        {
            var key = PeopleKeys[i];
            var role = key switch { "admin" => UserRole.Admin, "staff" => UserRole.Staff, "manager" => UserRole.OpsManager, _ => UserRole.User };
            var user = new User { Id = Id($"user:{key}"), Email = Email(key), DisplayName = $"{Label} {names[i]}",
                PasswordHash = hasher.Hash(password), Role = role, EmailVerifiedAt = key == "linh" ? now.AddMinutes(-25) : now.AddMonths(-6).AddHours(1),
                CreatedAt = key == "linh" ? now.AddMinutes(-30) : now.AddMonths(-6), UpdatedAt = now, IsLocked = key == "dung",
                TermsVersion = termsVersion, TermsAcceptedAt = termsVersion.HasValue ? now : null,
                CommunityConsentVersion = key is "an" or "binh" or "chi" ? ConsentVersions.Community : null,
                CommunityConsentAt = key is "an" or "binh" or "chi" ? now.AddMonths(-5) : null,
                DeletedAt = key == "ha" ? now.AddDays(-10) : null };
            user.RoleAssignment!.AssignedAt = user.CreatedAt;
            people.Add(key, user);
            db.Users.Add(user);
        }
        return people;
    }
}
