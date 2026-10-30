using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;
using FinMate.Domain.Entities;
using FinMate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FinMate.Infrastructure.Security;

/// <summary>
/// Kho key bên thứ ba của backend. Singleton vì code gửi email / đổi code Google / đẩy FCM cần đọc key mà không có scope của request; mỗi lần đọc DB mở một scope riêng.
/// Giá trị rõ giữ trong bộ nhớ đệm của TIẾN TRÌNH vài giây (không bao giờ ở Redis, không ra log), và bị xóa ngay khi lưu hay xóa key. Có nhiều máy chủ thì máy khác thấy đổi chậm nhất bằng
/// thời gian hết hạn.
/// </summary>
public sealed class SecretVault : ISecretVault
{
    public const string UnavailableCode = "SECRETS_UNAVAILABLE";
    public const string UnknownCode = "SECRET_UNKNOWN";
    public const string InvalidCode = "SECRET_INVALID";

    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(10);

    private readonly SecretCipher _cipher;
    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;

    public SecretVault(SecretCipher cipher, IMemoryCache cache, IServiceScopeFactory scopes, IConfiguration configuration)
    {
        _cipher = cipher;
        _cache = cache;
        _scopes = scopes;
        _configuration = configuration;
    }

    public bool CanStore => _cipher.Available;

    private static string CacheKey(string name) => $"secret:{name}";

    private string? FromEnvironment(BackendSecrets.Definition definition)
    {
        var value = _configuration[definition.EnvVar];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public async Task<string?> GetAsync(string name, CancellationToken ct = default)
    {
        var definition = BackendSecrets.Find(name) ?? throw new BusinessRuleException(UnknownCode, "Không có key này trong danh mục.");
        if (_cache.TryGetValue(CacheKey(name), out string? cached))
        {
            return cached;
        }

        string? value;
        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
            var row = await db.SecretEntries.AsNoTracking().FirstOrDefaultAsync(s => s.Name == name, ct);
            // Không giải mã được (khóa chủ đổi hoặc dòng hỏng) thì rơi về biến môi trường: key hỏng không được làm sập đăng nhập hay gửi email.
            value = (row is null ? null : _cipher.Decrypt(name, row.Ciphertext, row.Nonce)) ?? FromEnvironment(definition);
        }

        _cache.Set(CacheKey(name), value, Ttl);
        return value;
    }

    public async Task<IReadOnlyList<SecretStatus>> ListAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var rows = await db.SecretEntries.AsNoTracking().ToDictionaryAsync(s => s.Name, ct);
        return [.. BackendSecrets.All.Select(d => Describe(d, rows.GetValueOrDefault(d.Name)))];
    }

    private SecretStatus Describe(BackendSecrets.Definition definition, SecretEntry? row)
    {
        if (row is not null && _cipher.Decrypt(definition.Name, row.Ciphertext, row.Nonce) is not null)
        {
            return new SecretStatus(definition.Name, definition.Title, "db", true, row.Last4, row.UpdatedAt, definition.Multiline);
        }

        var env = FromEnvironment(definition);
        return env is null
            ? new SecretStatus(definition.Name, definition.Title, "none", false, null, null, definition.Multiline)
            : new SecretStatus(definition.Name, definition.Title, "env", true, Last4Of(env), null, definition.Multiline);
    }

    private static string Last4Of(string value) => value.Length <= 4 ? new string('*', value.Length) : value[^4..];

    public async Task<SecretStatus> SaveAsync(string name, string value, Guid adminId, CancellationToken ct = default)
    {
        var definition = BackendSecrets.Find(name) ?? throw new BusinessRuleException(UnknownCode, "Không có key này trong danh mục.");
        if (!_cipher.Available)
        {
            throw new BusinessRuleException(UnavailableCode, "Chưa cấu hình khóa mã hóa (SECRETS_MASTER_KEY của backend) nên chưa lưu được key ở đây.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > BackendSecrets.MaxLength)
        {
            throw new BusinessRuleException(InvalidCode, "Key không được để trống hoặc quá dài.");
        }

        if (definition.Validate(trimmed) is { } reason)
        {
            throw new BusinessRuleException(InvalidCode, reason);
        }

        var (cipher, nonce) = _cipher.Encrypt(name, trimmed);
        var now = DateTimeOffset.UtcNow;
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var row = await db.SecretEntries.FirstOrDefaultAsync(s => s.Name == name, ct);
        if (row is null)
        {
            row = new SecretEntry { Name = name, CreatedAt = now };
            db.SecretEntries.Add(row);
        }

        row.Ciphertext = cipher;
        row.Nonce = nonce;
        row.KeyVersion = 1;
        row.Last4 = Last4Of(trimmed);
        row.UpdatedBy = adminId;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        _cache.Remove(CacheKey(name));
        return Describe(definition, row);
    }

    public async Task<SecretStatus> DeleteAsync(string name, CancellationToken ct = default)
    {
        var definition = BackendSecrets.Find(name) ?? throw new BusinessRuleException(UnknownCode, "Không có key này trong danh mục.");
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FinMateDbContext>();
        var row = await db.SecretEntries.FirstOrDefaultAsync(s => s.Name == name, ct);
        if (row is not null)
        {
            db.SecretEntries.Remove(row);
            await db.SaveChangesAsync(ct);
        }

        _cache.Remove(CacheKey(name));
        return Describe(definition, null);
    }
}
