import io
import sys
import pathlib

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))

from fastapi.testclient import TestClient
from PIL import Image

import main
from reading.ocr import NoTextError
from tests.conftest import boxes_patch

client = TestClient(main.app)


def _png_bytes(dark: bool = False) -> bytes:
    # nền trắng có vài nét đen như hóa đơn thật (ảnh trắng tinh bị coi là quá sáng)
    img = Image.new("RGB", (60, 60), (20, 20, 20) if dark else "white")
    if not dark:
        img.paste((0, 0, 0), (5, 5, 55, 9))
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return buf.getvalue()


def _bill(header: str, total: str, conf: float = 0.95):
    """Chữ OCR giả của một hóa đơn đầy đủ (đủ để được chấm là hóa đơn)."""
    rows = [header, "HOA DON BAN LE", f"Mon A 1 {total} {total}",  # dòng hàng khớp với tổng (đối chiếu tổng với các dòng hàng)
            "Cam on quy khach", f"TONG CONG {total}", f"Tien khach tra {total}"]
    return lambda _: [(t, conf) for t in rows]


def _post(content: bytes, name="bill.png", ctype="image/png"):
    return client.post("/analyze-bill", files={"image": (name, content, ctype)})


def test_health():
    r = client.get("/health")
    assert r.status_code == 200
    assert r.json() == {"status": "ok"}


def test_rejects_non_image_with_400():
    r = _post(b"abc", "note.txt", "text/plain")
    assert r.status_code == 400
    assert "error" in r.json()


def test_rejects_oversize_with_400(monkeypatch):
    monkeypatch.setattr(main, "MAX_BYTES", 100)
    r = _post(_png_bytes())
    assert r.status_code == 400
    assert "error" in r.json()


def test_blank_image_returns_422(monkeypatch):
    def no_text(_):
        raise NoTextError()

    monkeypatch.setattr(main, "read_boxes", boxes_patch(no_text))
    r = _post(_png_bytes())
    assert r.status_code == 422
    assert "error" in r.json()


def test_success_returns_analysis(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("HIGHLANDS COFFEE", "125.000", 0.9)))
    r = _post(_png_bytes())
    assert r.status_code == 200
    body = r.json()
    assert body["total_amount"] == 125000
    assert body["category"] == "dining"
    assert body["quality"] == "ok"
    assert body["needs_review"] is False


def test_dark_image_reports_quality_and_forces_review(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("HIGHLANDS COFFEE", "125.000")))
    body = _post(_png_bytes(dark=True)).json()
    assert body["quality"] == "too_dark"
    assert body["needs_review"] is True


def test_saved_merchant_category_is_used_by_next_analysis(monkeypatch, tmp_path):
    from storage.memory import MerchantMemory

    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "m.json"))
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("QWERTY", "125.000")))

    first = _post(_png_bytes()).json()
    assert (first["category"], first["category_source"]) == ("other", "none")
    assert first["needs_review"] is True

    saved = client.post("/merchant-category", json={"merchant": "QWERTY", "category": "dining"})
    assert saved.status_code == 200
    assert saved.json() == {"merchant": "Qwerty", "category": "dining"}   # tên IN HOA được viết đẹp lại

    second = _post(_png_bytes()).json()
    assert (second["category"], second["category_source"]) == ("dining", "memory")
    assert second["needs_review"] is False


def test_model_suggestion_is_used_for_unknown_merchant_but_still_flagged(monkeypatch):
    class FakeModel:
        def predict(self, merchant, lines):
            return "healthcare", 0.88

    monkeypatch.setattr(main, "category_model", FakeModel())
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("QWERTY", "52.000")))
    body = _post(_png_bytes()).json()
    assert (body["category"], body["category_source"]) == ("healthcare", "model")
    assert body["category_confidence"] == 0.69
    assert body["needs_review"] is True      # gợi ý của mô hình luôn cần người xác nhận


def test_saving_category_appends_redacted_labeled_record(monkeypatch, tmp_path):
    from storage.dataset import Dataset
    from storage.memory import MerchantMemory

    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "m.json"))
    monkeypatch.setattr(main, "dataset", Dataset(tmp_path / "d.jsonl"))
    r = client.post("/merchant-category", json={
        "merchant": "Kisnac", "category": "groceries",
        "raw_lines": ["Kisnac", "SDT 0947210344", "TONG 570,500"],
        "total_amount": 570500, "predicted_category": "other", "predicted_source": "none",
    })
    assert r.status_code == 200
    rows = main.dataset.read_all()
    assert len(rows) == 1
    row = rows[0]
    assert (row["merchant"], row["category"], row["label_source"]) == ("Kisnac", "groceries", "user")
    assert (row["predicted_category"], row["total_amount"]) == ("other", 570500)
    assert row["lines"] == ["Kisnac", "TONG 570,500"]  # dòng SDT bị bỏ hẳn (dữ liệu cá nhân), không chỉ che số


def test_saving_stores_a_nicely_formatted_name_but_keeps_the_raw_ocr_name():
    r = client.post("/merchant-category", json={
        "merchant": "PHIEU THANH TOAN BACHHóA XANH", "ocr_merchant": "PHIEU THANH TOAN BACHHóA XANH",
        "category": "groceries",
    })
    assert r.status_code == 200 and r.json()["merchant"] == "Bách Hóa Xanh"
    row = main.dataset.read_all()[0]
    assert row["merchant"] == "Bách Hóa Xanh"
    assert row["ocr_merchant"] == "PHIEU THANH TOAN BACHHóA XANH"      # tên OCR gốc giữ nguyên để dò lỗi
    assert main.memory.get("PHIEU THANH TOAN BACHHóA XANH") == "groceries"  # lần sau OCR đọc tên cũ vẫn nhận ra


def test_analysis_returns_a_formatted_display_name(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("BACHHóA XANH", "17.447", 0.9)))
    body = _post(_png_bytes()).json()
    assert body["merchant"] == "BACHHóA XANH"            # tên OCR gốc không đổi
    assert body["merchant_display"] == "Bách Hóa Xanh"


def test_saved_pretty_name_is_shown_next_time_even_when_ocr_loses_accents(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("THUC.PHAMSACH", "52.000", 0.9)))
    first = _post(_png_bytes()).json()
    assert first["merchant"] == "THUC.PHAMSACH" and first["merchant_display"] == "Thuc Phamsach"   # chữ dính: chưa tách được

    saved = client.post("/merchant-category", json={
        "merchant": "Thực Phẩm Sạch", "ocr_merchant": "THUC.PHAMSACH", "category": "groceries"})
    assert saved.status_code == 200 and saved.json()["merchant"] == "Thực Phẩm Sạch"

    again = _post(_png_bytes()).json()
    assert again["merchant_display"] == "Thực Phẩm Sạch"                       # tên đẹp bạn đã chốt
    assert (again["category"], again["category_source"]) == ("groceries", "memory")


def test_known_name_is_found_even_when_it_is_not_in_the_header(monkeypatch):
    client.post("/merchant-category", json={"merchant": "Thực Phẩm Sạch", "category": "groceries"})
    rows = ["PHIEU GIAO NHAN VA THANH TOAN", "Hoa don so IN042620", "Cam on quy khach", "Mon A 1 20.000 20.000",
            "Mon B 2 15.000 30.000", "THUC.PHAMSACH", "TONG CONG 50.000", "Tien khach tra 50.000"]
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in rows]))
    body = _post(_png_bytes()).json()
    assert body["merchant"] == "THUC.PHAMSACH"
    assert body["merchant_display"] == "Thực Phẩm Sạch"


def test_user_corrected_name_also_remembers_the_ocr_garbled_name(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("FPTRvtail", "52.000", 0.9)))
    assert _post(_png_bytes()).json()["category"] == "other"
    r = client.post("/merchant-category", json={
        "merchant": "Nhà thuốc Long Châu", "ocr_merchant": "FPTRvtail", "category": "healthcare",
    })
    assert r.status_code == 200
    again = _post(_png_bytes()).json()
    assert (again["category"], again["category_source"]) == ("healthcare", "memory")
    row = main.dataset.read_all()[0]
    assert (row["merchant"], row["ocr_merchant"]) == ("Nhà thuốc Long Châu", "FPTRvtail")


def test_saving_without_bill_details_still_works_and_invalid_label_not_logged(monkeypatch, tmp_path):
    from storage.dataset import Dataset
    from storage.memory import MerchantMemory

    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "m.json"))
    monkeypatch.setattr(main, "dataset", Dataset(tmp_path / "d.jsonl"))
    assert client.post("/merchant-category", json={"merchant": "A", "category": "dining"}).status_code == 200
    assert client.post("/merchant-category", json={"merchant": "A", "category": "nope"}).status_code == 400
    rows = main.dataset.read_all()
    assert len(rows) == 1 and rows[0]["lines"] == []


def test_dataset_is_not_exposed_over_http():
    assert client.get("/dataset").status_code == 404


def test_merchant_category_rejects_bad_input(monkeypatch, tmp_path):
    from storage.memory import MerchantMemory

    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "m.json"))
    bad_category = client.post("/merchant-category", json={"merchant": "X", "category": "nope"})
    assert bad_category.status_code == 400 and "error" in bad_category.json()
    blank = client.post("/merchant-category", json={"merchant": "  ", "category": "dining"})
    assert blank.status_code == 400 and "error" in blank.json()


def test_image_with_text_but_not_a_receipt_is_rejected_with_a_clear_code(monkeypatch):
    poster = ["SALE 50% OFF", "MUA 1 TANG 1", "Chi tu 199.000d", "Con 99.000d", "Hotline 1900 5555", "www.shop.vn"]
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in poster]))
    r = _post(_png_bytes())
    assert r.status_code == 422
    body = r.json()
    assert body["code"] == "not_a_receipt" and "không giống hóa đơn" in body["error"]


def test_image_without_any_text_says_it_is_not_a_receipt(monkeypatch):
    def no_text(_):
        raise NoTextError()

    monkeypatch.setattr(main, "read_boxes", boxes_patch(no_text))
    body = _post(_png_bytes()).json()
    assert body["code"] == "not_a_receipt" and "hóa đơn" in body["error"]


def test_borderline_receipt_is_accepted_but_flagged_for_review(monkeypatch):
    # thực đơn dài có giá lặp nhưng không có nhãn Tổng: điểm đúng ngưỡng 5 => nhận nhưng nhắc kiểm tra
    menu = ["QUAN A", "Mon 1 35.000", "Mon 2 35.000", "Mon 3 30.000", "Mon 4 45.000", "Mon 5 25.000",
            "Mon 6 40.000", "Mon 7 20.000", "Mon 8 50.000"]
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in menu]))
    r = _post(_png_bytes())
    assert r.status_code == 200
    assert r.json()["needs_review"] is True and r.json()["receipt_score"] == 5


def test_unexpected_error_returns_500(monkeypatch):
    def boom(_):
        raise RuntimeError("x")

    monkeypatch.setattr(main, "read_boxes", boxes_patch(boom))
    r = _post(_png_bytes())
    assert r.status_code == 500
    assert "error" in r.json()


def test_app_screen_ride_and_order_list_are_read_by_their_templates(monkeypatch):
    from storage.memory import MerchantMemory
    from test_app_screens import RIDE, SHOPEE
    monkeypatch.setattr(main, "memory", MerchantMemory(pathlib.Path(__import__("tempfile").mkdtemp()) / "m.json"))
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in RIDE]))
    ride = _post(_png_bytes()).json()
    assert ride["screen_type"] == "ride" and ride["total_amount"] == 32000 and ride["category"] == "transport"
    assert not ride["needs_review"]  # có đủ tên chung và tổng

    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in SHOPEE]))
    shop = _post(_png_bytes()).json()
    assert shop["screen_type"] == "order_list" and shop["needs_review"]
    assert [(o["merchant_display"], o["total_amount"], o["category"]) for o in shop["orders"]] == [
        ("Furycube Local", 466650, "shopping"), ("Vualotchuot", 90200, "shopping"), ("Buni Furniture Toan Quoc", 1073748, "shopping")]
    assert shop["incomplete_orders"] == 1


def test_saved_shop_category_wins_over_the_shopping_default(monkeypatch, tmp_path):
    from storage.memory import MerchantMemory
    from test_app_screens import SHOPEE
    mem = MerchantMemory(tmp_path / "m.json")
    mem.set("VUALOTCHUOT", "entertainment", name="VuaLotChuot")
    monkeypatch.setattr(main, "memory", mem)
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in SHOPEE]))
    orders = _post(_png_bytes()).json()["orders"]
    assert orders[1]["category"] == "entertainment" and orders[1]["category_source"] == "memory"
    assert orders[1]["merchant_display"] == "VuaLotChuot"


def test_scan_returns_no_ocr_text_and_saves_the_server_side_filtered_lines(monkeypatch, tmp_path):
    from storage.dataset import Dataset
    from storage.memory import MerchantMemory
    rows = ["HIGHLANDS COFFEE", "HOA DON BAN LE", "Khach hang: Nguyen Van A", "DT093.636.6628", "Mon A 1 20.000 20.000", "Mon B 2 15.000 30.000",
            "STK 1903.4567.8901.23 Techcombank", "Cam on quy khach", "TONG CONG 50.000", "Tien khach tra 50.000"]
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in rows]))
    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "m.json"))
    monkeypatch.setattr(main, "dataset", Dataset(tmp_path / "d.jsonl"))
    result = _post(_png_bytes()).json()
    assert "raw_lines" not in result and result["scan_id"]  # app không nhận chữ OCR, chỉ nhận mã lượt quét
    assert result["merchant_display"] and result["total_amount"] == 50000
    # lưu bằng scan_id: server tự lấy lại phần chữ đã lọc, app không phải gửi lại chữ nào
    client.post("/merchant-category", json={"merchant": "Highlands", "category": "dining", "scan_id": result["scan_id"], "total_amount": 50000})
    saved = " | ".join(main.dataset.read_all()[0]["lines"])
    assert "Mon A 1 20.000" in saved and "TONG CONG 50.000" in saved
    for secret in ("093.636.6628", "Nguyen Van A", "1903.4567"):
        assert secret not in saved


def test_saving_with_an_unknown_scan_id_still_sanitizes_the_lines_sent_instead(monkeypatch, tmp_path):
    from storage.dataset import Dataset
    from storage.memory import MerchantMemory
    monkeypatch.setattr(main, "memory", MerchantMemory(tmp_path / "m.json"))
    monkeypatch.setattr(main, "dataset", Dataset(tmp_path / "d.jsonl"))
    client.post("/merchant-category", json={"merchant": "Kisnac", "category": "groceries", "scan_id": "khong-co",
                                            "raw_lines": ["Kisnac", "DT093.636.6628", "TONG 1.000"]})
    assert main.dataset.read_all()[0]["lines"] == ["Kisnac", "TONG 1.000"]


def test_response_carries_only_filtered_lines_for_the_ai_service(monkeypatch):
    rows = ["HIGHLANDS COFFEE", "HOA DON BAN LE", "SDT: 0901234567", "Khach hang: Nguyen Van A", "Mon A 1 125.000 125.000",
            "Cam on quy khach", "TONG CONG 125.000", "Tien khach tra 125.000"]
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in rows]))
    lines = _post(_png_bytes()).json()["lines"]
    assert "HIGHLANDS COFFEE" in lines and "TONG CONG 125.000" in lines
    assert not any("0901234567" in l or "Nguyen Van A" in l for l in lines)  # điện thoại và tên khách không rời OCR service


def test_warm_up_loads_the_engines_and_never_raises(monkeypatch):
    calls = []
    monkeypatch.setattr(main.ocr, "_engine", lambda: calls.append("paddle"))
    monkeypatch.setattr(main.ocr, "_use_vietocr", lambda: True)
    monkeypatch.setattr(main.vietocr_onnx, "_sessions", lambda: calls.append("vietocr"))
    main.warm_up()
    assert calls == ["paddle", "vietocr"]

    def boom():
        raise RuntimeError("không có model")

    monkeypatch.setattr(main.ocr, "_engine", boom)
    main.warm_up()  # lỗi nạp trước không được làm sập dịch vụ


def test_response_carries_the_transaction_date_in_vietnam_time(monkeypatch):
    rows = ["HIGHLANDS COFFEE", "HOA DON BAN LE", "Ngay11-04-202613:16", "Mon A 1 125.000 125.000", "TONG CONG 125.000", "Tien khach tra 125.000"]
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in rows]))
    assert _post(_png_bytes()).json()["transacted_at"] == "2026-04-11T13:16:00+07:00"
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("HIGHLANDS COFFEE", "125.000")))
    assert _post(_png_bytes()).json()["transacted_at"] is None


# ---- items trong /analyze-bill ----
def _items_rows():
    return ["QUAN CAFE", "HOA DON BAN LE", "STT Ten mon SL Don gia Thanh tien", "Tra dao 2 25.000 50.000",
            "Banh mi 1 30.000 30.000", "TONG CONG 80.000", "Tien khach tra 80.000"]


def _grid_boxes(rows):
    """Hộp có tọa độ thật: tên bên trái, giá bên phải, mỗi dòng một hàng."""
    from reading.ocr import OcrBox
    boxes = []
    for i, row in enumerate(rows):
        y = i * 20.0
        words = row.split()
        if len(words) > 3 and words[-1][0].isdigit():
            boxes.append(OcrBox(" ".join(words[:-3]), 0.95, 0, y, 200, y + 18))
            boxes += [OcrBox(w, 0.95, 220 + 60 * j, y, 270 + 60 * j, y + 18) for j, w in enumerate(words[-3:])]
        else:
            boxes.append(OcrBox(row, 0.95, 0, y, 200, y + 18))
    return boxes


def test_analyze_bill_returns_items_and_items_ok(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", lambda _: _grid_boxes(_items_rows()))
    body = _post(_png_bytes()).json()
    assert body["items_ok"] is True
    assert [(i["name"], i["amount"]) for i in body["items"]] == [("Tra dao", 50000), ("Banh mi", 30000)]
    assert set(body["items"][0]) == {"name", "amount", "parent", "is_discount"}


def test_item_names_are_sanitized(monkeypatch):
    from parsing.items import Item, ItemsResult
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("QUAN A", "50.000")))
    monkeypatch.setattr(main, "extract_items", lambda b, t: ItemsResult(
        [Item("Tra 0912345678", 20000, None, False), Item("Banh", 30000, None, False)], True))
    body = _post(_png_bytes()).json()
    assert "0912345678" not in str(body["items"])
    assert body["items_ok"] is True


def test_item_name_sanitized_to_empty_is_dropped_and_not_ok(monkeypatch):
    from parsing.items import Item, ItemsResult
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("QUAN A", "50.000")))
    monkeypatch.setattr(main, "extract_items", lambda b, t: ItemsResult(
        [Item("0912345678", 20000, None, False), Item("Banh", 30000, None, False)], True))
    body = _post(_png_bytes()).json()
    assert [i["name"] for i in body["items"]] == ["Banh"]
    assert body["items_ok"] is False


def test_analyze_bill_without_boxes_info_still_returns_old_keys(monkeypatch):
    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("HIGHLANDS COFFEE", "125.000")))
    body = _post(_png_bytes()).json()
    assert body["total_amount"] == 125000
    assert {"merchant", "category", "scan_id", "lines", "quality"} <= set(body)
    assert isinstance(body["items"], list) and isinstance(body["items_ok"], bool)


def test_extract_items_failure_does_not_fail_scan(monkeypatch):
    def boom(b, t):
        raise RuntimeError("secret 50.000")

    monkeypatch.setattr(main, "read_boxes", boxes_patch(_bill("HIGHLANDS COFFEE", "125.000")))
    monkeypatch.setattr(main, "extract_items", boom)
    r = _post(_png_bytes())
    assert r.status_code == 200
    assert r.json()["items"] == [] and r.json()["items_ok"] is False


def test_screen_bill_returns_no_items(monkeypatch):
    from test_app_screens import RIDE
    monkeypatch.setattr(main, "read_boxes", boxes_patch(lambda _: [(t, 0.95) for t in RIDE]))
    body = _post(_png_bytes()).json()
    assert body["items"] == [] and body["items_ok"] is False, body


def _layout(*rows):
    """Hộp chữ có tọa độ: mỗi hàng là danh sách (chữ, x0, x1); hàng thứ i cao 30px tại y = i * 45."""
    from reading.ocr import OcrBox
    return [OcrBox(t, 0.95, x0, i * 45, x1, i * 45 + 30) for i, row in enumerate(rows) for t, x0, x1 in row]


def test_total_is_the_amount_on_the_payable_row_even_when_the_line_above_is_a_number(monkeypatch):
    # ảnh thật (bill Nhà Bếp Quê): dòng ngay trước "Tổng thanh toán" là số thuế; luật đọc theo thứ tự dòng từng lấy nhầm thuế làm tổng
    boxes = _layout(
        [("QUAN COM NHA BEP QUE", 300, 700)], [("HOA DON BAN LE", 300, 700)],
        [("Com suon nuong", 50, 300), ("2", 500, 520), ("45.000", 600, 700), ("90.000", 800, 950)],
        [("Canh chua ca", 50, 300), ("1", 500, 520), ("35.000", 600, 700), ("35.000", 800, 950)],
        [("Tong tien hang (2 mon):", 50, 450), ("125.000", 800, 950)],
        [("Thue GTGT (8%):", 50, 400), ("10.000", 820, 950)],
        [("Tong thanh toan:", 50, 450), ("135.000", 780, 950)],
        [("Tien khach dua:", 50, 400), ("200.000", 780, 950)],
        [("Tien thoi lai:", 50, 400), ("65.000", 800, 950)],
        [("Cam on quy khach", 300, 700)],
    )
    monkeypatch.setattr(main, "read_boxes", lambda _data: boxes)

    r = _post(_png_bytes())

    assert r.status_code == 200
    assert r.json()["total_amount"] == 135000


def test_items_that_do_not_add_up_to_the_total_ask_for_review(monkeypatch):
    # bảng món đọc được (2 món = 125.000) nhưng tổng ở nhãn là 150.000: lệch mà không có dòng thuế/phí nào giải thích -> nhắc kiểm tra
    boxes = _layout(
        [("QUAN COM NHA BEP QUE", 300, 700)], [("HOA DON BAN LE", 300, 700)],
        [("STT", 50, 100), ("Ten hang", 150, 400), ("SL", 560, 600), ("Don gia", 650, 750), ("Thanh tien", 800, 950)],
        [("1", 50, 70), ("Com suon nuong", 150, 400), ("2", 570, 590), ("45.000", 650, 750), ("90.000", 820, 950)],
        [("2", 50, 70), ("Canh chua ca", 150, 400), ("1", 570, 590), ("35.000", 650, 750), ("35.000", 820, 950)],
        [("Tong thanh toan:", 50, 450), ("150.000", 780, 950)],
        [("Cam on quy khach", 300, 700)],
    )
    monkeypatch.setattr(main, "read_boxes", lambda _data: boxes)

    body = _post(_png_bytes()).json()

    assert body["total_amount"] == 150000
    assert body["items_ok"] is False
    assert body["needs_review"] is True


def test_a_correct_tong_cong_total_does_not_leave_the_review_flag_from_the_wrong_one(monkeypatch):
    # ảnh thật (Tạp hóa Đức Cường): đọc theo thứ tự dòng thì tổng bị chọn nhầm là 42.000 (số lặp của món cuối) và gắn cờ "nhắc kiểm tra";
    # tổng đọc theo hàng (157.680) phải thay luôn cả tổng lẫn các cờ tính từ nó
    def item(n, name, qty, unit, amount):
        return [(str(n), 50, 70), (name, 150, 450), (str(qty), 570, 590), (unit, 650, 750), (amount, 820, 950)]

    boxes = _layout(
        [("TAP HOA", 300, 700)], [("DUC CUONG", 300, 700)], [("HOA DON BAN HANG", 50, 500)],
        [("STT", 50, 100), ("Ten hang", 150, 400), ("SL", 560, 600), ("Don gia", 650, 750), ("Thanh tien", 800, 950)],
        item(1, "Mi Hao Hao tom chua cay", 2, "5.000", "10.000"), item(2, "Sua tuoi Vinamilk 1L", 1, "34.000", "34.000"),
        item(3, "Nuoc suoi Aquafina 500ml", 2, "7.000", "14.000"), item(4, "Nuoc rua chen Sunlight", 1, "28.000", "28.000"),
        item(5, "Khan giay Pulppy 10 goi", 1, "18.000", "18.000"), item(6, "Bot giat OMO 800g", 1, "42.000", "42.000"),
        [("Tam tinh:", 500, 700), ("146.000", 800, 950)], [("Thue GTGT (8%):", 500, 750), ("11.680", 820, 950)],
        [("Tong cong:", 500, 700), ("157.680", 800, 950)], [("Tien khach dua:", 500, 750), ("200.000", 800, 950)],
        [("Tien thoi lai:", 500, 750), ("42.320", 820, 950)], [("CAM ON QUY KHACH", 300, 700)],
    )
    monkeypatch.setattr(main, "read_boxes", lambda _data: boxes)

    body = _post(_png_bytes()).json()

    assert body["total_amount"] == 157680
    assert body["items_ok"] is True
    assert body["needs_review"] is False
