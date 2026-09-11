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


settings = Settings()
