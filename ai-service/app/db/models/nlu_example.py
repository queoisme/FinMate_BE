"""``nlu_examples`` — câu mẫu của Rasa NLU (huấn luyện và đo), nguồn sự thật nằm trong DB chứ không trong tệp YAML.

Một câu chỉ có ĐÚNG MỘT dòng (``text_key`` duy nhất, bất kể tập): cùng một câu không thể vừa ở ``train`` vừa ở ``test``. Đó là chỗ rò rỉ dữ liệu
mà ``generate_nlu.py`` từng phải chặn bằng tay; ở đây DB chặn. Tập (``split``) gán tất định bằng băm của ``text_key`` khi nhập (xem ``scripts/nlu_examples.py``),
không random: một câu nằm mãi ở một tập, như ``ml_sample_splits``.
Không xóa dòng: câu sai thì ``is_active = false`` (còn lịch sử, và nhập lại cùng câu không sống lại ngầm)."""

import hashlib
import re
import unicodedata

from sqlalchemy import Boolean, CheckConstraint, Index, String, Text, UniqueConstraint, true
from sqlalchemy.orm import Mapped, mapped_column

from app.core.enums import LabelSource, SplitName
from app.db.base import Base, created_at, enum_check, uuid_pk

MAX_TEXT_CHARS = 200
ENTITIES = ("amount", "merchant", "date")  # khớp ``entities`` trong rasa/domain.yml
_ANNOTATION = re.compile(r"\[([^\[\]]+)\]\((\w+)\)")  # cú pháp thực thể của Rasa: "ăn phở [45k](amount)"


def plain_text(value: str) -> str:
    """Câu thường, bỏ chú thích thực thể: đây là thứ người dùng gõ, dùng để đo và để so trùng."""
    return _ANNOTATION.sub(r"\1", value)


def annotation_problem(value: str) -> str | None:
    """Chú thích thực thể sai cú pháp hay thực thể lạ thì Rasa huấn luyện sai im lặng, nên chặn từ lúc nhập."""
    for match in _ANNOTATION.finditer(value):
        if match.group(2) not in ENTITIES:
            return f"thực thể {match.group(2)!r} không có trong domain.yml {ENTITIES}"
    if "[" in plain_text(value) or "]" in plain_text(value):
        return "dấu [ ] lẻ hoặc chú thích thực thể sai cú pháp (đúng là [giá trị](amount))"
    return None


def text_key(value: str) -> str:
    """Khóa so trùng: NFC, chữ thường, gọn khoảng trắng, BỎ chú thích thực thể (cùng một câu dù gắn nhãn khác nhau). KHÔNG bỏ dấu: "bạn" và "ban" là hai câu huấn luyện khác nhau."""
    return " ".join(unicodedata.normalize("NFC", plain_text(value)).lower().split())


def default_split(key: str) -> str:
    """Chia tập TẤT ĐỊNH theo băm của ``text_key`` (1/3 vào test): một câu nằm mãi ở một tập, dù nhập từ tệp hay duyệt từ ứng viên."""
    return SplitName.TEST.value if int(hashlib.sha256(key.encode("utf-8")).hexdigest()[:8], 16) % 3 == 0 else SplitName.TRAIN.value


class NluExample(Base):
    __tablename__ = "nlu_examples"

    id: Mapped[uuid_pk]
    text: Mapped[str] = mapped_column(Text, nullable=False)
    text_key: Mapped[str] = mapped_column(String(400), nullable=False)
    intent: Mapped[str] = mapped_column(String(32), nullable=False)  # tên intent trong rasa/domain.yml; `rasa data validate` bắt tên lạ
    split: Mapped[str] = mapped_column(String(16), nullable=False)
    tag: Mapped[str | None] = mapped_column(String(40))  # nhóm lỗi để đo rò rỉ theo nhóm (ai_tech, invest_advice...), không ảnh hưởng huấn luyện
    source: Mapped[str] = mapped_column(String(32), nullable=False)
    is_active: Mapped[bool] = mapped_column(Boolean, nullable=False, server_default=true())  # không dùng text("true"): cột `text` bên dưới che hàm text() trong thân class
    created_at: Mapped[created_at]

    __table_args__ = (
        UniqueConstraint("text_key", name="uq_nlu_examples_text_key"),
        Index("idx_nlu_examples_split_intent", "split", "intent"),
        enum_check("split", SplitName, "chk_nlu_examples_split"),
        enum_check("source", LabelSource, "chk_nlu_examples_source"),
        CheckConstraint(f"char_length(text) <= {MAX_TEXT_CHARS}", name="chk_nlu_examples_text_len"),
    )


__all__ = ["NluExample", "text_key", "plain_text", "annotation_problem", "default_split", "ENTITIES", "MAX_TEXT_CHARS"]
