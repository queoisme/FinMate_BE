"""Gợi ý luật từ phản hồi người dùng, cho trang "Học AI".

Phản hồi sửa danh mục không lưu tên cửa hàng; tên nằm ở dòng ``ops_analyze_log`` mà phản hồi trỏ tới.
Gom theo (tên cửa hàng, danh mục người dùng sửa thành) và chỉ đưa ra nhóm có từ ``MIN_USERS`` người
khác nhau trở lên: một cửa hàng nhiều người cùng sửa là cửa hàng thật, còn một tên chỉ một người có
(thường là tên người nhận chuyển khoản) thì không bao giờ lộ ra trang quản trị.

Hành động "thêm vào từ điển" không ở đây: trang admin dùng luôn đường lưu nháp và kích hoạt của tài liệu
``merchant_lexicon`` (có lịch sử, chạy thử và khôi phục), nên không có đường ghi thứ hai cho cùng một dữ liệu.
"""

from __future__ import annotations

from dataclasses import dataclass

from app.pipeline.stages import categorizer

MIN_USERS = 2  # sàn riêng tư: dưới mức này không trả về, kể cả khi admin xin thấp hơn


def lexicon_keyword(merchant_name: str) -> str:
    """Từ khóa đúng dạng bộ phân loại dò: chữ thường, gọn khoảng trắng (``categorize_by_rules`` so khớp chuỗi con trên chữ thường)."""
    return " ".join(merchant_name.lower().split())


@dataclass(frozen=True)
class Suggestion:
    keyword: str
    corrected_category: str
    users: int
    times: int
    predicted_categories: list[str]
    current_category: str  # bộ luật đang chạy xếp tên này vào đâu
    covered: bool  # luật hiện tại đã xếp tên này đúng danh mục người dùng sửa

    def as_dict(self, last_at: str | None) -> dict:
        return {
            "keyword": self.keyword,
            "corrected_category": self.corrected_category,
            "users": self.users,
            "times": self.times,
            "predicted_categories": self.predicted_categories,
            "current_category": self.current_category,
            "covered": self.covered,
            "last_at": last_at,
        }


def build_suggestion(merchant_name: str, corrected: str, users: int, times: int, predicted: list[str | None],
                     lexicon_data: dict[str, list[str]] | None = None) -> Suggestion:
    keyword = lexicon_keyword(merchant_name)
    data = categorizer.current_lexicon_data() if lexicon_data is None else lexicon_data
    current = categorizer.categorize_by_rules(keyword).category_slug if lexicon_data is None else _match(keyword, data)
    # Đã ổn khi từ điển có đúng từ này, hoặc luật hiện tại đã xếp đúng (vd "highlands" đã bắt "highlands coffee").
    covered = current == corrected or keyword in {lexicon_keyword(word) for word in data.get(corrected, [])}
    return Suggestion(keyword, corrected, users, times, sorted({p for p in predicted if p}), current, covered)


def _match(keyword: str, data: dict[str, list[str]]) -> str:
    """Cùng luật với ``categorize_by_rules`` nhưng trên một từ điển truyền vào (dùng trong test)."""
    lowered = f" {keyword} "
    for word, slug in categorizer.build_lexicon(data):
        if word in lowered:
            return slug
    return "other"


def with_keyword(data: dict[str, list[str]], keyword: str, category: str) -> dict[str, list[str]]:
    """Từ điển mới: thêm ``keyword`` vào ``category`` và bỏ đúng từ khóa đó khỏi danh mục khác (một tên chỉ thuộc một danh mục).

    Trang admin làm đúng phép này ở trình duyệt trước khi lưu nháp; hàm ở đây là bản chuẩn để test giữ hai bên cùng nghĩa.
    """
    key = lexicon_keyword(keyword)
    result = {slug: [word for word in words if lexicon_keyword(word) != key] for slug, words in data.items()}
    result.setdefault(category, []).append(key)
    return result


if __name__ == "__main__":
    lex = {"food": ["highlands"], "shopping": ["winmart"]}
    s = build_suggestion("  GOGI   HOUSE ", "food", 3, 5, ["other", None, "other"], lex)
    assert (s.keyword, s.current_category, s.covered, s.predicted_categories) == ("gogi house", "other", False, ["other"])
    assert build_suggestion("Highlands", "food", 2, 2, [], lex).covered
    assert build_suggestion("HIGHLANDS COFFEE", "food", 2, 2, [], lex).covered  # luật "highlands" đã bắt đúng
    moved = with_keyword(lex, "WinMart", "food")
    assert moved == {"food": ["highlands", "winmart"], "shopping": []}
    print("ok")
