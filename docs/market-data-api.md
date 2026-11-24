# API thông tin thị trường

Admin gửi link website. Hệ thống tự đọc mỗi 3 giờ và tự nhận dạng loại trang. Mọi API dưới đây chỉ dành cho `Admin`, thao tác ghi nằm trong nhật ký admin.

## Loại nguồn

| `kind` | Là gì | Cách đọc |
|---|---|---|
| `price_table` | Trang có bảng giá xăng | Đọc bằng luật, ra số chính xác |
| `feed` | RSS hoặc Atom của báo | Lọc tin về giá cả, rồi tải từng bài |
| `page` | Trang danh sách bài | Lấy link cùng tên miền có từ khóa về giá cả, rồi tải từng bài |
| `fuel_release` | Trang chỉ mục có link tới thông cáo điều hành giá xăng dầu (Bộ Công Thương). Chỉ nguồn hạng `official` | Mở link đầu tiên có chữ `fuelReleaseLinkText`, đọc câu "{tên hàng}: không cao hơn {số} đồng/lít" |

## Nguồn uy tín

Chỉ tên miền có trong `marketData.trustedDomains` mới được dùng. Hạng do admin đặt:

| Hạng | Là gì | Luật nhận giá |
|---|---|---|
| `official` | Nơi tự công bố con số: cơ quan nhà nước, doanh nghiệp phát hành giá | Một nguồn là đủ, và thắng nguồn `licensed` khi hai bên khác nhau. Hai nguồn `official` khác nhau thì chờ lượt sau |
| `licensed` | Báo hoặc trang tổng hợp ghi rõ số giấy phép và người chịu trách nhiệm nội dung | Phải đủ `minAgreeingSources` **đơn vị chủ quản** khác nhau cho đúng cùng con số |
| (không có tên) | Không rõ chủ quản, ví dụ webgia.com, giavang.org, giaxanghomnay.com | Không dùng |

- Thêm link ngoài danh sách trả 422 `MARKET_SOURCE_NOT_TRUSTED`, không tải trang nào.
- Nguồn đã có mà tên miền bị gỡ khỏi danh sách: lượt đọc bỏ qua, `lastErrorCode = MARKET_SOURCE_NOT_TRUSTED`, không tính là lỗi của trang.
- Hai tên miền cùng `owner` (so không phân biệt hoa thường) tính là một nguồn, cho cả giá lẫn sự kiện tin bài. Ví dụ tuoitre.vn và nld.com.vn cùng thuộc Cơ quan Báo và Phát thanh, Truyền hình TP.HCM.
- `economicFeeds.cpiIndexUrl` phải thuộc tên miền hạng `official`, nếu không cấu hình không lưu được.
- Danh sách mặc định gồm trang đã kiểm ngày 2026-11-23 (số giấy phép chép từ chân trang của chính trang đó). vnexpress.net và vneconomy.vn chưa có vì không thấy số giấy phép trong HTML tĩnh; muốn dùng thì admin thêm kèm số giấy phép.

Hệ thống không đọc trang cấm trong `robots.txt`, trang cần đăng nhập hoặc trang chặn công cụ tự động, và không tìm cách vượt qua. Vì vậy Facebook không dùng được. `robots.txt` không thay thế điều khoản sử dụng: trước khi phát hành thật phải kiểm tra điều khoản của từng trang.

## Nguồn

| API | Việc |
|---|---|
| `GET /api/v1/admin/market-sources` | Danh sách, kèm lần đọc gần nhất, mã lỗi và `trustTier` (`official`, `licensed`, `untrusted`) tính theo danh sách hiện tại |
| `POST /api/v1/admin/market-sources` | Thêm một link: `{ "url": "https://...", "name": "tùy chọn" }`. Trả `{ source, warnings }` |
| `POST /api/v1/admin/market-sources/bulk` | Thêm tối đa 50 link: `{ "urls": [...] }`. Báo kết quả từng link. Mỗi tên miền đọc tối đa 1 lần mỗi giây nên lô lớn có thể mất vài chục giây |
| `PATCH /api/v1/admin/market-sources/{id}` | Sửa `name`, `kind`, `isActive`. Không xóa nguồn |
| `POST /api/v1/admin/market-sources/{id}/test` | Đọc thử, không lưu. Trả giá đọc được hoặc 3 bài đầu tiên |

`warnings` khi thêm nguồn:

| Mã | Ý nghĩa |
|---|---|
| `AI_NOT_ALLOWED` | `robots.txt` chặn bot AI. Vẫn đọc được bảng giá, nhưng nội dung không được gửi cho AI |
| `NO_CONTENT` | Trang gần như trống, thường vì cần JavaScript. Hệ thống không chạy JavaScript |
| `NO_RELEVANT_LINKS` | Lúc thêm chưa thấy link nào về giá cả |

Mã lỗi 422: `MARKET_SOURCE_NOT_TRUSTED` (tên miền chưa có trong danh sách nguồn uy tín, hoặc đổi `kind` sang `fuel_release` cho nguồn không phải `official`), `MARKET_SOURCE_URL_INVALID`, `MARKET_SOURCE_ADDRESS_NOT_ALLOWED`, `MARKET_SOURCE_ROBOTS_DISALLOWED`, `MARKET_SOURCE_ROBOTS_UNAVAILABLE`, `MARKET_SOURCE_REFUSED`, `MARKET_SOURCE_NOT_FOUND`, `MARKET_SOURCE_TOO_LARGE`, `MARKET_SOURCE_UNSUPPORTED_CONTENT`, `MARKET_SOURCE_UNREACHABLE`. Link đã có trả 409 `MARKET_SOURCE_DUPLICATE`.

## Bài đã đọc

`GET /api/v1/admin/market-documents?sourceId=&status=&offset=&limit=`

Không có toàn văn, chỉ có tiêu đề, link, độ dài, trạng thái. Giữ 180 ngày.

| `status` | Ý nghĩa |
|---|---|
| `pending_ai` | Đã đọc được, chờ AI. Không có key hoặc AI lỗi thì ở lại chờ lượt sau |
| `processed` | AI đã đọc, có thể không rút ra được gì |
| `ai_not_allowed` | Nguồn hoặc trang chứa bài không cho phép dùng nội dung với AI |
| `no_content` | Không có đủ nội dung trong HTML |
| `blocked` | Trang hoặc `robots.txt` từ chối |
| `failed` | Lỗi không thử lại được, ví dụ bài đã bị gỡ |

## Giá xăng

| API | Việc |
|---|---|
| `GET /api/v1/admin/fuel-prices?fuelKey=&offset=&limit=` | Lịch sử giá đã xác nhận. Chỉ có dòng mới khi giá đổi. `sourceDomains`: tên miền đã cho đúng giá đó (null với giá nhập tay và giá cũ). `region2PriceCents` null khi nguồn chỉ có giá trần toàn quốc |
| `POST /api/v1/admin/fuel-prices` | Đường ngoại lệ khi nguồn hỏng hoặc sai: `{ "fuelKey", "region1PriceCents", "region2PriceCents", "note" }`, `region2PriceCents` bỏ trống được. Bắt buộc có ghi chú |

`fuelKey`: `e10_ron95_5`, `e10_ron95_3`, `e5_ron92_2`, `do_0001s_5`, `do_005s_2`, `kerosene_2k`. Giá tính bằng đồng.

Một giá được xác nhận khi có một nguồn `official`, hoặc đủ `minAgreeingSources` nguồn `licensed` thuộc các đơn vị chủ quản khác nhau cho đúng cùng cặp số trong một lượt đọc (xem "Nguồn uy tín"). Nguồn lệch giờ cập nhật thì chờ lượt sau. Đổi hơn `maxChangePercent` thì không nhận. Sau khi admin sửa tay, nguồn còn báo đúng giá cũ thì không ghi đè, nguồn đổi sang giá khác mới được tính.

**Thông cáo của Bộ Công Thương** (`fuel_release`, ví dụ trang chủ `https://moit.gov.vn/`): mỗi lượt mở trang chỉ mục, theo link đầu tiên có chữ `fuelReleaseLinkText` (mặc định "Thông tin về việc điều hành giá xăng dầu", cùng tên miền), rồi đọc câu "{tên hàng}: không cao hơn {số} đồng/lít" theo `fuelReleaseNames` (mặc định E5RON92 → `e5_ron92_2`, E10RON95-III → `e10_ron95_3`, điêzen 0.05S → `do_005s_2`). Thiếu `e5_ron92_2` hoặc `e10_ron95_3` thì nguồn báo `MISSING_FUELS`; không thấy link thì `FUEL_RELEASE_NOT_FOUND`. Đây là giá trần toàn quốc nên không có vùng 2, và không có E10 RON95 mức 5, DO 0,001S, dầu hỏa. Cảnh báo chỉ dùng vùng 1 nên không ảnh hưởng. Kiểu nguồn này chỉ được nhận ra khi tên miền là `official`.

## Giá vàng

Trang `price_table` có thể có giá xăng, giá vàng, hoặc cả hai; trang chỉ có giá vàng vẫn là nguồn đọc được. Mỗi lượt đọc cả hai bảng.

| API | Việc |
|---|---|
| `GET /api/v1/admin/gold-prices?goldKey=&offset=&limit=` | Lịch sử giá vàng đã xác nhận: `{ goldKey, buyCents, sellCents, firstSeenAt, origin, note, sourceDomains }`, đồng mỗi lượng. Chỉ có dòng mới khi giá đổi |
| `POST /api/v1/admin/gold-prices` | Nhập tay khi nguồn hỏng: `{ "goldKey", "buyCents", "sellCents", "note" }`. Bắt buộc ghi chú; sai khoảng giá, mua cao hơn bán hoặc chênh quá `maxSpreadPercent` trả 422 `GOLD_PRICE_INVALID`; mã lạ `GOLD_KEY_UNKNOWN`; giống giá đang lưu `GOLD_PRICE_UNCHANGED` |

`POST /market-sources/{id}/test` trả thêm `goldPrices`. `RefreshAsync` trả thêm `confirmedGold`.

**Cách đọc** (`marketData.gold`): tìm tên dòng (`rows[].names`, mặc định "Vàng miếng SJC", "SJC"), lấy các số ngay sau tên. Giữa các số chỉ được có số tăng giảm, phần trăm và chữ trong `rowWords` (mua, vào, bán, ra); gặp chữ khác (tên thương hiệu khác, câu văn) là dừng. Đơn vị lấy từ nhãn "Đơn vị: …" gần nhất **phía sau** dòng (trong 4.000 ký tự), quy ra đồng/lượng theo `units` (nghìn đồng/lượng, x1000đ/lượng, đồng/lượng, đồng/chỉ); không có nhãn hoặc nhãn lạ thì bỏ dòng. Số ngoài `[minPriceCents, maxPriceCents]` (mặc định 50 triệu đến 500 triệu) bị bỏ, đó là cột tăng giảm; hai số đầu còn lại là mua vào, bán ra. Số thập phân như "130.00" (triệu đồng) không được đọc.

**Xác nhận**: cùng luật với giá xăng (một nguồn `official`, hoặc đủ đơn vị chủ quản `licensed` khớp đúng cặp mua/bán), trần thay đổi là `gold.maxChangePercent` (10%, so trên giá mua).

Nguồn đọc được ngày 2026-11-23: `24h.com.vn/gia-vang-hom-nay-c425.html` và `cafef.vn/du-lieu/gia-vang-hom-nay/trong-nuoc.chn` (hai chủ quản khác nhau). Trang SJC, PNJ, DOJI nạp giá bằng JavaScript nên chưa đọc được; một trang hỏng là lượt đó không xác nhận được giá vàng. Chưa có sự cố giám sát riêng cho giá vàng, chỉ `lastErrorCode` của nguồn.

## Giả định nháp từ giá vàng

Sau lượt đọc có giá vàng mới, `MarketAssumptionDrafter` soạn bản **nháp** giả định theo `marketData.assumptionRules` (mặc định `sjc_bar` → `gold_price_growth`):

- Tốc độ = % thay đổi giá bán ra giữa giá mới nhất và giá đang áp dụng cách đó `windowDays` (365) ngày, quy tuyến tính ra một năm, điểm cơ bản. **Chưa có giá từ đủ lâu thì không soạn.** Admin có thể nhập tay vài mốc giá cũ (`POST /admin/gold-prices`, có ghi chú nguồn) để có nháp sớm hơn.
- Chỉ soạn khi lệch so với bản đang duyệt từ `minDeltaBps` (200 = 2 điểm %), hoặc chưa có bản nào. Đã có nháp hệ thống đúng số đó thì thôi; nháp hệ thống cũ khác số thì bị gỡ, để người duyệt chỉ thấy một bản.
- Nguồn: mẫu cố định "FinMate — giá … (bán ra) đã xác nhận: {giá cũ} ngày … → {giá mới} ngày …, quy ra mỗi năm", đường dẫn là một tên miền đã khớp. Giá chỉ có nhập tay, không có nguồn nào thì không soạn.
- **Không bao giờ tự duyệt** (khác CPI). Người có `ApproveAgentContent` duyệt ở `/ops/assumptions`; `AssumptionDto.createdBySystem` cho biết bản do hệ thống soạn. Tỉ lệ ngoài khoảng giả định cho phép thì bỏ, không soạn.

## Mục tiêu theo giá vàng

Người dùng gắn mục tiêu tiết kiệm với một chủ đề trong `marketData.goalTopics` (mặc định `gold` → giá bán ra của `sjc_bar`). API ở `docs/goals-and-reports-extras-api.md`.

Cảnh báo `goal_market_shift` (cờ `features.goalMarketAlerts`, mặc định tắt) chạy cùng job cảnh báo thị trường, với giá vàng mới nhất của mỗi loại chưa xét. Mục tiêu cá nhân đang chạy, đang theo chủ đề đó, nhận cảnh báo khi đủ cả:

1. Giá bán ra tăng từ `goalAlerts.minChangePercent` (5%) so với giá mốc của mục tiêu.
2. Tính lại ngày đạt (cùng cách màn mục tiêu, theo lịch sử góp) với số tiền tương đương `mục tiêu × giá mới / giá mốc`, làm tròn lên 100.000đ: ngày đạt lùi từ `goalAlerts.minShiftDays` (30) ngày, hoặc mục tiêu có hạn chuyển từ kịp hạn sang trễ hạn. Chưa góp lần nào thì không tính được ngày nên không báo.
3. Chưa báo mục tiêu này trong `goalAlerts.cooldownDays` (30) ngày.

Giá giảm không báo. Cờ tắt vẫn đánh dấu giá đã xét, bật cờ không gửi bù. Theo công tắc `forecastAlertsEnabled`, giờ yên lặng 21:00–08:00 như cảnh báo thị trường. Nội dung là mẫu cố định gồm nhãn chủ đề, tên mục tiêu của chính người dùng và số do code tính, không có lời khuyên mua bán. `data`: `goalId`, `topic`, `changeBps`, `shiftDays`, `adjustedTargetCents`. Nút: `update_goal_target` (`amountCents` = số tương đương, `targetId` = mục tiêu; app mở form sửa điền sẵn) và `open_goal`.

## Tin bài và AI

Mỗi bài được gửi cho `POST /api/v1/market-data/extract` của `ai-service`, cần khóa nội bộ. Chỉ gửi nội dung trang công khai, tối đa 20.000 ký tự.

Yêu cầu: `{ "title", "text", "published_at", "topics": [{ "key", "description" }] }`, 1 đến 20 chủ đề. Trả lời: `{ "status": "ok" | "not_configured" | "failed", "signals": [{ "topic", "direction", "fact_status", "change_percent", "effective_date", "evidence" }] }`.

`topic`: một mã trong `topics`, hoặc `other`. Chủ đề ngoài danh sách bị loại. `direction`: `up`, `down`, `stable`. `fact_status`: `actual` hoặc `forecast`.

Backend chỉ lưu một mục khi `evidence` có nguyên văn trong bài và `change_percent`, nếu có, nằm trong `evidence`. Việc này cho thấy thông tin đến từ bài, không chứng minh bài đúng. Mỗi ngày gửi tối đa `maxAiDocumentsPerDay` bài, tính cả bài lỗi, mỗi bài tính một lần trong ngày. AI lỗi 3 lần với cùng một bài thì bài chuyển `failed` với mã `AI_FAILED`, để không chặn các bài khác. Vì không phân biệt được lỗi do bài hay do nhà cung cấp, khi Gemini hỏng lâu, cứ 3 lượt đọc sẽ có một bài bị bỏ.

## Sự kiện

Nhiều trang cùng đưa tin một thay đổi giá thì thành sự kiện chờ duyệt. Chỉ tính nguồn có trong danh sách uy tín, đếm theo đơn vị chủ quản. Chỉ tính tin `actual`, tăng hoặc giảm, về loại chi phí đang bật và không có bảng giá, có ngày đăng rõ ràng trong 7 ngày, từ nguồn thuộc ít nhất `minEventDomains` tên miền. Tên miền con tính chung, và đếm theo nguồn admin đã thêm, không theo link trong bài. Tin về xăng không thành sự kiện vì đã có giá thật. Báo hay đăng lại của nhau nên số tên miền không chứng minh tin đúng, vì vậy mặc định phải có admin duyệt.

| API | Việc |
|---|---|
| `GET /api/v1/admin/market-events?status=&offset=&limit=` | Danh sách, kèm tiêu đề, link, đoạn trích từng bài. `status`: `pending`, `approved`, `rejected`, `expired` |
| `POST /api/v1/admin/market-events/{id}/approve` | Duyệt: `{ "note": "tùy chọn" }` |
| `POST /api/v1/admin/market-events/{id}/reject` | Từ chối. Tin đăng lại trong 7 ngày không tạo sự kiện mới |

Sự kiện đã xử lý trả 409 `MARKET_EVENT_NOT_PENDING`. Sự kiện đang mở không có tin mới sau 7 ngày thì chuyển `expired`.

## Cảnh báo

Bật bằng `features.marketAlerts`, mặc định tắt. Việc đọc dữ liệu vẫn chạy khi tắt, và bật cờ không gửi bù tin cũ. Cảnh báo có `kind` là `market_change`, đi qua `forecast_alerts`, theo công tắc `forecastAlertsEnabled` của người dùng. Từ 21:00 đến 08:00 thì hẹn tới 08:00.

Người dùng nhận cảnh báo khi có đủ:

1. **Giá tăng.** Loại có bảng giá (xăng): giá `referenceFuelKey` vùng 1 cao hơn giá ngay trước đó, và tăng từ `riseThresholdPercent` so với mốc `compareWindowDays` ngày trước, hoặc so với giá cũ nhất nếu chưa có đủ lịch sử. Loại khác: có sự kiện tăng giá đã duyệt. Tin về xăng không kích hoạt cảnh báo xăng.
2. **Thật sự chi cho khoản đó.** Tính từ giao dịch đã xác nhận trong `spendLookbackDays` ngày, ở mọi danh mục: từ `minCategoryTransactions` lần trở lên, lần gần nhất trong `lastSpendMaxDays` ngày, chiếm từ `minSpendSharePercent`% tổng chi.
3. **Ngân sách đang căng.** Xét ngân sách của danh mục chứa nhiều tiền nhất của khoản đó, không có thì ngân sách tổng. Căng nghĩa là dự báo `at_risk`, `over`, hoặc đã dùng từ ngưỡng T1 của người đó. Danh mục có ngân sách còn dư thì không xét ngân sách tổng.
4. **Đủ đáng kể.** Riêng loại có bảng giá: ước tính tốn thêm từ `minExtraCostCents`.
5. Chưa nhận cảnh báo cùng chủ đề trong `alertCooldownDays` ngày.

Một giao dịch thuộc một loại chi phí khi tên cửa hàng, mô tả hoặc tên danh mục chứa `transactionKeywords`, so theo cả từ, không phân biệt hoa thường và dấu, hoặc khi danh mục thuộc `categorySlugs`. Vì vậy danh mục đặt tên `Đi lại` hay `Sinh hoạt` đều được xét. Giao dịch không ghi rõ nội dung hoặc tiền mặt không ghi lại thì không nhận ra được. Hồ sơ này tính lại mỗi lần, không lưu và không gửi đi đâu.

Nội dung là mẫu cố định trong code, dùng `label` của loại chi phí và tên ngân sách của người dùng. Không chữ nào đến từ trang web hay AI, ngoài tên nguồn do admin đặt. Số tiền thêm chỉ ước tính cho loại có bảng giá: chi trung bình mỗi tháng nhân % tăng, làm tròn tới 1.000đ. `data` của cảnh báo có `topic`, `budgetId`, `monthlySpendCents`, `timesPerMonth`, và `extraCostCents` nếu có. App cần xử lý `kind` mới này.

## Loại chi phí

Nhóm `marketData.costTypes` trong `/admin/system-config`. Thêm loại mới không cần sửa code. Tối đa 20 loại, mã không trùng, không được là `other`, và phải bật ít nhất một loại.

| Trường | Ý nghĩa |
|---|---|
| `key` | Mã chủ đề, chữ thường và dấu gạch dưới |
| `label` | Tên trong câu cảnh báo: "Giá {label} đang tăng" |
| `aiDescription` | Mô tả gửi cho AI để nhận ra chủ đề trong bài |
| `newsKeywords` | Lọc bài trước khi gửi AI. Giữ phân biệt dấu, khác với từ khóa giao dịch |
| `transactionKeywords`, `categorySlugs` | Nhận diện giao dịch. Phải có ít nhất một trong hai. Từ khóa kết thúc bằng `*` khớp phần đầu của từ, như `evn*` với `EVNHCMC` |
| `transactionExcludes` | Cụm bị bỏ trước khi so khớp, như `tiền điện thoại` |
| `hasPriceTable` | Có giá thật ở `fuel_prices`. Chỉ `fuel` được bật, vì bộ đọc bảng chỉ đọc giá xăng |
| `enabled` | Tắt loại này |

Mặc định có `fuel`, `electricity`, `water`, `food_price`, `cooking_gas`. Danh sách từ khóa là điểm xuất phát, chưa đo trên giao dịch thật.

Tiền nhà, internet, gói đăng ký không thuộc phần này, vì tin báo không nói được giá riêng của từng người. Chúng dùng cảnh báo `price_change` của khoản định kỳ: khoản hằng tháng trả đúng một số tiền ở 2 kỳ trước đó thì tăng từ 1% là báo, khoản dao động chỉ báo khi lệch quá `amountTolerancePercent`. Khoản hằng năm không đủ 2 lần trong cửa sổ lịch sử nên chỉ báo khi lệch quá ngưỡng đó. Khoản người dùng tự thêm được báo nhưng giữ nguyên số họ đã nhập nếu lệch trong ngưỡng.

Nhiều lượt liền không xác nhận được giá xăng thì mở sự cố `market_data` mức thường. Một nguồn lỗi riêng lẻ chỉ hiện ở `lastErrorCode`.

## Cấu hình

Nhóm `marketData` trong `/admin/system-config`: `minAgreeingSources` (2), `maxDocumentsPerRun` (20), `minPriceCents` (10.000), `maxPriceCents` (60.000), `maxChangePercent` (20), `minEventDomains` (2), `autoPublishNewsEvents` (tắt), `maxAiDocumentsPerDay` (100), `referenceFuelKey` (`e10_ron95_3`), `riseThresholdPercent` (3), `compareWindowDays` (14), `alertCooldownDays` (14), `spendLookbackDays` (60), `minCategoryTransactions` (2), `lastSpendMaxDays` (45), `minSpendSharePercent` (2), `minExtraCostCents` (5.000), `costTypes`, `gold`, `goalTopics` (`{ key, label, goldKey, nameKeywords, enabled }`), `goalAlerts`, `trustedDomains` (danh sách `{ domain, tier, owner, licenseRef, checkedAt }`, tối đa 200; `licensed` bắt buộc `licenseRef`; `domain` là tên miền gốc viết thường), `fuelReleaseLinkText`, `fuelReleaseNames` (`{ name, fuelKey }`).

## An toàn

`SafeWebFetcher` là nơi duy nhất backend mở kết nối tới URL không cố định:

- Chỉ `https` cổng 443. Chặn địa chỉ nội bộ ở lúc mở kết nối, sau khi phân giải tên miền, nên chống được cả chuyển hướng và DNS rebinding.
- Tối đa 3 lần chuyển hướng, 2 MB, 15 giây cho cả lần tải. Không dùng proxy. Chỉ nhận HTML, JSON, XML, text.
- Khai báo `User-Agent: FinMateBot/1.0`, không giả trình duyệt.
