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
    gemini_model: str = "gemini-3.5-flash-lite"
    gemini_timeout_seconds: float = 5.0


settings = Settings()
