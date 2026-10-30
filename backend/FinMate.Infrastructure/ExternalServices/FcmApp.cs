using System.Security.Cryptography;
using System.Text;
using FinMate.Application.Common;
using FinMate.Application.Common.Interfaces;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Giữ <see cref="FirebaseApp"/> khớp với credentials ĐANG HIỆU LỰC: kho key (hoặc <c>FCM_CREDENTIALS_JSON</c>) trước, <c>FCM_CREDENTIALS_PATH</c> sau. Đổi credentials trên trang quản trị thì lần gửi kế
/// tiếp dựng lại app, không cần khởi động lại. Credentials sai không ném ra caller: ghi một cảnh báo (chỉ tên kiểu lỗi) và coi như FCM chưa bật, vì push luôn nằm sau một giao dịch đã lưu.
/// </summary>
public sealed class FcmApp
{
    private readonly ISecretVault _vault;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FcmApp> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _fingerprint;

    public FcmApp(ISecretVault vault, IConfiguration configuration, ILogger<FcmApp> logger)
    {
        _vault = vault;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Đã có Firebase app dùng được với credentials hiện hành chưa (dựng lại nếu credentials vừa đổi).</summary>
    public async Task<bool> EnsureAsync(CancellationToken ct = default)
    {
        var json = await _vault.GetAsync(BackendSecrets.FcmCredentialsJson, ct);
        var path = _configuration["FCM_CREDENTIALS_PATH"];
        var fingerprint = !string.IsNullOrWhiteSpace(json)
            ? "json:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            : !string.IsNullOrWhiteSpace(path) ? "path:" + path : null;

        if (fingerprint == _fingerprint && (fingerprint is null || FirebaseApp.DefaultInstance is not null))
        {
            return fingerprint is not null;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (fingerprint == _fingerprint && (fingerprint is null || FirebaseApp.DefaultInstance is not null))
            {
                return fingerprint is not null;
            }

            FirebaseApp.DefaultInstance?.Delete();
            _fingerprint = null;
            if (fingerprint is null)
            {
                return false;
            }

            try
            {
                // CredentialFactory chỉ định thẳng ServiceAccountCredential: file credentials của Firebase luôn là service account, nên sai loại hỏng ngay ở đây.
                var account = !string.IsNullOrWhiteSpace(json)
                    ? CredentialFactory.FromJson<ServiceAccountCredential>(json)
                    : CredentialFactory.FromFile<ServiceAccountCredential>(path!);
                FirebaseApp.Create(new AppOptions { Credential = account.ToGoogleCredential() });
                _fingerprint = fingerprint;
                return true;
            }
            catch (Exception ex)
            {
                // Chỉ tên kiểu: thông điệp lỗi của thư viện có thể trích lại nội dung credentials.
                _logger.LogWarning("FCM credentials could not be loaded ({ErrorType}); push runs log-only until they are fixed.", ex.GetType().Name);
                _fingerprint = fingerprint;
                return false;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Xin một access token bằng credentials hiện hành, để biết chúng có thật sự dùng được không. Không gửi gì tới thiết bị nào.</summary>
    public async Task<bool> CanObtainTokenAsync(CancellationToken ct = default)
    {
        if (!await EnsureAsync(ct) || FirebaseApp.DefaultInstance?.Options.Credential is not { } credential)
        {
            return false;
        }

        var token = await credential.CreateScoped("https://www.googleapis.com/auth/firebase.messaging").UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
        return !string.IsNullOrEmpty(token);
    }
}
