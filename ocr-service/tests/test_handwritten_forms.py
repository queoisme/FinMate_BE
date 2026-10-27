"""Hóa đơn bán lẻ MẪU IN SẴN, người bán viết tay (hai bill thật, Google Cloud Vision đọc, ngày 2026-10-27).

Hai mẫu này thiếu nhiều dấu hiệu mà bộ đọc quen dựa vào, nên cùng trượt theo bốn cách (fixture: tests/fixtures/handwritten_forms.json):
  - CoCo Mart: Google trả một ô con nằm trọn trong ô khác; tổng 447.000 không có nhãn (chỉ là số đứng dưới cột tiền); ô "Họ tên người mua hàng" (bên MUA) bị lấy làm tên cửa hàng.
  - Tiệm rau: người bán viết tắt theo nghìn đồng ("44" = 44.000) nên không dòng nào giống số tiền; có ô gộp "1 quả (1,15) 24" nuốt mất thành tiền; không ghi tổng.
Mỗi bài kiểm thử nêu một nguyên tắc chung, không phải luật của riêng bill nào."""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from parsing.extract import MIN_RECEIPT_SCORE, analyze, receipt_score
from parsing.items import extract_items
from parsing.prepare import prepare
from parsing.totals import choose_total, resolve_total
from reading.ocr import OcrBox, drop_contained

FIXTURES = json.loads((pathlib.Path(__file__).parent / "fixtures" / "handwritten_forms.json").read_text(encoding="utf-8"))


def _boxes(name):
    return [OcrBox(t, 0.9, x0, y0, x1, y1) for t, x0, y0, x1, y1 in FIXTURES[name]]


def _prepared(name):
    boxes, scaled = prepare(drop_contained(_boxes(name)))
    return boxes, scaled


def test_a_box_lying_inside_another_box_is_a_duplicate_detection_and_is_dropped():
    # Google trả "M" (của chữ "Mì") thành ô riêng nằm trọn trong ô "Bánh Mì": tên món thành "Bánh Mì M"
    outer, inner, other = OcrBox("Bánh Mì", 0.9, 111, 687, 289, 746), OcrBox("M", 0.9, 232, 689, 249, 739), OcrBox("01", 0.9, 53, 706, 76, 722)
    assert [b.text for b in drop_contained([outer, inner, other])] == ["Bánh Mì", "01"]


def test_a_bare_amount_under_the_item_column_that_equals_the_sum_of_the_items_is_the_total():
    # mẫu in sẵn không có chữ "Tổng": 396.000 + 51.000 = 447.000 là bằng chứng số học mạnh nhất có thể có
    boxes, _ = _prepared("coco_mart_pad")
    assert choose_total(boxes) == (447_000, True)
    assert [i.name for i in extract_items(boxes, 447_000).items] == ["Bánh Mì", "Việt Quất"]


def test_the_buyer_field_of_a_retail_invoice_pad_is_not_the_shop_name():
    # "Họ tên người mua hàng: CoCo Mart" là BÊN MUA, "Đơn vị cơ quan" là tên ô điền: bill không in tên người bán thì để trống để hỏi người dùng
    boxes, _ = _prepared("coco_mart_pad")
    result = analyze([(b.text, b.conf) for b in boxes], forced_total=447_000)
    assert not result["merchant"]


def test_a_field_label_takes_the_handwritten_value_on_its_row_so_the_pair_reads_as_one_field():
    # chữ viết tay cao hơn nhãn in nên hộp của nó nằm lệch lên trên; hai ô rời nhau thì "Colo Mart" đứng một mình như tên cửa hàng
    # (Paddle đọc "Khách hàng: COCO MART" thành một dòng, nên bộ chọn tên đã loại "Nhãn: giá trị"; Google tách ô nên phải ghép lại)
    boxes, _ = _prepared("coco_mart_pad")
    texts = [b.text for b in boxes]
    assert "Họ tên người mua hàng : Colo Mart" in texts and "Colo Mart" not in texts


def test_a_label_without_a_written_value_or_with_a_number_value_is_left_alone():
    label = OcrBox("Điện thoại :", 0.9, 10, 10, 120, 40)
    number = OcrBox("0907 123 456", 0.9, 130, 12, 300, 38)
    dots = OcrBox("........", 0.9, 130, 12, 300, 38)
    assert [b.text for b in prepare([label, number])[0]] == ["Điện thoại :", "0907 123 456"]
    assert [b.text for b in prepare([label, dots])[0]] == ["Điện thoại :", "........"]


def test_amounts_written_in_thousands_are_scaled_and_the_bill_is_flagged_for_review():
    boxes, scaled = _prepared("tiem_rau_form")
    assert scaled is True
    items = extract_items(boxes, 366_000).items
    assert [i.amount for i in items] == [44_000, 34_000, 22_000, 30_000, 24_000, 24_000, 24_000, 26_000, 15_000, 20_000, 20_000, 18_000, 30_000, 10_000, 25_000]


def test_a_merged_cell_that_swallowed_the_amount_is_split_so_each_row_keeps_its_own_price():
    # ô gộp "1 quả ( 1,15 ) 24" nằm cả từ cột số lượng sang cột tiền: "24" phải về lại cột tiền, không thì hai hàng dính nhau
    boxes, _ = _prepared("tiem_rau_form")
    names = [i.name for i in extract_items(boxes, 366_000).items]
    assert any(n.startswith("Bay") for n in names) and not any("Bay" in n and "Bi" in n for n in names)
    assert len(names) == 15


def test_a_bill_without_any_total_uses_the_sum_of_its_items_but_is_not_verified():
    boxes, _ = _prepared("tiem_rau_form")
    assert resolve_total(boxes, [b.text for b in boxes]) == (366_000, False, "items")


def test_a_printed_form_with_thousands_shorthand_still_looks_like_a_receipt():
    raw = [b.text for b in _boxes("tiem_rau_form")]
    boxes, _ = _prepared("tiem_rau_form")
    assert receipt_score(raw) < MIN_RECEIPT_SCORE <= receipt_score([b.text for b in boxes])


def test_ordinary_bills_are_never_rescaled():
    # cột tiền đã có số có dấu nghìn thì không có gì để nhân: "44" cạnh "396.000" là số lượng, không phải 44.000
    boxes, scaled = _prepared("coco_mart_pad")
    assert scaled is False
    assert any(b.text.endswith("396.000") for b in boxes)


def test_the_sum_of_a_garbled_item_list_never_replaces_the_total_the_text_already_gave(monkeypatch):
    # ảnh rất tối: bảng món đọc ra rác (có món KHÔNG tên, có món trùng đúng số tổng); tổng đoán theo số lặp (570.500) vẫn đáng tin hơn cộng rác
    from parsing import totals
    from parsing.items import Item
    junk = [Item("173o SAFAN", 6_900, None, False), Item("", 49_000, None, False), Item("tien hang", 570_500, None, False), Item("Pheo qotha", 570_500, None, False)]
    monkeypatch.setattr(totals, "_split_items", lambda boxes: (None, junk))
    texts = ["570,500", "570,500"]
    assert resolve_total([], texts) == (None, False, "read")  # None = không ghi đè: analyze tự lấy tổng theo chữ
    assert analyze([(t, 0.9) for t in texts])["total_amount"] == 570_500
