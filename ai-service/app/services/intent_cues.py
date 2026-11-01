"""Nạp từ vựng, mẫu nhận dạng, ngưỡng và câu trả lời của lớp hiểu câu chat từ ``app/data/intent_cues.json`` (đọc ``_doc`` trong tệp trước khi sửa).

Code (``transaction_parser.py``) chỉ giữ CƠ CHẾ quyết định; kiến thức ngôn ngữ nằm ở tệp dữ liệu, cùng kiểu với ``dictionaries/*.json``.
Mẫu có ``on: folded`` khớp trên câu đã bỏ dấu (danh sách được bỏ dấu khi chèn); ``on: accented`` khớp trên câu có dấu, dùng khi bỏ dấu làm trùng nghĩa
(thưởng/thường, bán/bạn). Cú pháp ``<<tên>>`` chèn một danh sách (``lists``) hoặc một đoạn dùng lại (``fragments``).
Tệp hỏng hay tham chiếu sai thì báo lỗi to lúc khởi động: thiếu từ vựng một cách im lặng làm câu chat bị hiểu sai mà không có dấu hiệu nào.
"""
from __future__ import annotations

import copy
import json
import re
import unicodedata
from dataclasses import dataclass
from pathlib import Path

from app.services.vi_text import fold

DATA_PATH = Path(__file__).resolve().parents[1] / "data" / "intent_cues.json"
_REF = re.compile(r"<<([a-z_]+)>>")

# Nhóm đặt tên mà CODE đọc từ từng mẫu kỳ (transaction_parser): thiếu một nhóm thì chat sẽ văng IndexError lúc gặp câu đó, nên ``build`` từ chối ngay (cả bản mặc định lẫn bản admin).
# Đây là hợp đồng cấu trúc giữa regex và code, không phải từ vựng: từ ngữ vẫn tự do sửa trong từng nhóm.
PATTERN_GROUPS = {
    "period_month": {"day", "month", "year"},
    "period_year": {"year"},
    "period_day": {"day", "month", "year"},
    "period_day_only": {"day"},
    "period_range": {"d1", "m1", "d2", "m2", "year"},
    "period_last": {"days", "weeks", "months"},
    "period_quarter": {"quarter", "year", "previous", "current"},
    "period_day_part": {"today", "yesterday"},
}


@dataclass(frozen=True)
class Cues:
    patterns: dict[str, re.Pattern]
    phrases: dict[str, frozenset[str]]  # cả câu khớp nguyên cụm, đã bỏ dấu
    question_particles: tuple[str, ...]  # đã bỏ dấu, có khoảng trắng đầu: " khong"
    answer_starts: tuple[str, ...]
    thresholds: dict[str, float]
    replies: dict[str, str]
    lists: dict[str, tuple[str, ...]]  # nguyên văn như trong tệp (không bỏ dấu): nơi code cần danh sách từ riêng, không qua mẫu
    period_labels: tuple[tuple[str, str], ...]  # (khóa kỳ đã bỏ dấu, nhãn hiển thị) theo thứ tự thử khớp; khóa là định danh code dùng (period_range)
    needs_accent: dict[str, str]  # từ khóa đã bỏ dấu -> dạng có dấu mà câu gốc phải chứa thì từ khóa mới được nhận
    reply_intents: tuple[str, ...]  # tên ý định của Rasa mà code coi là lời đáp chứ không phải khoản ghi


def _expand(template: str, lists: dict[str, list[str]], fragments: dict[str, str], folded: bool, stack: tuple[str, ...] = ()) -> str:
    def replace(match: re.Match) -> str:
        key = match.group(1)
        if key in stack:
            raise ValueError(f"tham chiếu vòng <<{key}>>")
        if key in lists:
            return "|".join(fold(word) if folded else word for word in lists[key])
        if key in fragments:
            return _expand(fragments[key], lists, fragments, folded, stack + (key,))
        raise ValueError(f"tham chiếu <<{key}>> không tồn tại")

    return _REF.sub(replace, template)


def _text(value) -> str:
    if not isinstance(value, str):
        raise ValueError(f"{value!r} phải là chuỗi")
    return value


def _pair(entry) -> tuple[str, str]:
    key, label = entry
    return _text(key), _text(label)


def build(data: dict, source: str = "intent_cues") -> Cues:
    """Dựng bộ từ vựng từ một tài liệu JSON (tệp mặc định hoặc bản admin tải lên). Sai thì báo ValueError nói rõ sai ở đâu."""
    try:
        lists, fragments = data["lists"], data["fragments"]
        patterns: dict[str, re.Pattern] = {}
        for name, spec in data["patterns"].items():
            if spec["on"] not in ("folded", "accented"):
                raise ValueError(f"mẫu {name}: 'on' phải là folded hoặc accented")
            patterns[name] = re.compile(_expand(spec["regex"], lists, fragments, spec["on"] == "folded"))
            if missing := PATTERN_GROUPS.get(name, set()) - set(patterns[name].groupindex):
                raise ValueError(f"mẫu {name}: thiếu nhóm đặt tên {sorted(missing)} mà code đọc (giữ nguyên tên nhóm, chỉ sửa chữ bên trong)")
        forms = data["forms"]
        return Cues(
            patterns=patterns,
            phrases={name: frozenset(fold(phrase) for phrase in phrase_list) for name, phrase_list in data["phrases"].items()},
            question_particles=tuple(" " + fold(word) for word in forms["question_particles"]["words"]),
            answer_starts=tuple(fold(word) for word in forms["answer_starts"]["words"]),
            thresholds={name: spec["value"] for name, spec in data["thresholds"].items()},
            replies=dict(data["replies"]),
            lists={name: tuple(words) for name, words in lists.items()},
            period_labels=tuple(_pair(entry) for entry in forms["period_labels"]["pairs"]),
            needs_accent={key: unicodedata.normalize("NFC", _text(accented)) for key, accented in forms["needs_accent"]["map"].items()},
            reply_intents=tuple(_text(name) for name in forms["reply_intents"]["words"]),
        )
    except (ValueError, KeyError, TypeError, AttributeError, re.error) as error:
        raise ValueError(f"Tệp từ vựng {source} không đọc được: {error}") from error


def default_data(path: Path = DATA_PATH) -> dict:
    """Tài liệu mặc định (tệp trong image), là bản dùng khi DB không có bản nào đang chạy."""
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise ValueError(f"Tệp từ vựng {path.name} không đọc được: {error}") from error


def with_defaults(content: dict) -> dict:
    """Bản của một tài liệu ĐÃ LƯU với các khóa mà bản mặc định có nhưng nó chưa có (thêm sau lần lưu) được bổ sung từ bản mặc định; không sửa ``content``.

    Admin không xóa được khóa (xem ``config_documents``), nên khóa thiếu trong tài liệu đã lưu chỉ có thể do code thêm khóa SAU khi tài liệu được lưu. Không bổ sung thì mọi bản
    admin đã kích hoạt sẽ bị từ chối (``missing_keys``) và tiến trình lặng lẽ quay về hành vi cũ sau lần deploy thêm khóa. Chỉ dùng cho tài liệu đọc từ DB: bản nháp admin vừa tải
    lên vẫn phải đủ khóa."""
    merged = {**content}
    for section, defaults in default_data().items():
        if isinstance(defaults, dict) and isinstance(content.get(section), dict):
            merged[section] = {**content[section], **{key: copy.deepcopy(value) for key, value in defaults.items() if key not in content[section]}}
    return merged


def load(path: Path = DATA_PATH) -> Cues:
    return build(default_data(path), path.name)


CUES = load()  # bản mặc định, nạp một lần lúc import

_current: Cues = CUES
_listeners: list = []


def current() -> Cues:
    """Bản đang chạy: mặc định, hoặc bản admin vừa kích hoạt (xem ``apply``)."""
    return _current


def subscribe(callback) -> None:
    """Đăng ký hàm được gọi với bản mới mỗi khi ``apply`` đổi bản đang chạy (``transaction_parser`` gắn lại các tên của nó)."""
    _listeners.append(callback)


def apply(cues: Cues) -> None:
    """Đổi bản đang chạy trong tiến trình này và báo cho các bên đăng ký. Chỉ gọi với bản đã qua ``build`` (đã biên dịch được)."""
    global _current
    _current = cues
    for callback in _listeners:
        callback(cues)
