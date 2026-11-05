"""Sức khỏe nhà cung cấp AI trong tiến trình này, để backend đọc mỗi phút. Không lưu khóa, tên món hay phản hồi, chỉ mã lỗi cố định.

Mất khi khởi động lại, chấp nhận được: backend đọc lại ngay phút kế tiếp và lỗi thật sẽ lặp lại.
"""
from datetime import UTC, datetime


class ProviderHealth:
    def __init__(self) -> None:
        self.consecutive_failures = 0
        self.last_error_code: str | None = None
        self.last_error_at: datetime | None = None
        self.last_success_at: datetime | None = None

    def success(self) -> None:
        self.consecutive_failures = 0
        self.last_success_at = datetime.now(UTC)

    def failure(self, code: str) -> None:
        self.consecutive_failures += 1
        self.last_error_code = code
        self.last_error_at = datetime.now(UTC)

    def snapshot(self, configured: bool) -> dict:
        return {
            "state": "enabled" if configured else "disabled",
            "consecutive_failures": self.consecutive_failures,
            "last_error_code": self.last_error_code,
            "last_error_at": self.last_error_at.isoformat() if self.last_error_at else None,
            "last_success_at": self.last_success_at.isoformat() if self.last_success_at else None,
        }


gemini_health = ProviderHealth()
backup_health = ProviderHealth()
