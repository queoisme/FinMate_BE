"""Tài liệu cấu hình JSON có phiên bản, sửa được từ trang quản trị (bảng ``config_documents``).

Luồng: tải bản đang chạy -> sửa -> tải lên thành BẢN NHÁP (được kiểm tra) -> xem trước tác động -> kích hoạt. Hoàn tác là kích hoạt lại một phiên bản cũ.
Mỗi lần lưu là một dòng mới, không bao giờ sửa dòng cũ.

Ba lớp bảo vệ để một lần sửa sai không làm hỏng dịch vụ hay làm hành vi đổi mà không ai biết:
  1. KIỂM TRA bản nháp: không được xóa khóa mà code đang dùng, kiểu dữ liệu phải đúng, regex phải biên dịch được.
  2. KHÓA LẠC QUAN: kích hoạt phải nói rõ đang thay bản số mấy; người khác đã đổi trong lúc đó thì bị từ chối thay vì ghi đè.
  3. CỔNG ĐO (nếu tài liệu có): từ vựng hiểu câu chat được chạy thử trên bộ câu có đáp án; kích hoạt làm tụt độ chính xác bị từ chối trừ khi ép (``force``).
Không có bản đang chạy trong DB thì dùng bản mặc định (tệp trong image). Không log nội dung tài liệu."""
from __future__ import annotations

import json
import pathlib
import re
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime

from sqlalchemy import func, select
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from app.bill import lexicon
from app.bill.rules_admin import RuleError
from app.db.models.config_document import ConfigDocument
from app.core.category_map import INCOME_SLUG, LOAN_SLUG, SLUG_BY_LOCAL_CATEGORY
from app.core.enums import CATEGORY_SLUGS
from app.pipeline.stages import categorizer
from app.services import intent_cues, vocab
from app.services.vi_text import fold

MAX_DOCUMENT_BYTES = 1_000_000
MAX_DIFF_ITEMS = 50
MAX_PREVIEW_CHANGES = 30
EVAL_DIR = pathlib.Path(__file__).resolve().parents[2] / "tests" / "fixtures" / "eval"
_SAME = {"needs_amount": "add_expense"}  # luật hỏi lại số tiền vẫn là một khoản chi (cùng cách scripts/audit_overrides.py)
APPLIED: dict[str, int] = {}  # tài liệu -> phiên bản đã áp dụng trong TIẾN TRÌNH NÀY (0 hoặc vắng = bản mặc định); activate và config_runtime cùng cập nhật


def _invalid(code: str, message: str) -> RuleError:
    return RuleError(422, code, message)


def _conflict(code: str, message: str) -> RuleError:
    return RuleError(409, code, message)


# ------------------------------------------------------------------ kiểm tra từng loại tài liệu

def _check_json_size(content: object) -> None:
    if len(json.dumps(content, ensure_ascii=False).encode("utf-8")) > MAX_DOCUMENT_BYTES:
        raise _invalid("too_large", f"Tài liệu quá lớn (tối đa {MAX_DOCUMENT_BYTES // 1000} KB).")


def _validate_intent_cues(content: dict) -> None:
    default = intent_cues.default_data()
    sections = ("lists", "fragments", "patterns", "phrases", "forms", "thresholds", "replies")
    for section in sections:
        if not isinstance(content.get(section), dict):
            raise _invalid("invalid_section", f"Mục '{section}' phải là một đối tượng JSON.")
    for section in sections:
        missing = sorted(set(default[section]) - set(content[section]))
        if missing:
            raise _invalid("missing_keys", f"Thiếu khóa mà code đang dùng ở '{section}': {missing[:10]}. Được thêm khóa mới, không được xóa khóa có sẵn.")
    for name, spec in content["thresholds"].items():
        value = spec.get("value") if isinstance(spec, dict) else None
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not 0 <= value <= 1:
            raise _invalid("invalid_threshold", f"Ngưỡng '{name}' phải là số trong khoảng 0 đến 1 (độ chắc của Rasa).")
    for name, text in content["replies"].items():
        if not isinstance(text, str) or not text.strip():
            raise _invalid("invalid_reply", f"Câu trả lời '{name}' phải là chuỗi không rỗng.")
    try:
        built = intent_cues.build(content, "bản nháp")  # biên dịch mọi regex, kiểm tra tham chiếu <<tên>>
    except ValueError as error:
        raise _invalid("invalid_document", str(error)) from error
    keys, expected = [key for key, _ in built.period_labels], [key for key, _ in intent_cues.CUES.period_labels]
    if sorted(keys) != sorted(expected):
        raise _invalid("period_keys_changed", f"Khóa kỳ ở 'forms/period_labels' phải giữ đúng {expected} (code dùng chúng để tính khoảng ngày); chỉ được sửa nhãn hiển thị và thứ tự.")


_VOCAB_SHAPES = {
    "tuple": lambda v: isinstance(v, list) and all(isinstance(x, str) for x in v),
    "set": lambda v: isinstance(v, list) and all(isinstance(x, str) for x in v),
    "list": lambda v: isinstance(v, list) and all(isinstance(x, str) for x in v),
    "tuple2": lambda v: isinstance(v, list) and all(isinstance(x, list) and all(isinstance(y, str) for y in x) for x in v),
    "dict": lambda v: isinstance(v, dict) and all(isinstance(k, str) and isinstance(x, (str, int, float)) and not isinstance(x, bool) for k, x in v.items()),
}


def _validate_parsing_vocab(content: dict) -> None:
    default = lexicon.load("parsing_vocab.json")
    missing = sorted(set(default) - set(content))
    if missing:
        raise _invalid("missing_keys", f"Thiếu bảng mà code đang dùng: {missing[:10]}. Được thêm mục vào bảng, không được xóa bảng có sẵn.")
    for key, entry in content.items():
        if key not in default:
            raise _invalid("unknown_table", f"Bảng '{key}' không có trong code nên sẽ không được dùng; thêm bảng mới cần sửa code.")
        if not isinstance(entry, dict) or set(entry) != {"type", "doc", "value"}:
            raise _invalid("invalid_entry", f"Bảng '{key}' phải có đúng các trường type, doc, value.")
        if entry["type"] != default[key]["type"]:
            raise _invalid("type_changed", f"Bảng '{key}': kiểu phải giữ là '{default[key]['type']}' vì code dựa vào kiểu đó.")
        if not _VOCAB_SHAPES[entry["type"]](entry["value"]) or not entry["value"]:
            raise _invalid("invalid_value", f"Bảng '{key}': giá trị phải đúng dạng của kiểu '{entry['type']}' và không rỗng.")
        if not isinstance(entry["doc"], str):
            raise _invalid("invalid_doc", f"Bảng '{key}': 'doc' phải là chuỗi.")


def _same_shape(default, content, path: str) -> None:
    """Cùng cấu trúc với bản mặc định: không thiếu khóa, không thêm khóa lạ (code không đọc), cùng kiểu từng giá trị, mảng không rỗng nếu mặc định không rỗng."""
    if isinstance(default, dict):
        if not isinstance(content, dict):
            raise _invalid("type_changed", f"'{path}' phải là một đối tượng JSON.")
        missing, unknown = sorted(set(default) - set(content)), sorted(set(content) - set(default))
        if missing:
            raise _invalid("missing_keys", f"Thiếu khóa mà code đang dùng ở '{path}': {missing[:10]}. Được sửa giá trị, không được xóa khóa.")
        if unknown:
            raise _invalid("unknown_key", f"Khóa {unknown[:10]} ở '{path}' không có trong bản mặc định nên code sẽ không đọc; thêm khóa mới cần sửa code.")
        for key in default:
            _same_shape(default[key], content[key], f"{path}/{key}")
    elif isinstance(default, list):
        if not isinstance(content, list) or (default and not content):
            raise _invalid("invalid_value", f"'{path}' phải là mảng{' không rỗng' if default else ''}.")
        for index, item in enumerate(content):
            _same_shape(default[0] if default else item, item, f"{path}[{index}]")  # mọi phần tử cùng cấu trúc với phần tử đầu của bản mặc định
    else:
        same = isinstance(content, type(default)) and not (isinstance(content, bool) != isinstance(default, bool))
        if not same:
            raise _invalid("type_changed", f"'{path}' phải giữ kiểu {type(default).__name__}.")


def _validate_app_screens(content: dict) -> None:
    default = {k: v for k, v in lexicon.load("app_screens.json").items() if not k.startswith("_")}
    body = {k: v for k, v in content.items() if not k.startswith("_")}
    _same_shape(default, body, "app_screens")
    groups = set(lexicon.load("category_groups.json")["groups"])
    phrase_suffixes = ("_phrase", "_prefix", "_label", "_phrases", "_prefixes", "_tokens", "_words", "skip_lines", "match", "_word")

    def walk(node, path: tuple) -> None:
        if isinstance(node, dict):
            for key, value in node.items():
                walk(value, path + (key,))
            return
        if isinstance(node, list):
            for index, value in enumerate(node):
                walk(value, path + (index,))
            return
        key = next(part for part in reversed(path) if isinstance(part, str))
        where = "/".join(str(part) for part in path)
        if key == "doc":
            if not isinstance(node, str) or not node.strip():
                raise _invalid("invalid_doc", f"'{where}': 'doc' (lý do/ý nghĩa) phải là chuỗi không rỗng.")
        elif key.endswith("_regex") and isinstance(node, str):
            try:
                compiled = re.compile(node)
            except re.error as error:
                raise _invalid("invalid_regex", f"'{where}' không biên dịch được: {error}") from error
            if key in {"total_regex", "dish_total_regex", "paid_regex", "merchant_regex"} and compiled.groups < 1:
                raise _invalid("invalid_regex", f"'{where}' phải có nhóm bắt giá trị (...).")
        elif isinstance(node, str) and key.endswith(phrase_suffixes):
            if node != node.lower() or node != fold(node) or not node.strip():
                raise _invalid("invalid_phrase", f"'{where}': '{node}' phải viết thường, bỏ dấu (code so khớp trên dòng đã bỏ dấu nên chữ có dấu không bao giờ khớp).")
        elif key in {"category", "housing_category", "default_category"}:
            if node not in groups:
                raise _invalid("invalid_category", f"'{where}': '{node}' không thuộc 9 nhóm hóa đơn {sorted(groups)}.")
        elif key.endswith(("_span", "_window", "_window_lines")):
            if isinstance(node, bool) or not isinstance(node, int) or not 1 <= node <= 30:
                raise _invalid("invalid_value", f"'{where}': số dòng phải là số nguyên từ 1 đến 30.")

    walk(body, ())
    for screen, low, high in (("cinema_ticket", "amount_min", "amount_max"), ("paper_bus_ticket", "price_min", "price_max")):
        if not 0 < body[screen][low] < body[screen][high]:
            raise _invalid("invalid_value", f"'{screen}': {low} phải dương và nhỏ hơn {high}.")


def _validate_thresholds(content: dict) -> None:
    default = {k: v for k, v in lexicon.load("thresholds.json").items() if not k.startswith("_")}
    entries = {k: v for k, v in content.items() if not k.startswith("_")}
    missing = sorted(set(default) - set(entries))
    if missing:
        raise _invalid("missing_keys", f"Thiếu ngưỡng mà code đang dùng: {missing[:10]}.")
    for key, entry in entries.items():
        if key not in default:
            raise _invalid("unknown_threshold", f"Ngưỡng '{key}' không có trong code nên sẽ không được dùng.")
        if not isinstance(entry, dict) or set(entry) != {"value", "doc", "samples", "source"}:
            raise _invalid("invalid_entry", f"Ngưỡng '{key}' phải có đúng các trường value, doc, samples, source.")
        value, base = entry["value"], default[key]["value"]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or value < 0:
            raise _invalid("invalid_value", f"Ngưỡng '{key}' phải là số không âm.")
        if base <= 1 and value > 1:
            raise _invalid("invalid_value", f"Ngưỡng '{key}' là tỉ lệ/độ chắc nên phải trong khoảng 0 đến 1.")
        if entry["samples"] is not None and (not isinstance(entry["samples"], int) or isinstance(entry["samples"], bool) or entry["samples"] < 1):
            raise _invalid("invalid_samples", f"Ngưỡng '{key}': 'samples' là số mẫu đã dùng để chỉnh (số nguyên >= 1) hoặc null nếu chưa ghi nguồn đo.")
        if not isinstance(entry["source"], str) or not entry["source"].strip():
            raise _invalid("invalid_source", f"Ngưỡng '{key}': phải ghi nguồn của giá trị ('chưa ghi nguồn đo' cũng là một câu trả lời trung thực).")


MAX_KEYWORDS_PER_GROUP = 2000
MAX_KEYWORD_CHARS = 60


def _validate_chat_dictionary(content: dict) -> None:
    default = vocab.default_data()
    missing, unknown = sorted(set(default) - set(content)), sorted(set(content) - set(default))
    if missing:
        raise _invalid("missing_keys", f"Thiếu nhóm mà code đang dùng: {missing[:10]}. Được thêm từ khóa vào nhóm, không được xóa nhóm có sẵn.")
    if unknown:
        raise _invalid("unknown_group", f"Nhóm {unknown[:10]} không có trong code nên sẽ không được dùng; thêm nhóm mới cần sửa code và lược đồ danh mục.")
    seen: dict[str, str] = {}
    for name, entry in content.items():
        if not isinstance(entry, dict) or not {"kind", "label", "keywords"} <= set(entry) <= {"kind", "label", "keywords", "weak_keywords"}:
            raise _invalid("invalid_entry", f"Nhóm '{name}' phải có các trường kind, label, keywords (và tùy chọn weak_keywords: từ khóa chỉ quyết khi không có từ khóa thường nào khớp).")
        if entry["kind"] != default[name]["kind"]:
            raise _invalid("type_changed", f"Nhóm '{name}': kind phải giữ là '{default[name]['kind']}' vì code dựa vào đó.")
        if not isinstance(entry["label"], str) or not entry["label"].strip():
            raise _invalid("invalid_label", f"Nhóm '{name}': 'label' (tên hiển thị) phải là chuỗi không rỗng.")
        words, weak = entry["keywords"], entry.get("weak_keywords", [])
        for field, listed in (("keywords", words), ("weak_keywords", weak)):
            if not isinstance(listed, list) or not all(isinstance(word, str) for word in listed):
                raise _invalid("invalid_keywords", f"Nhóm '{name}': '{field}' phải là mảng chuỗi.")
        if len(words) + len(weak) > MAX_KEYWORDS_PER_GROUP:
            raise _invalid("too_many_keywords", f"Nhóm '{name}': tối đa {MAX_KEYWORDS_PER_GROUP} từ khóa (kể cả từ khóa yếu).")
        for word in [*words, *weak]:
            if word != " ".join(fold(word).split()) or not 2 <= len(word) <= MAX_KEYWORD_CHARS:
                raise _invalid("invalid_keyword", f"Nhóm '{name}', từ '{word[:MAX_KEYWORD_CHARS]}': phải viết thường, bỏ dấu, gọn khoảng trắng, dài {2}-{MAX_KEYWORD_CHARS} ký tự "
                                                  "(câu chat được bỏ dấu trước khi so khớp nên từ có dấu không bao giờ khớp).")
            if word in seen:
                raise _invalid("duplicate_keyword", f"Từ '{word}' có ở cả nhóm '{seen[word]}' và '{name}': một từ chỉ được thuộc một nhóm.")
            seen[word] = name
    try:
        vocab.build(content, "bản nháp")
    except ValueError as error:
        raise _invalid("invalid_document", str(error)) from error


def _validate_merchant_lexicon(content: dict) -> None:
    default = categorizer.default_lexicon_data()
    missing, unknown = sorted(set(default) - set(content)), sorted(set(content) - set(default))
    if missing:
        raise _invalid("missing_keys", f"Thiếu danh mục mà code đang dùng: {missing[:10]}. Được thêm từ khóa vào danh mục, không được xóa danh mục có sẵn.")
    if unknown:
        raise _invalid("unknown_group", f"Danh mục {unknown[:10]} không có trong bản mặc định nên sẽ không được dùng; slug phải là slug hệ thống của backend.")
    seen: dict[str, str] = {}
    for slug, words in content.items():
        if slug not in CATEGORY_SLUGS:
            raise _invalid("unknown_group", f"'{slug}' không phải slug hệ thống của backend: giao dịch sẽ mất danh mục một cách im lặng.")
        if not isinstance(words, list) or not all(isinstance(word, str) for word in words):
            raise _invalid("invalid_keywords", f"Danh mục '{slug}': phải là mảng chuỗi.")
        if len(words) > MAX_KEYWORDS_PER_GROUP:
            raise _invalid("too_many_keywords", f"Danh mục '{slug}': tối đa {MAX_KEYWORDS_PER_GROUP} từ khóa.")
        for word in words:
            if word != word.lower() or not 2 <= len(word.strip()) <= MAX_KEYWORD_CHARS:
                raise _invalid("invalid_keyword", f"Danh mục '{slug}', từ '{word[:MAX_KEYWORD_CHARS]}': phải viết thường và dài {2}-{MAX_KEYWORD_CHARS} ký tự "
                                                  "(khoảng trắng cuối như 'pho ' được giữ vì nó là ranh giới từ). Khớp trên chữ thường nguyên văn nên có thể thêm cả dạng có dấu lẫn không dấu.")
            if word in seen:
                raise _invalid("duplicate_keyword", f"Từ '{word}' có ở cả '{seen[word]}' và '{slug}': một từ chỉ được thuộc một danh mục (khớp từ dài trước, trùng thì kết quả bất định).")
            seen[word] = slug


def _lexicon_cases() -> list[tuple[str, str, str]]:
    """(đường đi, câu/tên món, danh mục đáp án) từ bộ tên món và bộ cụm từ nhất quán. Từ điển tên cửa hàng đi vào bộ phân loại chung (``resolve``) nên ảnh hưởng cả hai."""
    cases = [("merchant", case["text"], case["expect"]) for case in _load_cases("merchant_cases.jsonl")]  # đường trực tiếp của thông báo ngân hàng
    cases += [("item", case["name"], case["expect"]) for case in _load_cases("item_cases.jsonl")]
    return cases + [("chat", case["phrase"], case["expect"]) for case in _load_cases("consistency_cases.jsonl")]


def _run_lexicon_cases(cases: list[tuple[str, str, str]]) -> dict[tuple[str, str], str]:
    from app.services.category_resolver import resolve
    from app.services.transaction_parser import VN, parse_chat

    now = datetime(2026, 10, 21, 10, 0, tzinfo=VN)
    out = {}
    for way, text, _ in cases:
        if way == "merchant":
            slug = categorizer.categorize_by_rules(text).category_slug
        elif way == "item":
            slug = resolve(text).slug
        else:
            entry = parse_chat(text + " 50k", None, now=now).entry
            slug = _shape(entry)[2] if entry else None
        out[(way, text)] = slug or "unknown"
    return out


def _preview_merchant_lexicon(content: dict) -> dict:
    """Chạy bộ tên món/cụm từ có đáp án với từ điển đang chạy và với bản nháp (đồng bộ, luôn trả lại bản đang chạy)."""
    cases = _lexicon_cases()
    if not cases:
        return {"available": False, "reason": "Không thấy bộ câu thử trong image (tests/fixtures/eval)."}
    active = categorizer.current_lexicon_data()
    before = _run_lexicon_cases(cases)
    categorizer.apply_lexicon(content)
    try:
        after = _run_lexicon_cases(cases)
    finally:
        categorizer.apply_lexicon(active)
    right = lambda got: sum(got[(way, text)] == expected for way, text, expected in cases)
    changed = [{"way": way, "text": text, "expected": expected, "before": before[(way, text)], "after": after[(way, text)]}
               for way, text, expected in cases if before[(way, text)] != after[(way, text)]]
    keyword_changes = {slug: {"added": sorted(set(content[slug]) - set(active[slug]))[:MAX_DIFF_ITEMS], "removed": sorted(set(active[slug]) - set(content[slug]))[:MAX_DIFF_ITEMS]}
                       for slug in sorted(set(active) & set(content)) if set(active[slug]) != set(content[slug])}
    return {"available": True, "cases": len(cases), "correct_before": right(before), "correct_after": right(after), "delta": right(after) - right(before),
            "changed": changed[:MAX_PREVIEW_CHANGES], "changed_total": len(changed), "keyword_changes": keyword_changes,
            "note": "Đo trên tên cửa hàng của thông báo ngân hàng (đường trực tiếp), tên món và cụm từ chat (qua bộ phân loại chung). Bộ tên cửa hàng chỉ canh việc sửa không làm hỏng "
                    "những cửa hàng đã biết; nó không đo khả năng nhận ra cửa hàng mới, nên vẫn cần đọc danh sách từ khóa đổi."}


def _keyword_changes(old: dict, new: dict) -> dict[str, dict[str, list[str]]]:
    """Từ khóa thêm / bớt theo từng nhóm: người duyệt thấy đúng từ nào vừa đổi, không phải đọc cả tài liệu."""
    out = {}
    for name in sorted(set(old) & set(new)):
        before = set(old[name]["keywords"]) | set(old[name].get("weak_keywords", ()))  # từ yếu vẫn là từ khóa của nhóm: chuyển qua lại không phải thêm/bớt
        after = set(new[name]["keywords"]) | set(new[name].get("weak_keywords", ()))
        if before != after:
            out[name] = {"added": sorted(after - before)[:MAX_DIFF_ITEMS], "removed": sorted(before - after)[:MAX_DIFF_ITEMS]}
    return out


# ------------------------------------------------------------------ xem trước (cổng đo)

def _eval_cases() -> list[tuple[str, str]]:
    cases: list[tuple[str, str]] = []
    for name in ("heldout_intent.jsonl", "chat_cases.jsonl"):
        path = EVAL_DIR / name
        if path.is_file():
            for line in path.read_text(encoding="utf-8").splitlines():
                if line.strip() and "intent" in (row := json.loads(line)):
                    cases.append((row["text"], row["intent"]))
    return cases


def _run_cases(cases: list[tuple[str, str]]) -> dict[str, str]:
    from app.services.transaction_parser import VN, parse_chat

    now = datetime(2026, 10, 21, 10, 0, tzinfo=VN)
    out = {}
    for text, _ in cases:
        got = parse_chat(text, None, now=now).intent
        out[text] = _SAME.get(got, got)
    return out


def _preview_intent_cues(content: dict) -> dict:
    """Chạy bộ câu có đáp án với bản đang chạy và với bản nháp (luật thuần, không Rasa). Đồng bộ, không await: không tiến trình nào chen vào lúc đổi tạm."""
    cases = _eval_cases()
    if not cases:
        return {"available": False, "reason": "Không thấy bộ câu thử trong image (tests/fixtures/eval)."}
    candidate, active = intent_cues.build(content, "bản nháp"), intent_cues.current()
    before = _run_cases(cases)
    intent_cues.apply(candidate)
    try:
        after = _run_cases(cases)
    finally:
        intent_cues.apply(active)
    right = lambda got: sum(got[text] == expected for text, expected in cases)
    changed = [{"text": text, "expected": expected, "before": before[text], "after": after[text]} for text, expected in cases if before[text] != after[text]]
    return {"available": True, "cases": len(cases), "correct_before": right(before), "correct_after": right(after),
            "delta": right(after) - right(before), "changed": changed[:MAX_PREVIEW_CHANGES], "changed_total": len(changed),
            "note": "Luật thuần (không Rasa) trên bộ câu có đáp án; với Rasa kết quả có thể khác."}


def _load_cases(name: str) -> list[dict]:
    path = EVAL_DIR / name
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()] if path.is_file() else []


def _dictionary_cases() -> list[tuple[str, list[tuple]]]:
    """(câu, [(loại, số tiền, danh mục)]) từ bộ câu chat có đáp án và bộ cụm từ nhất quán (chat nhận cụm + " 50k"). Nhãn đáp án lấy từ tệp, không sửa theo kết quả."""
    cases = [(case["text"], [(e["kind"], e["amount"], e["category"]) for e in case["expect"]]) for case in _load_cases("chat_cases.jsonl") if "expect" in case]
    cases += [(case["phrase"] + " 50k", [("expense", 50000, None if case["expect"] == "unknown" else case["expect"])]) for case in _load_cases("consistency_cases.jsonl")]
    return cases


def _shape(entry) -> tuple:
    if entry.kind == "income":
        return (entry.kind, entry.amount, INCOME_SLUG)
    if entry.kind == "transfer" or entry.category is None:
        return (entry.kind, entry.amount, None)
    slug = SLUG_BY_LOCAL_CATEGORY.get(entry.category, entry.category)
    return (entry.kind, entry.amount, slug if slug in CATEGORY_SLUGS or slug == LOAN_SLUG else None)


def _run_dictionary_cases(cases: list[tuple[str, list[tuple]]]) -> dict[str, list[tuple]]:
    from app.services.transaction_parser import VN, parse_chat

    now = datetime(2026, 10, 21, 10, 0, tzinfo=VN)
    out = {}
    for text, _ in cases:
        parsed = parse_chat(text, None, now=now)
        entries = list(getattr(parsed, "entries", None) or ([parsed.entry] if parsed.entry else []))
        out[text] = [_shape(entry) for entry in entries]
    return out


def _preview_chat_dictionary(content: dict) -> dict:
    """Chạy bộ câu có đáp án với từ điển đang chạy và với bản nháp (luật thuần, đồng bộ: không tiến trình nào chen vào lúc đổi tạm, và luôn trả lại bản đang chạy)."""
    cases = _dictionary_cases()
    if not cases:
        return {"available": False, "reason": "Không thấy bộ câu thử trong image (tests/fixtures/eval)."}
    active = vocab.current_data()
    before = _run_dictionary_cases(cases)
    vocab.apply(content)
    try:
        after = _run_dictionary_cases(cases)
    finally:
        vocab.apply(active)
    right = lambda got: sum(got[text] == expected for text, expected in cases)
    changed = [{"text": text, "expected": expected, "before": before[text], "after": after[text]} for text, expected in cases if before[text] != after[text]]
    return {"available": True, "cases": len(cases), "correct_before": right(before), "correct_after": right(after), "delta": right(after) - right(before),
            "changed": changed[:MAX_PREVIEW_CHANGES], "changed_total": len(changed), "keyword_changes": _keyword_changes(active, content),
            "note": "Luật thuần (không Rasa) trên bộ câu chat và cụm từ có đáp án: chỉ đo phần danh mục/số tiền mà từ điển ảnh hưởng."}


# ------------------------------------------------------------------ danh sách tài liệu được phép sửa

@dataclass(frozen=True)
class Spec:
    name: str
    title: str
    consumers: tuple[str, ...]
    default: Callable[[], dict]
    validate: Callable[[dict], None]
    apply: Callable[[dict], None] | None  # áp dụng vào TIẾN TRÌNH NÀY (ai-service); dịch vụ khác tự nạp từ DB
    preview: Callable[[dict], dict] | None
    upgrade: Callable[[dict], dict] | None = None  # bổ sung khóa mà code thêm SAU khi tài liệu được lưu vào DB; chỉ dùng cho nội dung đọc từ DB, không dùng cho bản nháp vừa tải lên


def stored_content(spec: Spec, content: dict) -> dict:
    """Nội dung một tài liệu đã lưu trong DB, đã bổ sung khóa mặc định mới (nếu tài liệu loại này có ``upgrade``)."""
    return spec.upgrade(content) if spec.upgrade else content


SPECS: dict[str, Spec] = {
    "intent_cues": Spec("intent_cues", "Từ vựng hiểu câu chat", ("ai-service",), intent_cues.default_data, _validate_intent_cues,
                        lambda content: intent_cues.apply(intent_cues.build(content, "DB")), _preview_intent_cues, intent_cues.with_defaults),
    "chat_dictionary": Spec("chat_dictionary", "Từ điển phân loại câu chat (từ khóa theo danh mục)", ("ai-service",), vocab.default_data, _validate_chat_dictionary,
                            vocab.apply, _preview_chat_dictionary),
    "merchant_lexicon": Spec("merchant_lexicon", "Từ điển tên cửa hàng (thông báo ngân hàng và bộ phân loại chung)", ("ai-service",), categorizer.default_lexicon_data,
                             _validate_merchant_lexicon, categorizer.apply_lexicon, _preview_merchant_lexicon),
    "parsing_vocab": Spec("parsing_vocab", "Từ vựng đọc hóa đơn (OCR)", ("ocr-service",), lambda: lexicon.load("parsing_vocab.json"), _validate_parsing_vocab, None, None),
    "app_screens": Spec("app_screens", "Chữ nhận dạng màn hình app (OCR)", ("ocr-service",), lambda: lexicon.load("app_screens.json"), _validate_app_screens, None, None),
    "thresholds": Spec("thresholds", "Ngưỡng đọc hóa đơn (OCR)", ("ocr-service",), lambda: lexicon.load("thresholds.json"), _validate_thresholds, None, None),
}


def spec_of(name: str) -> Spec:
    if name not in SPECS:
        raise RuleError(404, "not_found", f"Không có tài liệu cấu hình '{name}'. Các tài liệu có: {sorted(SPECS)}.")
    return SPECS[name]


# ------------------------------------------------------------------ biểu diễn

def _iso(value) -> str | None:
    return value.isoformat() if value is not None else None


def _meta(row: ConfigDocument) -> dict:
    return {"id": str(row.id), "name": row.name, "version": row.version, "status": row.status, "note": row.note, "created_by": row.created_by,
            "created_at": _iso(row.created_at), "activated_at": _iso(row.activated_at)}


def diff(old: object, new: object) -> dict:
    """Khác biệt cấp 1 giữa hai tài liệu: khóa thêm / bớt / đổi (người duyệt nhìn thấy ngay mình đang đổi gì)."""
    if not isinstance(old, dict) or not isinstance(new, dict):
        return {"added": [], "removed": [], "changed": []}
    keys = lambda a, b: sorted(set(a) - set(b))[:MAX_DIFF_ITEMS]
    return {"added": keys(new, old), "removed": keys(old, new), "changed": sorted(k for k in set(old) & set(new) if old[k] != new[k])[:MAX_DIFF_ITEMS]}


# ------------------------------------------------------------------ thao tác trên DB

async def _active_row(session: AsyncSession, name: str) -> ConfigDocument | None:
    return await session.scalar(select(ConfigDocument).where(ConfigDocument.name == name, ConfigDocument.status == "active"))


async def active_document(session: AsyncSession, name: str) -> dict:
    """Bản đang chạy: trong DB nếu có, không thì bản mặc định trong image (version 0)."""
    spec = spec_of(name)
    row = await _active_row(session, name)
    if row is None:
        return {"name": name, "version": 0, "status": "default", "source": "file", "content": spec.default()}
    return {**_meta(row), "source": "db", "content": stored_content(spec, row.content)}  # admin tải về bản đủ khóa mới, nếu không bản họ sửa sẽ bị từ chối vì thiếu khóa


async def overview(session: AsyncSession) -> list[dict]:
    result = []
    for spec in SPECS.values():
        row = await _active_row(session, spec.name)
        count = await session.scalar(select(func.count()).select_from(ConfigDocument).where(ConfigDocument.name == spec.name))
        result.append({"name": spec.name, "title": spec.title, "consumers": list(spec.consumers), "active_version": row.version if row else 0,
                       "source": "db" if row else "file", "versions": count or 0, "preview_available": spec.preview is not None})
    return result


async def list_versions(session: AsyncSession, name: str, *, limit: int = 50, offset: int = 0) -> dict:
    spec_of(name)
    rows = (await session.scalars(select(ConfigDocument).where(ConfigDocument.name == name).order_by(ConfigDocument.version.desc()).limit(limit).offset(offset))).all()
    total = await session.scalar(select(func.count()).select_from(ConfigDocument).where(ConfigDocument.name == name)) or 0
    return {"items": [_meta(row) for row in rows], "total": total, "limit": limit, "offset": offset}


async def get_version(session: AsyncSession, name: str, version: int) -> ConfigDocument:
    spec_of(name)
    row = await session.scalar(select(ConfigDocument).where(ConfigDocument.name == name, ConfigDocument.version == version))
    if row is None:
        raise RuleError(404, "not_found", f"Không có phiên bản {version} của '{name}'.")
    return row


async def create_draft(session: AsyncSession, name: str, content: dict, *, note: str, actor: str) -> dict:
    spec = spec_of(name)
    if not isinstance(content, dict) or not content:
        raise _invalid("invalid_document", "Tài liệu phải là một đối tượng JSON không rỗng.")
    if not note or len(note.strip()) < 3:
        raise _invalid("note_required", "Cần ghi lý do đổi (ít nhất 3 ký tự): người đọc lịch sử phải hiểu vì sao.")
    _check_json_size(content)
    spec.validate(content)
    current = await active_document(session, name)
    next_version = (await session.scalar(select(func.max(ConfigDocument.version)).where(ConfigDocument.name == name)) or 0) + 1
    row = ConfigDocument(name=name, version=next_version, status="draft", content=content, note=note.strip(), created_by=actor)
    session.add(row)
    try:
        await session.commit()
    except IntegrityError as error:
        await session.rollback()
        raise _conflict("version_conflict", "Có người vừa lưu một phiên bản khác cùng lúc; tải lại rồi thử lại.") from error
    await session.refresh(row)
    return {**_meta(row), "validation": "ok", "diff_vs_active": diff(current["content"], content)}


async def preview_version(session: AsyncSession, name: str, version: int) -> dict:
    spec = spec_of(name)
    row = await get_version(session, name, version)
    current = await active_document(session, name)
    content = stored_content(spec, row.content)
    result = {"name": name, "version": version, "diff_vs_active": diff(current["content"], content)}
    result["preview"] = spec.preview(content) if spec.preview else {
        "available": False, "reason": "Tài liệu này được dùng ở dịch vụ khác; chạy cổng đo của dịch vụ đó (ví dụ ocr-service/benchmark/run_holdout.py) sau khi kích hoạt."}
    return result


async def activate(session: AsyncSession, name: str, version: int, *, actor: str, expected_active: int, force: bool = False) -> dict:
    """Kích hoạt một phiên bản (bản nháp hoặc bản cũ để hoàn tác). ``expected_active`` là bản mà người sửa đang nhìn thấy: khác với thực tế thì từ chối."""
    spec = spec_of(name)
    rows = (await session.scalars(select(ConfigDocument).where(ConfigDocument.name == name).with_for_update())).all()  # khóa các dòng của tài liệu này
    target = next((r for r in rows if r.version == version), None)
    if target is None:
        await session.rollback()
        raise RuleError(404, "not_found", f"Không có phiên bản {version} của '{name}'.")
    active = next((r for r in rows if r.status == "active"), None)
    active_version = active.version if active else 0
    if target.status == "active":
        await session.rollback()
        raise _conflict("already_active", f"Phiên bản {version} đang chạy rồi.")
    if expected_active != active_version:
        await session.rollback()
        raise _conflict("stale_active", f"Bản đang chạy là {active_version}, không phải {expected_active}: có người vừa đổi. Tải lại rồi xem xét lại.")
    content = stored_content(spec, target.content)  # bản nháp/bản cũ lưu trước khi code thêm khóa mới thì thiếu khóa đó
    try:
        spec.validate(content)  # kiểm tra lại: quy tắc kiểm tra có thể đã đổi từ lúc lưu
        if spec.preview and not force:
            result = spec.preview(content)
            if result.get("available") and result["delta"] < 0:
                raise _invalid("preview_regression", f"Phiên bản này làm tụt độ chính xác trên bộ câu thử ({result['correct_before']} -> {result['correct_after']} / {result['cases']}). "
                                                     "Xem /preview để biết câu nào đổi; nếu vẫn muốn áp dụng thì kích hoạt với force=true.")
    except RuleError:
        await session.rollback()
        raise
    if active is not None:
        active.status = "archived"
        await session.flush()  # bỏ cờ active cũ trước, để partial unique index không bị vi phạm
    target.status, target.activated_at = "active", func.now()
    await session.commit()
    await session.refresh(target)
    if spec.apply:
        spec.apply(content)  # có hiệu lực ngay trong tiến trình này; tiến trình khác nạp lại ở lần kiểm tra kế tiếp (config_runtime)
        APPLIED[name] = target.version
    return {**_meta(target), "previous_active": active_version, "applied_here": spec.apply is not None}
