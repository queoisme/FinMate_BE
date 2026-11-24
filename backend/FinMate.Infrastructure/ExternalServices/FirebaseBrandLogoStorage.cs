using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FinMate.Application.Common;
using FinMate.Application.Common.Exceptions;
using FinMate.Application.Common.Interfaces;

namespace FinMate.Infrastructure.ExternalServices;

/// <summary>
/// Đưa logo lên Firebase Storage bằng service account Firebase đang dùng cho FCM và bucket admin nhập ở trang Tích hợp. Mỗi lần tải lên là một tên file mới
/// (id ngẫu nhiên) nên app không giữ ảnh cũ trong cache; link có token tải về của Firebase nên không cần mở công khai cả bucket.
/// </summary>
public sealed class FirebaseBrandLogoStorage : IBrandLogoStorage
{
    public const string Scope = "https://www.googleapis.com/auth/devstorage.read_write";

    private readonly ISecretVault _vault;
    private readonly FcmApp _fcm;
    private readonly IHttpClientFactory _http;

    public FirebaseBrandLogoStorage(ISecretVault vault, FcmApp fcm, IHttpClientFactory http)
    {
        _vault = vault;
        _fcm = fcm;
        _http = http;
    }

    public async Task<string> UploadAsync(byte[] data, string contentType, string extension, CancellationToken ct = default)
    {
        var bucket = await _vault.GetAsync(BackendSecrets.FirebaseStorageBucket, ct);
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new BusinessRuleException("logo_storage_not_configured", "Chưa cấu hình Firebase Storage bucket. Thêm ở Cấu hình > Tích hợp bên thứ ba.");
        }

        var token = await _fcm.AccessTokenAsync(Scope, ct)
            ?? throw new BusinessRuleException("logo_storage_not_configured", "Chưa có Firebase service account hợp lệ. Thêm ở Cấu hình > Tích hợp bên thứ ba.");

        var objectName = $"brand/logo-{Guid.NewGuid():N}.{extension}";
        var downloadToken = Guid.NewGuid().ToString();
        var metadata = JsonSerializer.Serialize(new { name = objectName, contentType, cacheControl = "public, max-age=31536000", metadata = new Dictionary<string, string> { ["firebaseStorageDownloadTokens"] = downloadToken } });

        using var form = new MultipartContent("related");
        form.Add(new StringContent(metadata, Encoding.UTF8, "application/json"));
        var media = new ByteArrayContent(data);
        media.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(media);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://storage.googleapis.com/upload/storage/v1/b/{Uri.EscapeDataString(bucket)}/o?uploadType=multipart") { Content = form };
        request.Headers.Authorization = new("Bearer", token);
        HttpResponseMessage response;
        try
        {
            response = await _http.CreateClient("firebase-storage").SendAsync(request, ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new BusinessRuleException("logo_upload_failed", "Không gọi được Firebase Storage (lỗi mạng hoặc quá thời gian). Thử lại sau.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new BusinessRuleException("logo_upload_failed", (int)response.StatusCode switch
                {
                    404 => "Không tìm thấy bucket. Kiểm tra tên bucket ở trang Tích hợp.",
                    401 or 403 => "Service account chưa có quyền ghi vào bucket (cần Storage Object Admin).",
                    var code => $"Firebase Storage trả HTTP {code}.",
                });
            }
        }

        return $"https://firebasestorage.googleapis.com/v0/b/{Uri.EscapeDataString(bucket)}/o/{Uri.EscapeDataString(objectName)}?alt=media&token={downloadToken}";
    }
}
