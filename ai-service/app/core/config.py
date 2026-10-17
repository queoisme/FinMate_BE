"""Application settings loaded from environment variables."""

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    database_url: str
    redis_url: str
    internal_api_key: str
    model_registry_path: str = "/models"
    log_level: str = "INFO"

    # Ghi mọi thông báo đã xử lý (bản đã anonymize) vào raw_samples để làm dữ liệu train.
    # Tắt được vì đây là thu thập dữ liệu người dùng — production thật cần cờ này gắn với
    # đồng ý của người dùng, không bật mặc định cho mọi triển khai.
    collect_raw_samples: bool = True

    # Nạp app/data/provider_patterns.json vào bảng provider_patterns lúc khởi động, bỏ qua
    # pattern đã có. Cùng cách backend chạy ProviderConfigSeeder sau Database.Migrate().
    seed_provider_patterns: bool = True

    # OCR service riêng (PaddleOCR + VietOCR). Đặt cả URL lẫn khóa thì /ocr dùng nó; để trống thì /ocr vẫn dùng
    # Tesseract trong tiến trình này.
    ocr_service_url: str | None = None
    ocr_service_api_key: str | None = None
    ocr_service_timeout_seconds: float = 30.0

    # Rasa (hiểu ý định câu chat). Để trống thì /chat/parse chỉ dùng luật.
    rasa_url: str | None = None
    # Khoản chi chắc chắn dưới mức này (đồng) được đánh dấu auto_save_ok; 0 = không bao giờ.
    chat_auto_save_below: int = 2_000_000

    # Gemini phân loại tên món hóa đơn khi luật/ML không chắc. Để trống khóa thì không gọi Gemini.
    gemini_api_key: str | None = None
    gemini_model: str = "gemini-3-flash-preview"  # đo 2026-10-13 (4 lượt, 6 món): 3,6-5,1s và đúng 6/6; 3.1-flash-lite mất 11-45s và đúng 4/6
    # Model dự phòng, cách nhau bằng dấu phẩy: thử lần lượt khi model chính quá tải (503) hay hết giờ
    gemini_fallback_models: str = "gemini-3.5-flash,gemini-3.1-flash-lite"  # 3.5-flash đúng 6/6 khi không 503; 3.7/3.8 hiện luôn 503
    gemini_timeout_seconds: float = 20.0

    # Chat là hội thoại nên cần phản hồi nhanh (hóa đơn thì không): client riêng, model nhanh đứng đầu, mô hình chính để dự phòng. Độ trễ từng model đổi mạnh theo
    # ngày (đo 13/10: 3.1-flash-lite 11-45 giây; 17/10: 1,3 giây, còn model chính trung vị 2,8 giây, đuôi tới 6 giây) nên mỗi model có trần riêng.
    chat_gemini_model: str = "gemini-3.1-flash-lite"
    chat_gemini_fallback_models: str = "gemini-3-flash-preview"
    chat_gemini_timeout_seconds: float = 3.5  # trần cho TỪNG model: model chậm bị bỏ để thử model kế
    chat_gemini_overall_seconds: float = 7.5  # trần cả chuỗi: quá thì chuỗi tự thất bại và cầu dao mở 30 giây (chat dùng luật, không ai phải chờ)
    chat_ai_timeout_seconds: float = 9.0  # trần ngoài cùng của bước AI trong chat, lớn hơn hai trần trên để chuỗi tự thất bại trước
    # Dự phòng khi mọi model Gemini đều lỗi (OpenRouter). Để trống khóa thì tắt. Đo 2026-10-13: nemotron-3-super:free 6-16s, đúng 5-6/6; gemma-4 :free bị 429
    openrouter_api_key: str | None = None
    openrouter_model: str = "nvidia/nemotron-3-super-120b-a12b:free"


settings = Settings()
