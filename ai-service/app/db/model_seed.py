"""Dữ liệu ban đầu của ``ml_models``: các model đã có ``stage`` trong dữ liệu cũ.

Migration 0023 giữ BẢN SAO CỐ ĐỊNH (và tự thêm mọi ``stage`` lạ tìm thấy trong dữ liệu cũ); module này dành cho test và công cụ dựng lại bảng.
Thêm một model mới: INSERT một dòng vào ``ml_models`` (``code`` là giá trị bạn sẽ truyền khi ghi phiên bản), rồi thêm vào đây để test biết.
"""

from sqlalchemy import text

# (code, kind, provider, notes)
MODELS: list[tuple[str, str, str | None, str]] = [
    ("classifier", "sklearn", None, "Thông báo ngân hàng: có phải giao dịch không (scripts/train.py --stage classifier)"),
    ("categorizer", "sklearn", None, "Thông báo ngân hàng: danh mục của giao dịch (scripts/train.py --stage categorizer)"),
    ("bill_slug", "sklearn", None, "Danh mục hóa đơn học từ nhãn người dùng (app/bill/slug_model.py)"),
    ("bill_category", "sklearn", None, "Nhóm danh mục hóa đơn do ocr-service huấn luyện (classification/category_model.py)"),
]

INSERT_MODEL = text("INSERT INTO ml_models (code, kind, provider, notes) VALUES (:code, :kind, :provider, :notes) ON CONFLICT (code) DO NOTHING")


async def seed_models(connection) -> None:
    for code, kind, provider, notes in MODELS:
        await connection.execute(INSERT_MODEL, {"code": code, "kind": kind, "provider": provider, "notes": notes})
