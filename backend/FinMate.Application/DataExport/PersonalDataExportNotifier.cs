using FinMate.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FinMate.Application.DataExport;

/// <summary>Hàng đợi job dựng gói. Bản thật dùng Hangfire; test thay bằng bản ghi lại để chạy job tất định.</summary>
public interface IPersonalDataExportQueue
{
    void Enqueue(Guid exportId);
}

/// <summary>
/// BR-EXP-10: báo gói đã xong (push + email) hoặc thất bại (push). Không link, không số liệu. Push đi qua <see cref="IPushNotificationService"/>
/// (tôn trọng công tắc push của user); email gửi kể cả khi tắt push, vì đây là việc user chủ động yêu cầu. Không bao giờ ném.
/// </summary>
public sealed class PersonalDataExportNotifier(IPushNotificationService push, IEmailSender email, IUserRepository users, ILogger<PersonalDataExportNotifier> logger)
{
    public const string ReadyAction = "data_export_ready";
    public const string FailedAction = "data_export_failed";
    public const string ReadyTitle = "Dữ liệu của bạn đã sẵn sàng";

    public async Task ReadyAsync(Guid userId, Guid exportId, CancellationToken ct = default)
    {
        await SafePushAsync(userId, ReadyTitle, "Mở FinMate, vào Cài đặt → Dữ liệu của tôi để tải về.", ReadyAction, exportId, ct);
        try
        {
            if (await users.GetByIdAsync(userId, ct) is { } user)
            {
                await email.SendAsync(user.Email, "FinMate — " + ReadyTitle, ReadyEmailBody, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Không gửi được email báo gói dữ liệu: {Error}", ex.GetType().Name);
        }
    }

    public Task FailedAsync(Guid userId, Guid exportId, CancellationToken ct = default)
        => SafePushAsync(userId, "Chưa tạo được gói dữ liệu", "Mở FinMate, vào Cài đặt → Dữ liệu của tôi để xem chi tiết.", FailedAction, exportId, ct);

    private const string ReadyEmailBody = """
        Chào bạn,

        Gói dữ liệu cá nhân bạn yêu cầu đã sẵn sàng.

        Để tải về, mở app FinMate, vào Cài đặt → Dữ liệu của tôi. Vì lý do bảo mật, email này không chứa đường dẫn tải: chỉ tải trong app sau khi đăng nhập.
        Gói được giữ trong thời gian ngắn rồi tự xoá.

        Nếu bạn không yêu cầu tải dữ liệu, hãy đổi mật khẩu ngay.
        """;

    private async Task SafePushAsync(Guid userId, string title, string body, string action, Guid exportId, CancellationToken ct)
    {
        try
        {
            await push.NotifyAsync(userId, title, body, new Dictionary<string, string> { ["action"] = action, ["exportId"] = exportId.ToString() }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Không gửi được push báo gói dữ liệu: {Error}", ex.GetType().Name);
        }
    }
}
