# AI Financial Agent — hợp đồng API cho app

**Trạng thái:** đã triển khai ở backend và ai-service; chưa có phía app (FE là repo riêng). Số liệu đánh giá thật: xem `docs/agent-eval.md`.

## Nguyên tắc

1. **LLM không bao giờ sinh số tiền.** Mọi số tiền trong câu trả lời là số nguyên đồng VND và bằng đúng giá trị của một bằng chứng mà mệnh đề đó trích dẫn; bộ kiểm chứng là code thuần Python, không gọi LLM. Vi phạm → sinh lại một lần → rơi về câu mẫu dựng từ bằng chứng.
2. **Mọi dữ kiện có nguồn, ngày và độ tin cậy.** Độ tin cậy = độ đầy đủ × độ mới, bị chặn trần theo nguồn; nó **không** là xác suất thống kê và UI không được hiển thị như xác suất.
3. **Không đoán.** Thiếu dữ liệu → `insufficient_data` kèm lý do. Ngoài phạm vi tài chính cá nhân của chính người dùng → `refused`.
4. **`userId` chỉ lấy từ JWT.** ai-service không bao giờ thấy id thật (chỉ SHA-256) và không nhận câu hỏi qua log.
5. **Agent chỉ đề xuất.** Không tự tạo giao dịch, mục tiêu hay thay đổi dữ liệu.

## `POST /api/v1/agent/ask`

Yêu cầu (JWT bắt buộc): `{ "question": "Mua xe 600 triệu sau 2 năm được không?" }` — 1–500 ký tự.

Hạn mức: 20 lượt/phút/người dùng (`RATE_LIMIT_AGENT_PER_MINUTE`), vượt thì 429.

Lỗi: 422 `invalid_question`; 503 khi ai-service không dùng được **trước khi** có dữ liệu để trả lời. Nếu ai-service sập sau khi đã có bằng chứng thì vẫn trả 200 với `usedTemplate: true` (câu mẫu dựng từ bằng chứng).

### `AgentAnswerDto`

```json
{
  "status": "answered",
  "message": null,
  "claims": [{ "text": "Bạn đang có 100.000.000 đồng.", "evidenceIds": ["balance"] }],
  "evidence": [
    { "id": "balance", "key": "total_balance", "valueVnd": 100000000, "text": "Tổng số dư các tài khoản",
      "unit": "VND", "source": "confirmed", "asOf": "2026-11-07T10:00:00+07:00", "confidence": 1.0,
      "sourceName": null, "sourceUrl": null }
  ],
  "verdict": "tight",
  "traceId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301",
  "usedTemplate": false
}
```

| Trường | Ý nghĩa |
|---|---|
| `status` | `answered` (có `claims` + `evidence`), `clarify` (hỏi lại `message`), `refused` (ngoài phạm vi / tư vấn đầu tư / dữ liệu người khác / tiêm lệnh; `message` là câu từ chối), `insufficient_data` (`message` nói thiếu gì) |
| `claims[]` | Từng mệnh đề của câu trả lời. `evidenceIds` luôn nằm trong `evidence[]` của chính lượt này |
| `evidence[]` | Toàn bộ bằng chứng của lượt, để màn "Vì sao?" liệt kê từng dòng |
| `evidence[].source` | `confirmed` (đã xác nhận), `user_entered` (người dùng nhập), `estimated` (ước tính), `document` (tài liệu / giả định đã duyệt) |
| `evidence[].valueVnd` | Số nguyên đồng; `null` khi bằng chứng chỉ là chữ (đoạn tài liệu, số tháng) |
| `verdict` | Chỉ với câu hỏi mục tiêu / mua / chịu đựng: `feasible`, `tight`, `infeasible`; không có thì `null`. Không dùng từ "chắc chắn" hay "nên mua" |
| `traceId` | Mã vết của lượt (không chứa nội dung) |
| `usedTemplate` | `true` = câu trả lời dựng bằng mẫu cố định từ bằng chứng (ai-service lỗi hoặc không qua kiểm chứng) |
| `suggestedActions[]` | Thẻ đề xuất hành động (luôn có mảng, rỗng khi không có gì để đề xuất; tối đa 8). Xem mục "Thẻ đề xuất hành động" bên dưới |

### Thẻ đề xuất hành động

Mỗi phần tử: `{kind, title, amountVnd, date, categorySlug, categoryName, evidenceIds}`. Thẻ do **code của công cụ** sinh từ dữ liệu có cấu trúc, **không do LLM viết**: `title` và mọi con số lấy từ bằng chứng của chính lượt này (`evidenceIds` chỉ tới chúng nên màn "Vì sao?" mở được nguồn), nên thẻ không thể mang một con số không có nguồn. Thẻ chỉ là **đề xuất**: app hiện nút, người dùng bấm xác nhận thì app mới gọi API sẵn có; agent không tự đổi dữ liệu nào.

| `kind` | Khi nào có | Trường dùng | App làm gì khi người dùng bấm |
|---|---|---|---|
| `wait_until` | Khoản mua chưa ổn (`verdict` khác `feasible`) và có ngày an toàn sau ngày mua | `date` (`yyyy-MM-dd`) | Nút "Nhắc tôi": `POST /forecast/purchase-reminders` `{date}` hẹn một push lúc 08:00 giờ Việt Nam đúng ngày đó (xem dưới) |
| `lower_amount_to` | Khoản mua chưa ổn và mức tối đa còn giữ được vùng đệm thấp hơn giá | `amountVnd` | Nút "Kiểm tra lại": thêm thẻ mua với giá `amountVnd` và gọi `/forecast/purchase-check` (chỉ đọc) |
| `set_budget_limit` | Kết quả chia ngân sách (`plan_budget`): mỗi nhóm một thẻ, không có thẻ cho nhóm "Các nhóm khác". Cũng từ `spending_summary` khi nhìn ≥ 2 tháng: top 3 nhóm chi nhiều nhất, hạn mức = chi trung bình mỗi tháng của nhóm (làm tròn xuống nghìn đồng, tối thiểu 10.000) | `categorySlug`, `categoryName`, `amountVnd` | Tạo hoặc sửa hạn mức tháng của nhóm đó (`POST`/`PATCH /budgets`); app đổi `categorySlug` sang mã nhóm của người dùng. Cảnh báo ngưỡng đi kèm hạn mức sẵn có nên không có thẻ "bật cảnh báo" riêng |
| `create_goal` | Dự báo mục tiêu (`forecast_goal`, "mua X trong N tháng được không"), luôn có; hoặc phương án kéo dài của `goal_plan` khi chưa có mục tiêu cùng số tiền | `amountVnd` (giá người dùng nêu), `date` (hôm nay + N tháng, giờ Việt Nam) | Nút "Tạo mục tiêu": gọi `POST /saving-goals` luôn (người dùng đã bấm), thẻ "Đã tạo" trong chat có nút Hoàn tác |
| `change_goal_deadline` | `goal_plan` khi chưa khả thi và người dùng đã có mục tiêu Active cùng số tiền | `targetId` (id mục tiêu), `date` (hạn mới), `amountVnd` (số cần góp mỗi tháng theo hạn mới) | Nút "Đổi hạn": `PATCH /saving-goals/{targetId}` giữ tên và số tiền, chỉ đổi hạn |

Không có thẻ khi `status` khác `answered`, khi khoản mua đã khả thi với dữ liệu đã xác nhận, hoặc khi hoãn/giảm không đổi được gì. App chỉ hiện nút khi thẻ đủ dữ liệu cho việc của nó (vd `create_goal` không có `amountVnd` thì chỉ hiện chữ) và bỏ qua loại thẻ nó chưa biết.

**`POST /api/v1/forecast/purchase-reminders`** `{ "date": "yyyy-MM-dd" }` → `{ date, remindAt, created }`. Ngày phải sau hôm nay (giờ Việt Nam) và trong 365 ngày, nếu không 422 `PURCHASE_REMINDER_INVALID_DATE`. Hẹn lại cùng ngày trả `created: false`, không tạo thêm. Nhắc đi qua hộp thư `forecast_alerts` (kind `purchase_reminder`, `ScheduledFor` = 08:00 ngày đó, job gửi cảnh báo đến hạn sẵn có push). Nội dung cố định, không mang số tiền hay tên món; không phụ thuộc công tắc cảnh báo dự báo nhưng vẫn theo công tắc push. Cần migration `AddPurchaseReminderAlertKind` (nới `chk_forecast_alerts_kind`). Thêm loại thẻ mới = thêm một hằng ở `SuggestedActionKinds` và để công cụ tương ứng sinh ra nó cùng bằng chứng làm căn cứ.

### Đề xuất chủ động trên cảnh báo dự báo

Không cần người dùng hỏi: mỗi phần tử của `GET /api/v1/forecast/alerts` có thêm `actions[]` (`{kind, title, amountCents, categoryId, targetId}`), do **code** tính từ loại cảnh báo và payload đã lưu (không LLM, không bảng mới). Cảnh báo đã bị báo "không chính xác" thì `actions` rỗng; cảnh báo cũ thiếu trường thì không có nút thay vì đoán.

| `kind` | Cảnh báo | Trường dùng | App làm gì khi bấm |
|---|---|---|---|
| `raise_budget_limit` | `budget_risk` có `categoryId`, `projectedCents` (alert mới ghi thêm `categoryId`, `limitCents`) và mức dự kiến làm tròn **lên** chục nghìn đồng vượt hạn mức hiện tại | `amountCents`, `categoryId` (rỗng = ngân sách tổng) | Đặt lại hạn mức tháng của nhóm đó (như `set_budget_limit`) |
| `open_forecast` | `budget_risk`, `low_balance` | — | Mở màn dự báo (chỉ điều hướng) |
| `resume_recurring` | `bill_status_check` (khoản định kỳ bị tạm ngừng vì quá hạn lâu) | `targetId` = id khoản | `POST /recurring-items/{id}/resume` |

Các loại khác (`anomaly`, `bill_due`, `purchase_reminder`, `listener_health`...) chưa có hành động. Nút chỉ là đề xuất: người dùng bấm thì app mới gọi API có sẵn, cảnh báo không tự đổi dữ liệu nào. Thêm loại mới = thêm một hằng ở `ForecastAlertActionKinds` và một nhánh ở `ForecastAlertActions.For`.

### Thẻ kết quả (gợi ý cho app)

- **Kết luận** ở trên cùng: dịch `verdict` thành "Khả thi / Cần cân nhắc / Khó đạt" kèm kỳ tính; không hiện một "điểm AI" chung chung.
- **Dải** (với dự báo): hiển thị ba kịch bản (bảo thủ, cơ sở, lạc quan) lấy từ các bằng chứng `scenario_*`, `shortfall_*`; luôn ghi đây là dải dự báo, không phải lời hứa.
- **"Vì sao?"**: danh sách `evidence[]`, mỗi dòng gồm nhãn (`text`), giá trị, **nguồn** (`source` + `sourceName`), **ngày cập nhật** (`asOf`) và **độ tin cậy** (`confidence`, làm tròn 2 chữ số). Đánh dấu nhãn `đã xác nhận` / `người dùng nhập` / `ước tính` / `theo tài liệu`.
- Khi `usedTemplate` là `true`, không cần báo lỗi: nội dung vẫn đúng số, chỉ văn phong cố định.

## Cách app chuyển câu hỏi sang agent (một ô nhập, không có công tắc)

App luôn gửi câu người dùng cho chat (`POST /transactions/chat`) trước; backend không gọi agent từ chat (agent có hạn mức và chi phí riêng ở `/agent/ask`). App chuyển sang `POST /agent/ask` khi phản hồi của chat là một lời đáp văn bản (`action = reply`) với:

- `intent = ask_agent`: câu kế hoạch/dự báo ("nếu thu nhập giảm 30% thì sao", "bao lâu đủ tiền mua nhà", "sau 2 năm", "chia ngân sách"). Chat không ghi gì và không có `replyText`. Từ khóa nằm ở ai-service `intent_cues.json` (mẫu `agent_planning`, danh sách `agent_planning_phrases`), admin sửa được ở "Từ vựng hiểu câu chat". Đứng trước đánh giá mua vì `/forecast/purchase-check` chỉ nhìn tối đa 90 ngày.
- `intent = out_of_scope`: câu chat không hiểu. Agent có câu từ chối riêng và tra được tri thức đã duyệt, nên trả lời tốt hơn câu "chỉ giúp ghi chi tiêu"; agent lỗi thì app giữ lời đáp cũ của chat.

Các ý định khác (ghi chép, hỏi tổng, đánh giá mua, chào hỏi...) vẫn do chat xử lý. Lỗi của `/agent/ask` với câu `ask_agent`: app hiện thành một dòng của trợ lý.

## Công cụ của agent (danh sách cho phép)

| Công cụ | Việc | Thiếu dữ liệu |
|---|---|---|
| `forecast_goal` | Bao lâu đủ tiền mua món giá X trong N tháng, ba kịch bản | dưới 3 tháng lịch sử → `history_too_short` |
| `get_assumptions` | Tra giả định kinh tế đã duyệt (nguồn, ngày). `gold_price_growth` do hệ thống soạn nháp từ giá vàng, người duyệt mới dùng được | chưa có → `assumption_missing` |
| `search_knowledge` | Tra tri thức đã duyệt (RAG) | không có đoạn nào → `knowledge_missing` (ghi mã băm câu hỏi) |
| `simulate_purchase` | Số dư thấp nhất trong kỳ trước và sau khi mua; bọc `IPurchaseCheckService` (cùng số với màn "Mua món này có ổn không?") | engine báo `insufficient_data` → `purchase_data_missing`; có dòng ước tính → kết luận tối đa `tight` |
| `get_loans` | Phase 90: **đọc** sổ vay: tổng đang nợ (`loans_i_owe`), tổng người khác nợ (`loans_owed_to_me`), số khoản quá hạn (`loans_overdue_count`), tối đa 10 khoản `loan_N` (quá hạn và hạn gần trước; gốc còn lại, không lãi). Không tên người, không tiêu đề khoản; không có công cụ ghi | không có khoản `open` → `no_open_loans` |
| `runway` | Số dư đủ chi tiêu bao nhiêu tháng nếu thu nhập dừng | `history_too_short`, `no_spending_history` |
| `stress_test` | Thu nhập giảm X% trong N tháng thì sao | `history_too_short` |
| `sensitivity` | Yếu tố nào làm kết luận mục tiêu đổi nhiều nhất | `history_too_short` |
| `spending_summary` | Chi tiêu đã xác nhận theo nhóm trong `months` tháng gần nhất (1–12, **tính cả tháng này** đến hôm nay): tổng chi + tối đa 5 nhóm lớn nhất `cat_1..5` kèm phần trăm (phần trăm chỉ nằm trong chữ, không phải số tiền bằng chứng), các nhóm còn lại gộp thành "Các nhóm khác" `cat_other` | không có chi tiêu trong khoảng → `no_spending_history` |
| `plan_budget` | Chia ngân sách tháng: `save_vnd` (bắt buộc) để dành, phần còn lại của `income_vnd` (tùy chọn; bỏ trống = trung vị thu nhập 12 tháng) chia theo tỉ lệ chi của **chính người dùng** trong 3 tháng trọn gần nhất, không có thì theo mẫu admin `agent.budgetTemplate` trong system config (mẫu không hợp lệ — tổng ≠ 100, trùng slug, không phải số nguyên… — được coi như không có). Chỉ giữ 7 nhóm có tỉ lệ lớn nhất, các nhóm còn lại gộp thành "Các nhóm khác" trước khi chia. Mỗi nhóm làm tròn xuống nghìn đồng, phần dư dồn vào nhóm lớn nhất: tổng các nhóm + tiết kiệm = thu nhập đúng. Bằng chứng (≤ 12): `income`, `save`, `remaining` (thu nhập − tiết kiệm = tổng các nhóm), `alloc_1..7` theo vị trí (key `budget_alloc:<slug>`, cắt cho vừa 64 ký tự) + `alloc_other`, `savings_rate` | tiết kiệm ≥ thu nhập → `goal_exceeds_income`; không nêu thu nhập và lịch sử không cho ra thu nhập (trung vị ≤ 0) → `history_too_short`; không có tỉ lệ của người dùng lẫn mẫu admin → `no_budget_basis` |
| `budget_status` | **Đọc** ngân sách tháng này qua `IBudgetForecastService` (cùng số màn Dự báo): tối đa 4 ngân sách có % dự kiến cao nhất, mỗi cái `budget_N_spent` + `budget_N_limit` (≤ 8 bằng chứng; phần trăm và trạng thái chỉ trong chữ) | chưa có ngân sách → `no_budgets` |
| `goals_progress` | **Đọc** các mục tiêu tiết kiệm đang chạy: tối đa 4, hạn gần nhất trước, mỗi cái `goal_N_saved` + `goal_N_target` | chưa có mục tiêu → `no_goals` |
| `upcoming_items` | **Đọc** khoản thu/chi định kỳ đang theo dõi đến hạn trong 14 ngày tới (sớm nhất trước, tối đa 5): `upcoming_N` | không có khoản nào → `no_upcoming_items` |
| `propose_budget_limit` | **Đề xuất** (không đặt): `category_name` (tên nhóm người dùng nói, bỏ dấu/hoa thường, khớp đúng tên trước rồi tên chứa cụm, phải ra đúng một nhóm chi) + `limit_vnd` (phải đọc được từ câu hỏi như mọi tham số tiền). Trả bằng chứng `limit` và một thẻ `set_budget_limit` | không ra nhóm → `category_not_found`; nhiều nhóm → `category_ambiguous` (agent hỏi lại, không đoán) |

| `goal_plan` | **Đánh giá mục tiêu tiết kiệm** `target_vnd` trong `months` tháng (cả hai phải đọc được từ câu hỏi): cần góp ⌈mục tiêu / số tháng⌉ mỗi tháng (`plan_required`) so với khả năng góp thật từ lịch sử thu chi (`capacity_base` = thu Q50 − chi Q50, `capacity_conservative` = thu Q10 − chi Q90, cùng hai kịch bản của `forecast_goal`). Kết luận `verdict`: `feasible` nếu cần ≤ mức bảo thủ, `tight` nếu ≤ mức cơ sở, còn lại `infeasible`. Chưa khả thi thì tối đa 2 phương án **kéo dài** làm tròn lên số năm tròn (`infeasible` dùng mức cơ sở, `tight` dùng mức bảo thủ để có vùng đệm), mỗi cái `alt_N_monthly` = số cần góp mỗi tháng. Thẻ: `change_goal_deadline` nếu người dùng đã có mục tiêu Active cùng số tiền (mới nhất), ngược lại `create_goal`. Không giả định giá tăng | không còn dư ra đồng nào (mức dùng để kéo dài ≤ 0) → có kết luận nhưng không có phương án. **Dưới 3 tháng lịch sử → ước tính sơ khảo** nếu người dùng đã khai thu nhập hằng tháng (hồ sơ): dùng thu nhập đã khai (`income_declared`) và chi tiêu trung bình của các tháng đã ghi (`spend_avg`, nếu có); khả năng góp lấy theo thứ tự: (1) có chi tiêu đã ghi thì thu khai − chi trung bình (`spend_avg`, `capacity_base`); (2) chưa có thì **theo cách quản lý chính người dùng đã chọn từ khảo sát đầu vào**: thu khai × tổng giá trị đang có hiệu lực của các tham số có tên trong `agent.savingParamKeys` (cấu hình hệ thống, admin sửa; mặc định `goals_pct`, `saving_pct`, `contribution_pct`, `long_term_pct`, `financial_freedom_pct`, tối đa 100%) — vd 50/30/20 dành 20%, 6 hũ cộng tự do tài chính và tiết kiệm dài hạn; cách không có khóa nào (vd chỉ theo dõi) thì không ước tính. Có khả năng góp thì so số cần góp: `tight` nếu đủ, `infeasible` nếu không, **không bao giờ `feasible`**; không có thì chỉ nêu số cần góp, phần trăm thu nhập khai và hai phương án kéo dài thêm 1 và 2 năm, không có `verdict`. Luôn kèm bằng chứng `prelim_note` (khóa `goal_plan_preliminary`): "Số liệu này chỉ để tham khảo ... cần ghi chép chi tiêu thật (≥ 3 tháng) để tính chính xác và kỹ hơn"; **app hiện nó thành dòng ghi chú cố định dưới thẻ** (không phụ thuộc LLM có nhắc hay không). Không khai thu nhập và không có lịch sử → `history_too_short` |

**Mục tiêu bằng lời (AI thao tác app).** Câu lệnh đặt mục tiêu ĐỦ số tiền và hạn ("đặt mục tiêu mua nhà 500 triệu trong 3 năm") do bộ đọc chat nhận ra (`ChatAction.goal`): app hỏi agent TRƯỚC "mục tiêu ... có khả thi không, mỗi tháng cần góp bao nhiêu?" để nhận `goal_plan`. **Chỉ khi `verdict = feasible`** (và câu trả lời có bằng chứng `goal_target`, tức đúng là đánh giá mục tiêu) app mới **tạo ngay** (`POST /saving-goals`), hiện thẻ "Đã tạo" kèm nút **Hoàn tác** rồi phân tích. Chưa khả thi (`tight`, `infeasible`), agent lỗi hoặc thiếu dữ liệu thì **không tạo**: app hiện lời giải thích, thẻ phân tích kèm phương án (vd kéo dài hạn, nút "Tạo mục tiêu" theo hạn mới) rồi thẻ xác nhận để người dùng quyết định (tạo theo hạn đã nêu, chọn phương án khác, sửa hay hủy). Dữ liệu còn mỏng (ước tính sơ khảo) không bao giờ là `feasible` nên luôn qua thẻ xác nhận. Thiếu số tiền hoặc hạn thì vẫn là thẻ xác nhận như trước.

**Câu đặt mục tiêu luật không hiểu hạn.** Luật đọc được "N tháng/năm nữa/sau/tới", "trong (vòng) N ...", "kỳ hạn N ...", ngày cụ thể, "cuối tháng/năm". Câu có chữ chỉ thời gian mà luật không ra hạn ("đến năm 2029", "sau chừng 3 năm") thì, khi người dùng đã đồng ý AI, `apply_ai_goal` (ai-service, `chat_ai.py`) nhờ Gemini chỉ CỤM THỜI HẠN: AI chỉ thấy câu đã che mọi chữ số và chỉ trả số thứ tự ký hiệu của con số thời gian + đơn vị (hoặc ký hiệu của một năm) + tên ngắn; con số thật lấy từ câu gốc, ngày tính bằng cùng hàm của luật (`deadline_after`, `deadline_end_of_year`) và phải ở tương lai; số tiền không đổi; tên AI chỉ nhận khi mọi chữ có trong câu. Kết quả đặt `ai_assisted: true` (backend chuyển thành `goal.aiAssisted`): app **không tự tạo** mà hiện thẻ xác nhận "AI đọc hộ, bạn kiểm tra lại ngày nhé". Lời nhắc nằm ở `replies.ai_goal_prompt` trong `intent_cues.json` (admin sửa được). Thẻ `create_goal` của agent cũng tạo luôn khi người dùng bấm (không xác nhận lần hai); `change_goal_deadline` đổi hạn mục tiêu `targetId` (giữ tên và số tiền) bằng `PATCH /saving-goals/{id}`.

**Ngữ cảnh hỏi nối tiếp.** Sau mỗi lượt thành công backend nhớ các tham số nguyên (Redis, 30 phút, tối đa 6 lượt, không lưu chữ) để câu sau dùng lại: `price_vnd`/`horizon_months` (`forecast_goal`), `price_vnd` (`simulate_purchase`), `months` (`spending_summary`), `income_vnd`/`save_vnd` (`plan_budget`), `limit_vnd` (`propose_budget_limit`). Con số mang sang vẫn phải qua bộ kiểm tham số tiền như số trong câu hỏi.

**Câu chat chuyển sang agent.** Ngoài câu kế hoạch, các cụm "hạn mức", "ngân sách ... còn", "mục tiêu của tôi", "tiến độ mục tiêu", "sắp đến hạn", "khoản định kỳ" (`agent_planning_phrases` trong `intent_cues.json`) cũng được gắn `ask_agent`, để "đặt hạn mức ăn uống 3 triệu" không bị đọc thành khoản chi.

**Bản tin tuần.** `WeeklyDigestJob` (thứ Hai 08:00 giờ Việt Nam) gửi mỗi người có hoạt động gần đây một cảnh báo kind `weekly_digest` qua hộp thư `forecast_alerts`: số ngân sách có nguy cơ vượt (kèm tối đa 3 tên nhóm) và tiến độ % của mục tiêu gần hạn nhất. Dựng bằng code, không số tiền, không LLM; khoá chống lặp theo ngày thứ Hai của tuần; không có ngân sách lẫn mục tiêu thì không gửi; theo công tắc cảnh báo dự báo của người dùng. Có nút "Xem dự báo ngân sách". Cần migration `AddWeeklyDigestAlertKind`.

## Các API đi kèm (JWT của chính người dùng)

| API | Mô tả |
|---|---|
| `GET/PUT/DELETE /api/v1/agent/memory[/{key}]` | Sở thích dài hạn (khóa cho phép: `preferred_method`, `eating_out_weekends`, `notification_quiet_hours`, `communication_tone`; tối đa 200 ký tự) |
| `GET/POST/... /api/v1/recurring-items` | Khoản thu/chi định kỳ (Phase 54, module `Forecasting`): đề xuất tự phát hiện, người dùng xác nhận. Công cụ `simulate_purchase` của agent đọc chúng qua `IPurchaseCheckService`, không có API riêng của agent |
| `/api/v1/support/tickets` | Khiếu nại của chính người dùng (Phase 62). Agent không có luồng khiếu nại riêng |

## API cho nhân sự (vai trò Staff / OpsManager / Admin của Phase 61–65)

- `/api/v1/admin/assumptions` — giả định kinh tế: xem và soạn bản nháp cần quyền `ManageAgentContent` (mặc định Staff trở lên), duyệt / gỡ cần `ApproveAgentContent` (mặc định OpsManager trở lên). Admin chỉnh được hai quyền này trên trang Phân quyền; thiếu quyền thì 403.
- `/api/v1/admin/knowledge` — tài liệu tri thức và câu hỏi chưa có tri thức: cùng phân quyền như trên. `GET /documents` là danh sách (không kèm nội dung); `GET /documents/{id}` trả thêm `text` (toàn bộ nội dung) để người duyệt đọc trước khi bấm duyệt.
- Giao diện duyệt trên web quản trị (repo `FinMate_Admin`): menu vận hành **Tri thức AI** (`/ops/knowledge`) và **Giả định kinh tế** (`/ops/assumptions`), hiện cho tài khoản có `ManageAgentContent`; nút duyệt / gỡ chỉ hiện khi có `ApproveAgentContent`. Cấu hình tự cập nhật CPI nằm ở trang Admin **Số liệu kinh tế** (`/settings/economic`).
- `POST /api/v1/ops/tickets/{id}/assist` — trợ lý gợi ý cho nhân sự khi xử lý ticket: tóm tắt + bản nháp trả lời, **chỉ gợi ý** (không đổi ticket, không gửi gì cho khách; nhân sự tự gửi qua `/ops/tickets/{id}/messages`). `available: false` khi LLM sập; `verified: false` khi bản nháp bị bỏ vì chứa số tiền không có trong ticket. Ghi chú nội bộ không bao giờ gửi cho mô hình.

## Chỉ dùng khi phát triển

`POST /api/v1/internal/agent/run` (chạy agent trên hồ sơ giả lập) chỉ tồn tại ở môi trường Development khi `AGENT_EVAL_ENABLED=true` và cần header `X-Eval-Key`; ngoài Development bật cờ này thì backend **từ chối khởi động**. Không thuộc hợp đồng với app.

## Quyền riêng tư

Câu hỏi và bằng chứng được gửi tới nhà cung cấp LLM bên thứ ba (Gemini gói trả phí; không dùng gói miễn phí cho dữ liệu thật vì gói đó dùng dữ liệu để cải thiện sản phẩm). Người dùng đã chấp nhận ở bước chính sách quyền riêng tư; nhà cung cấp dự phòng chỉ hoạt động khi chủ dự án đặt `AGENT_BACKUP_MODEL`. Không log câu hỏi, số tiền hay khóa.

## Câu chữ của công cụ (admin sửa được)

Các câu cố định mà `goal_plan` ghép số vào (nhãn bằng chứng, ghi chú "chỉ để tham khảo", tiêu đề thẻ hành động, cách đọc "3 năm 6 tháng") là **mẫu có chỗ trống `{tên}`** trong cấu hình hệ thống `agent.texts` (khóa → mẫu), có phiên bản như mọi cấu hình khác. Danh mục khóa, mẫu mặc định và chỗ trống được phép nằm ở `AgentTexts.Catalog`; chỉ cần ghi khóa muốn đổi. Lưu cấu hình bị từ chối nếu khóa lạ, mẫu rỗng/quá 300 ký tự hoặc dùng chỗ trống không được phép; lúc chạy, mẫu sai thì rơi về mặc định của khóa đó. Con số luôn do code tính, và dòng máy đọc `verdict: ...` không nằm trong mẫu.

## Tạo ngân sách, khoản định kỳ, khoản vay nợ bằng chat

`POST /transactions/chat` trả `action: "item"` (kèm `item`) khi câu đủ thông tin để tạo; thiếu thông tin thì trả `action: "reply"`, `intent: "create_item"` và `replyText` là câu hỏi lại (app nhớ câu dở và ghép tin ngắn kế tiếp rồi gửi lại, lặp đến khi đủ). Đủ thông tin thì app TẠO NGAY qua API sẵn có và hiện thẻ "Đã lưu" kèm "Hoàn tác" (xóa/trả về như cũ).

| `item.kind` | Ví dụ | Đủ khi | App gọi |
|---|---|---|---|
| `budget` | "đặt ngân sách ăn uống 2 triệu" | có nhóm chi và số tiền | `POST /budgets` (nhóm đã có thì đổi hạn mức) |
| `recurring` | "gia hạn gói Claude Pro 600k hằng tháng ngày 5" | có số tiền, chu kỳ (weekly/monthly/yearly) và ngày thanh toán tiếp theo | `POST /recurring-items` (chi) |
| `loan` | "bạn An nợ tôi 700k" (cho vay), "tôi nợ bạn Bình 2 triệu" (đi vay) | có số tiền và tên người | `POST /loans` |

Câu "cho A vay", "A cho tôi mượn", "A trả nợ tôi" KHÔNG vào đây: đó là lúc tiền đi hoặc về, vẫn là giao dịch loại cho vay/mượn như trước. Mọi từ vựng nằm ở `ai-service/app/data/intent_cues.json` (nhóm `item_*`), code ở `app/services/item_parser.py`. Chỉ có khoản định kỳ dạng chi; thu định kỳ chưa hỗ trợ.

### Câu nối tiếp và trả nợ

- **Nhớ trong phiên:** app giữ tối đa 5 việc vừa tạo (bộ nhớ của phiên, mất khi đóng app hoặc đăng xuất) và gửi kèm việc mới nhất ở `itemContext` của `POST /transactions/chat` (chỉ số liệu có cấu trúc). Nhờ đó câu nối tiếp được hiểu: `operation: update` ("đổi thành 3 triệu"), `add` ("thêm 300k nữa"), `delete` ("xóa cái đó"), hoặc tạo tiếp cùng loại ("còn di chuyển thì 1 triệu"). Câu còn từ lạ ngoài lệnh và số tiền không phải nối tiếp. Server không lưu gì.
- **Trả nợ:** câu như "bạn An đã trả cho tôi 500k" / "tôi trả An 300k" vẫn được đọc như giao dịch thu/chi; ai-service chỉ gắn thêm gợi ý `item` (`operation: repay`). Backend tìm khoản vay CỦA NGƯỜI DÙNG đúng chiều, khớp tên và còn đủ số; đúng MỘT khoản thì trả `action: item` với `loanId`, `remainingCents`, `financialAccountId`, app hỏi xác nhận rồi gọi `POST /loans/{id}/repayments` (lần trả ghi cả giao dịch ví nên không ghi thêm giao dịch thu/chi). Không khớp, nhiều khoản cùng tên hoặc trả quá số còn lại thì giữ giao dịch thu/chi như cũ. Lần trả chưa có API hoàn tác nên luôn hỏi xác nhận.

### Tiết kiệm chi phí và chống LLM treo

- **Đường tắt:** câu quen về dữ liệu quản lý ("ngân sách còn bao nhiêu", "mục tiêu đến đâu rồi", "sắp đến hạn khoản nào", "tháng này / N tháng qua tiêu nhiều nhất vào đâu") được luật chọn công cụ ngay ở cổng (`POST /agent/gate` trả thêm `direct_tool`, `direct_args`), backend bỏ lượt LLM lập kế hoạch (lượt đó chiếm khoảng một nửa token của một lượt hỏi). Câu không khớp rõ ràng (không nói kỳ, có số tiền, hoặc khớp hai công cụ) vẫn qua LLM. Mẫu nhận dạng ở `intent_cues.json` (nhóm `agent_direct_*`), code ở `ai-service/app/agent/direct.py`; công cụ vẫn qua danh sách cho phép và kiểm tham số như mọi kế hoạch.
- **Chạy nền:** các lệnh "học" gửi ai-service sau khi lưu hoặc sửa giao dịch (nhớ danh mục cửa hàng, phản hồi sửa danh mục) được xếp hàng Hangfire (`IAiLearningQueue`, `AiLearningJob`) thay vì chờ trong request. Lỗi xếp hàng chỉ ghi log. Các lệnh khác (thông báo đẩy, email OTP) vẫn chạy trong request.

### Sửa/xóa theo tên, thu nhập định kỳ, ngân sách tổng

- **Sửa/xóa theo tên** (`item.operation`: `update_named` / `delete_named`, `kind`: `budget` | `loan` | `recurring` | `any`, tên ở `name`): "xóa ngân sách ăn uống", "đổi khoản Netflix thành 300k", "xóa khoản nợ của An". ai-service chỉ trả tên và loại; app tìm trong dữ liệu CỦA NGƯỜI DÙNG (ngân sách nhóm, khoản định kỳ đang chạy, khoản vay nợ). Không thấy hoặc nhiều mục khớp thì nói rõ, không đoán. Sửa số tiền làm ngay (Hoàn tác về số cũ); xóa luôn hỏi xác nhận. Không áp dụng cho giao dịch, mục tiêu, danh mục, tài khoản.
- **Thu nhập định kỳ:** "lương 15 triệu hằng tháng ngày 5" tạo khoản định kỳ `direction: inflow` (cần có chu kỳ; "nhận lương 15 triệu" một mình vẫn là khoản thu thường).
- **Ngân sách tổng:** "đặt ngân sách tổng 15 triệu" / "ngân sách cả tháng 15 triệu" → `kind: budget_total`, đặt hạn mức chung của tháng; đã có thì đổi (Hoàn tác trả mức cũ).

### Giải thích con số trong chat

Câu như "vì sao ô số dư ra 14,5 triệu?", "số này tính thế nào", "con số đó từ đâu ra" được ai-service nhận ra (`intent: explain_numbers`, cụm từ ở `explain_phrases` trong `intent_cues.json`). App giải thích bằng chính **bằng chứng của câu trả lời agent gần nhất** (mỗi số kèm mô tả và nguồn: đã xác nhận / bạn khai / ước tính / tài liệu, thêm ghi chú "chỉ để tham khảo" nếu có): không gọi AI và không tính lại, nên không thể lệch với con số đã hiện. Chưa có câu trả lời nào thì nói rõ là chưa có gì để giải thích. Chỉ giải thích câu trả lời của trợ lý hỏi đáp, chưa giải thích các thẻ ngân sách hay số liệu ở màn khác.
