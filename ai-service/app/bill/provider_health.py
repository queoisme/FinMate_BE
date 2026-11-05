"""Sức khỏe nhà cung cấp AI trong tiến trình này, để backend đọc mỗi phút. Không lưu khóa, tên món hay phản hồi, chỉ mã lỗi cố định.

Mất khi khởi động lại, chấp nhận được: backend đọc lại ngay phút kế tiếp và lỗi thật sẽ lặp lại.
"""
from datetime import UTC, datetime


class ProviderHealth:
    """Đếm riêng theo từng client: chat chạy tốt không được xóa số lỗi của hóa đơn, vì chúng dùng các model khác nhau. Số báo ra là kênh tệ nhất."""

    def __init__(self) -> None:
        self._failures: dict[str, int] = {}
        self.last_error_code: str | None = None
        self.last_error_at: datetime | None = None
        self.last_success_at: datetime | None = None

    @property
    def consecutive_failures(self) -> int:
        return max(self._failures.values(), default=0)

    def success(self, channel: str = "default") -> None:
        self._failures[channel] = 0
        self.last_success_at = datetime.now(UTC)

    def failure(self, code: str, channel: str = "default") -> None:
        self._failures[channel] = self._failures.get(channel, 0) + 1
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
