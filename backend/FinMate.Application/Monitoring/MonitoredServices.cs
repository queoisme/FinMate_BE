using FinMate.Domain.Enums;

namespace FinMate.Application.Monitoring;

/// <summary>Tên, ảnh hưởng và mức độ của từng dịch vụ. Nằm trong code vì đây là phán đoán về kiến trúc, không phải tham số vận hành.</summary>
public sealed record MonitoredService(string Key, string Name, string Impact, IncidentSeverity Severity);

public static class MonitoredServices
{
    public static readonly MonitoredService Redis = new("redis", "Redis", "Đăng nhập bằng mã OTP và các dữ liệu lưu tạm có thể lỗi.", IncidentSeverity.Critical);
    public static readonly MonitoredService AiService = new("ai_service", "AI Service", "Đọc thông báo ngân hàng, chat và phân loại giao dịch có thể không hoạt động.", IncidentSeverity.High);
    public static readonly MonitoredService OcrService = new("ocr_service", "OCR", "Khách không quét được hóa đơn và phải nhập tay.", IncidentSeverity.High);
    public static readonly MonitoredService SttService = new("stt_service", "Nhận dạng giọng nói", "Khách không nhập được giao dịch bằng giọng nói.", IncidentSeverity.High);

    public static readonly MonitoredService Vision = new("google_vision", "Google Vision", "Quét hóa đơn đang chuyển sang đọc cục bộ, chậm hơn và có thể kém chính xác hơn. Key có thể đã hết hạn mức hoặc không còn hiệu lực.", IncidentSeverity.High);
    public static readonly MonitoredService Gemini = new("gemini", "Gemini", "Phân loại món và chat đang dùng luật hoặc nhà cung cấp dự phòng.", IncidentSeverity.High);
    public static readonly MonitoredService GeminiBackup = new("gemini_backup", "Nhà cung cấp AI dự phòng", "Khi Gemini lỗi sẽ không còn đường dự phòng, hệ thống dùng luật.", IncidentSeverity.Normal);
    public static readonly MonitoredService Brevo = new("brevo", "Brevo", "Không gửi được email: mã OTP đăng nhập, đặt lại mật khẩu và cảnh báo sự cố.", IncidentSeverity.High);
    public static readonly MonitoredService Fcm = new("fcm", "Firebase", "Khách không nhận được thông báo đẩy.", IncidentSeverity.High);

    private static readonly Dictionary<string, MonitoredService> ByKey = new[] { Redis, AiService, OcrService, SttService, Vision, Gemini, GeminiBackup, Brevo, Fcm }.ToDictionary(s => s.Key);

    public static string ImpactOf(string serviceKey)
        => ByKey.TryGetValue(serviceKey, out var service) ? service.Impact : "Một phần hệ thống có thể hoạt động không ổn định.";
}
