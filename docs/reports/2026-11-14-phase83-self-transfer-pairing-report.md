# Báo cáo Phase 83 — Ghép cặp chuyển tiền giữa các ví của chính mình

| | |
|---|---|
| Nhánh | `feat/self-transfer-pairing` (từ `main` @ `1c45c09`, sau PR #30 và #31) |
| Kế hoạch | `docs/superpowers/plans/2026-11-13-self-transfer-pairing.md` (D1–D22, S1–S6, bảng BR → test) |
| API cho app | `docs/transactions-transfer-pairing-api.md` |
| Kết quả test | Backend 3012/3013 (chỉ FR-FIN-07 đỏ, như mốc); ai-service 2477/2477 kể cả tích hợp trên Postgres |

## Đã làm

- **ai-service:**
  - `relation_hint = transfer` cho cả hai chiều, kèm `transfer_kind`: `atm_withdrawal`, `atm_deposit`, `wallet_topup`, `wallet_withdrawal`, `own_account`;
  - từ khoá nằm trong tài liệu `notification_vocab`; nhóm loại trừ (nạp điện thoại, data, thẻ, game, VETC, ePass, hoá đơn) thắng mọi nhóm chuyển ví;
  - hoàn tiền thắng chuyển ví; cụm dài nhất quyết định loại;
  - alembic `0038`.
- **Backend dữ liệu:** migration `AddSelfTransferPairing`, gồm:
  - kiểu riêng cho `ai_results.relation_hint` và cột `transfer_kind`;
  - `transactions.transfer_dismissed_at`;
  - lý do đóng bất thường `transfer`;
  - bảng `transaction_merge_snapshots`.
- **Gợi ý:** `TransferMatcher` (chỉ gợi ý khi có tín hiệu), tính khi đọc (`transferSuggestion`), push `review_transfer`.
- **Chuyển đổi:**
  - endpoint `convert-to-transfer`, `not-transfer` và hai `/preview`;
  - bất biến số dư BR-TRF-07 ở hàm thuần `TransferConversion`; khoản phí `other`;
  - ngân sách tính lại, `daily_summaries` chốt lại, bất thường đóng, bỏ loại khỏi dự báo;
  - snapshot; cảnh báo ví Tiền mặt âm.
- **Thông báo về sau một Transfer ghi tay:** nghi trùng rồi gộp; Transfer luôn được giữ, phí thành khoản riêng.
- **Dự báo (S3, S4):** truy vấn riêng cho chuyển khoản vượt ranh giới ví được tính, dùng cho:
  - số thực tế của độ chính xác dự báo;
  - phát hiện và theo dõi khoản định kỳ, nhắc hạn, kiểm tra "đã trả" khi chiếu dòng tiền (chỉ tiền ra);
  - hỏi "còn dùng không?" khi lần trả của khoản định kỳ thành chuyển khoản trong tập được tính.
- **Snapshot ở mọi đường gộp** (cả Phase 82), dọn theo `snapshots.retentionDays`.
- **`/admin/ai-stats`:** số gợi ý / chấp nhận / từ chối chuyển ví theo loại.
- **Sửa lỗi có từ Phase 81:** `GET /forecast/anomalies?status=all` lỗi 500 khi có đánh dấu đã được hệ thống đóng `refunded`/`reversed`, vì bảng hiển thị chỉ biết bốn câu trả lời của client.

## Độ chính xác dự báo trước và sau (S4)

Cách tính "số thực tế" thay đổi đúng một điểm: chuyển khoản vượt ranh giới tập ví được tính nay được cộng vào.

| Đo | Trước | Sau |
|---|---|---|
| Bộ dữ liệu cố định (`ForecastBoundaryTransferTests.ForecastActuals_*`): ví Tiền mặt được tính, chuyển 300.000đ sang ví tiết kiệm bị loại trong khoảng của mốc 7 ngày, kèm 1.000.000đ chuyển qua lại giữa hai ví được tính | Số thực tế 9.500.000đ: bỏ sót 300.000đ đã rời tập được tính, nên một dự báo đúng bị chấm là lệch 300.000đ | 9.200.000đ, đúng số dư thật; chuyển nội bộ không đổi gì |
| `python -m agent_eval.backtest` (dải dự báo của Agent trên hồ sơ giả lập) | Như bản dưới | **Giống hệt** (so bằng `diff` trên `main` và trên nhánh). Bộ này không đi qua `ForecastActuals` nên không đo được thay đổi này; chạy để chứng minh không có gì khác bị ảnh hưởng |

Mọi test dự báo có sẵn đều qua mà không sửa kỳ vọng. File test dự báo cũ duy nhất bị đụng là `AnomalyServiceTests`, và chỉ có thêm test cho lỗi ở trên.

Trên dữ liệu thật, chỉ số độ chính xác (MAE, coverage) sẽ đổi ở người dùng có ví bị loại khỏi dự báo và có chuyển tiền qua lại với ví đó. Môi trường dev không có dữ liệu thật nên không đo được ở đây. Sau khi triển khai nên xem lại `GET /ops/forecast/accuracy` với cửa sổ chỉ gồm các bản chụp có số thực tế điền sau ngày triển khai.

## Lệch so với kế hoạch đã duyệt

- **D18 (xem trước "không khoá, không ghi"):** xem trước chạy đúng đường code của lệnh thật trong một DB transaction rồi luôn rollback, nên không ghi gì và không bao giờ lệch với lệnh thật. Đổi lại, nó giữ khoá dòng giao dịch trong lúc chạy (vài chục ms). Viết riêng một bộ "lập kế hoạch" thuần cho gộp và chuyển đổi sẽ nhân đôi logic dễ lệch nhất của phase.
- **S4, bộ khoản định kỳ chỉ nhận tiền ra:** tiền vào từ ví bị loại (rút tiết kiệm) không được phát hiện là định kỳ. Nếu nhận, nó sẽ thành "thu nhập định kỳ" và lọt vào dự báo thu nhập, trái BR-TRF-11. Đã ghi §13.1.
- **BR-TRF-04 thêm một điều kiện:** khoản chi đã có khoản hoàn đã xác nhận cũng không chuyển được. Transfer không thể là khoản chi gốc của một khoản hoàn.
- **Test Phase 82 đổi một ca:** `MergePolicyTests.DifferentShape_IsRefused("transfer")` từng cho rằng gộp một khoản chi với một Transfer luôn bị từ chối. Phase 83 cố ý cho phép đúng trường hợp đó (BR-TRF-06), nên ca này nay là "hai Transfer với nhau".
- **Test hợp đồng ai-service:** `test_response_matches_the_backend_contract` thêm `transfer_kind` vào danh sách trường, cùng cách Phase 81 thêm `relation_hint`.

## Còn lại

- Hoàn tác chuyển đổi và gộp (§13.1; dữ liệu đã có trong snapshot 365 ngày).
- Danh mục phí riêng và 4 số đuôi tài khoản (§13.4), chờ chủ sản phẩm.
- Nhóm Flutter: màn "Đây có phải chuyển ví?", push `review_transfer`, màn xem trước khi gộp/chuyển đổi (danh sách trong `.context/TASKS.md`).
