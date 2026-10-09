"""Đo phần TEXT của luồng Voice: câu đã qua speech-to-text -> số tiền/loại/danh mục."""
from datetime import datetime, UTC

from app.pipeline.stages.categorizer import categorize_by_rules
from app.pipeline.stages.extractor import extract_generic

D, C = "Debit", "Credit"
# (câu, số tiền VND, loại, danh mục mong đợi hoặc None = không chấm)
CASES = [
    # --- chữ đầy đủ, đúng mẫu docx
    ("Cà phê Highland bốn mươi lăm ngàn", 45000, D, "food"),
    ("ăn phở hai mươi lăm nghìn", 25000, D, "food"),
    ("mua áo một trăm năm mươi ngàn", 150000, D, "shopping"),
    ("đổ xăng năm chục nghìn", 50000, D, "transport"),
    ("tiền điện một trăm lẻ năm nghìn", 105000, D, None),
    ("trả tiền nhà ba triệu", 3000000, D, None),
    ("nhận lương mười lăm triệu", 15000000, C, None),
    ("mua laptop hai mươi ba triệu năm trăm", 23500000, D, "shopping"),
    ("ăn trưa ba mươi nghìn", 30000, D, "food"),
    ("grab đi làm bốn mươi hai nghìn", 42000, D, "transport"),
    ("uống trà sữa năm mươi lăm nghìn", 55000, D, "food"),
    ("mười nghìn gửi xe", 10000, D, None),
    ("mười hai nghìn bánh mì", 12000, D, "food"),
    ("hai triệu rưỡi tiền học", 2500000, D, None),
    ("một triệu hai tiền điện thoại", 1200000, D, None),
    # --- biến thể phát âm / STT
    ("cà phê hai mươi mốt nghìn", 21000, D, "food"),
    ("ăn sáng hai mươi tư nghìn", 24000, D, "food"),
    ("ăn tối hai mươi nhăm nghìn", 25000, D, "food"),
    ("bốn lăm ngàn cà phê", 45000, D, "food"),
    ("ba lăm nghìn trà đá", 35000, D, None),
    ("tám mươi ngàn cắt tóc", 80000, D, None),
    ("chín mươi chín nghìn mua sách", 99000, D, None),
    ("một trăm hai mươi nghìn đi chợ", 120000, D, None),
    ("hai trăm năm chục nghìn đổ xăng", 250000, D, "transport"),
    ("sáu trăm nghìn tiền điện", 600000, D, None),
    # --- STT trả chữ số (rất hay gặp trên Android/iOS)
    ("cà phê 45000", 45000, D, "food"),
    ("ăn phở 45.000 đồng", 45000, D, "food"),
    ("ăn phở 45 nghìn", 45000, D, "food"),
    ("cà phê 45k", 45000, D, "food"),
    ("mua áo 150k", 150000, D, "shopping"),
    ("tiền nhà 3 triệu", 3000000, D, None),
    ("lương 15 triệu", 15000000, C, None),
    ("ăn trưa 35 ngàn", 35000, D, "food"),
    ("trà sữa 2 ly 80 nghìn", 80000, D, "food"),
    ("đi grab 1 triệu 2", 1200000, D, "transport"),
    ("1,5 triệu tiền điện", 1500000, D, None),
    ("2 triệu rưỡi học phí", 2500000, D, None),
    ("45.000đ cà phê", 45000, D, "food"),
    ("45,000 VND ăn phở", 45000, D, "food"),
    # --- chữ số lẫn chữ (STT hay trộn)
    ("ăn phở 2 mươi lăm nghìn", 25000, D, "food"),
    ("ăn cơm 30 nghìn ở quán cô Ba", 30000, D, "food"),
    # --- tiếng lóng
    ("hai lít tiền nhà", 200000, D, None),
    ("một củ tiền trọ", 1000000, D, None),
    ("năm xị cà phê", 500000, D, None),
    # --- thu nhập
    ("được mẹ cho năm trăm nghìn", 500000, C, None),
    ("nhận tiền thưởng hai triệu", 2000000, C, None),
    ("bạn chuyển khoản trả nợ ba trăm nghìn", 300000, C, None),
    ("bán đồ cũ được tám mươi nghìn", 80000, C, None),
    # --- có thêm số gây nhiễu
    ("ăn 2 tô phở hết một trăm nghìn", 100000, D, "food"),
    ("mua 3 cái áo tổng hai trăm bảy mươi nghìn", 270000, D, "shopping"),
    ("hôm nay 15 tháng 9 ăn phở bốn mươi nghìn", 40000, D, "food"),
    ("lúc 12 giờ trưa ăn cơm ba mươi lăm nghìn", 35000, D, "food"),
    ("tiền nhà tháng 10 là ba triệu rưỡi", 3500000, D, None),
    # --- hình thức nói đơn vị khác
    ("ăn phở bốn mươi lăm", None, D, None),  # thiếu đơn vị: mơ hồ, chấp nhận không đọc được
    ("ăn sáng năm mươi", None, D, None),
]

now = datetime(2026, 9, 30, 10, 0, tzinfo=UTC)
amount_ok = type_ok = cat_ok = cat_n = 0
scored = 0
fails = []
for text, amt, typ, cat in CASES:
    out = extract_generic(text, now)
    got_amt = out.amount_cents if out else None
    got_typ = out.transaction_type.value.capitalize() if out else None
    if amt is None:
        continue
    scored += 1
    a = got_amt == amt
    t = got_typ == typ
    amount_ok += a
    type_ok += t
    c = None
    if cat is not None:
        cat_n += 1
        c = categorize_by_rules(text).category_slug == cat
        cat_ok += c
    if not (a and t and (c is not False)):
        fails.append((text, amt, got_amt, typ, got_typ, cat, None if cat is None else categorize_by_rules(text).category_slug))

print(f"cases scored: {scored}")
print(f"amount exact : {amount_ok}/{scored} = {amount_ok/scored:.0%}")
print(f"type correct : {type_ok}/{scored} = {type_ok/scored:.0%}")
print(f"category     : {cat_ok}/{cat_n} = {cat_ok/cat_n:.0%}")
print("\nFAILURES (text | want amt/got | want type/got | want cat/got):")
for f in fails:
    print(" ", f)
