"""Application settings loaded from environment variables."""

from typing import Literal

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    database_url: str
    redis_url: str
    internal_api_key: str
    model_registry_path: str = "/models"
    log_level: str = "INFO"

    # Ghi mọi thông báo đã xử lý (bản đã anonymize) vào ml_raw_samples để làm dữ liệu train.
    # Tắt được vì đây là thu thập dữ liệu người dùng — production thật cần cờ này gắn với
    # đồng ý của người dùng, không bật mặc định cho mọi triển khai.
    collect_raw_samples: bool = True

    # Nạp app/data/provider_patterns.json vào bảng kb_provider_patterns lúc khởi động, bỏ qua
    # pattern đã có. Cùng cách backend chạy ProviderConfigSeeder sau Database.Migrate().
    seed_provider_patterns: bool = True

    # Thêm thương hiệu/từ khóa mặc định trong code (app/bill/seed_defaults.py) vào kb_brands / kb_keywords lúc khởi động, chỉ thêm,
    # không đụng dòng đã có. Tắt thì chuỗi mới thêm vào code chỉ đến DB cũ qua script tay hoặc API quản trị.
    seed_bill_knowledge: bool = True

    # Ghi bản mặc định của các tài liệu cấu hình có seed=True (narrative_labels, notification_vocab) vào config_documents lúc khởi động khi DB chưa có
    # phiên bản nào; có rồi thì không đụng. Tắt thì tài liệu đó dùng bản trong image cho tới khi admin lưu bản đầu tiên.
    seed_config_documents: bool = True

    # OCR service riêng (PaddleOCR + VietOCR). Đặt cả URL lẫn khóa thì /ocr dùng nó; để trống thì /ocr vẫn dùng
    # Tesseract trong tiến trình này.
    ocr_service_url: str | None = None
    ocr_service_api_key: str | None = None
    ocr_service_timeout_seconds: float = 30.0

    # Khoản chi chắc chắn dưới mức này (đồng) được đánh dấu auto_save_ok; 0 = không bao giờ.
    chat_auto_save_below: int = 2_000_000

    # Dọn dữ liệu cá nhân quá hạn ngay trong tiến trình (app/services/retention.py): nhật ký phân tích, mẫu thô chưa nhãn, hàng đợi bill lạ.
    # Mỗi bản chạy AI Service tự dọn; tắt khi đã có việc dọn ngoài (cron + scripts/purge_*.py) để không dọn hai lần.
    retention_enabled: bool = True
    retention_days: int = 90
    retention_interval_hours: float = 24.0

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
    notification_ai_timeout_seconds: float = 8.0  # trần ngoài cùng của bước AI phân loại nội dung thông báo trong /analyze, phải lớn hơn chat_gemini_overall_seconds (7.5) để chuỗi Gemini tự thất bại và mở cầu dao trước khi wait_for hủy; quá hạn thì giữ kết quả luật ('other')
    # Dự phòng khi mọi model Gemini đều lỗi (OpenRouter). Để trống khóa thì tắt. Đo 2026-10-13: nemotron-3-super:free 6-16s, đúng 5-6/6; gemma-4 :free bị 429
    openrouter_api_key: str | None = None
    google_vision_api_key: str | None = None  # chỉ để hiện trạng thái và thử key: OCR Service là bên dùng, nó lấy key qua /internal/secrets

    # Khóa chủ AES-256 (32 byte, base64) mã hóa key của bên thứ ba lưu qua trang quản trị (secret_store). Để trống thì không lưu được key ở DB (key ở biến môi trường vẫn chạy).
    # Sinh một khóa: python -c "import os,base64;print(base64.urlsafe_b64encode(os.urandom(32)).decode())". Mất khóa này là mất mọi key đã lưu ở DB (nhập lại được).
    secrets_master_key: str | None = None
    openrouter_model: str = "nvidia/nemotron-3-super-120b-a12b:free"

    # Chọn cách quản lý tài chính (POST /finance-methods/recommend): client Gemini riêng (cầu dao riêng), dùng chung GEMINI_API_KEY; hết cách Gemini thì mới tới OpenRouter
    # (signals ẩn danh chỉ được gửi cho bên đã nêu trong câu chữ đồng ý; hết cả hai thì 503 và backend dùng rule). Người dùng đang chờ ở màn khảo sát nên trần chặt hơn hóa đơn.
    method_gemini_model: str = "gemini-3-flash-preview"
    method_gemini_fallback_models: str = "gemini-3.1-flash-lite"
    method_gemini_timeout_seconds: float = 10.0  # trần cho TỪNG model (đo 2026-10-27: gemini-3-flash-preview mất 6,5-16 s với 9 phương pháp)
    method_gemini_overall_seconds: float = 14.0  # trần cả chuỗi (gồm lần thử lại)
    # Dự phòng thứ hai (người dùng duyệt 2026-10-27): OpenRouter, model :free, dùng chung OPENROUTER_API_KEY; để trống METHOD_OPENROUTER_MODELS thì tắt.
    # Luôn gửi provider.data_collection = "deny" (xem app/services/method_backup.py): model :free không có bên nào đáp ứng thì bị bỏ qua (404).
    # Đo 2026-10-27 với deny: dots-3-note-preview 4/4 đúng, 3-4 s khi tắt suy luận; nemotron-3.5-super-vl không có endpoint; gemma-4 và xing4.0 bị 429.
    method_openrouter_models: str = "dots-studio/dots-3-note-preview:free"
    method_openrouter_timeout_seconds: float = 10.0  # trần cho TỪNG model dự phòng
    method_total_seconds: float = 24.0  # trần cả route (Gemini + dự phòng); backend chờ 30 giây

    # AI Financial Agent (kế hoạch docs/superpowers/plans/2026-11-07-ai-financial-agent.md). Dùng chung GEMINI_API_KEY / OPENROUTER_API_KEY; không có khóa của nhà cung cấp đã chọn thì agent tắt.
    # Dữ liệu người dùng thật chỉ được đi qua gói Gemini TRẢ PHÍ (gói miễn phí dùng dữ liệu để cải thiện sản phẩm của Google).
    seed_agent_scope: bool = True  # gieo cụm từ + câu từ chối mặc định của cổng phạm vi vào DB lúc khởi động (chỉ thêm, DB thắng)
    agent_provider: Literal["gemini", "openrouter"] = "gemini"
    agent_model: str = "gemini-3.1-flash-lite"  # mô hình của nhà cung cấp đã chọn; đổi mô hình = đổi biến này, chọn bằng bộ đánh giá agent_eval
    agent_timeout_seconds: float = 20.0  # trần cho MỘT lần gọi LLM của agent (từng pha mạng; thử lại 429/503 có trần riêng gấp đôi)
    agent_backup_model: str = ""  # mô hình OpenRouter dự phòng khi nhà cung cấp chính lỗi; TRỐNG = không có dự phòng (dữ liệu người dùng không tự đi tới bên chưa ai chọn)
    agent_max_output_tokens: int = 1024  # trần token đầu ra mỗi lần gọi LLM, để chi phí không vượt tầm ngân sách
    agent_eval_enabled: bool = False  # bật khi chạy bộ đánh giá (agent_eval): cho phép bỏ bộ kiểm chứng theo từng lời gọi (đánh giá cắt bỏ). Production luôn False
    agent_verifier_enabled: bool = True  # chỉ tắt khi chạy đánh giá cắt bỏ (agent_eval); production luôn bật
    agent_rag_enabled: bool = True  # tắt = RAG trả rỗng (đánh giá cắt bỏ)
    agent_embedding_model: str = "gemini-embedding-2"  # đã đối chiếu trang embeddings chính thức của Google (2026-11-07): tự chuẩn hóa vector khi cắt còn 768 chiều. Trống = RAG chưa tra được
    agent_embedding_dim: int = 768  # số chiều cột vector; đổi số này sau khi có bảng tri thức phải làm migration mới

    @property
    def agent_enabled(self) -> bool:
        """Agent chỉ chạy khi nhà cung cấp đã chọn có khóa (môi trường; khóa lưu ở secret_store do tầng gọi LLM tự tra)."""
        key = self.gemini_api_key if self.agent_provider == "gemini" else self.openrouter_api_key
        return bool(key)


settings = Settings()
