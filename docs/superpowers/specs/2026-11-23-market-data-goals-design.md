# Dữ liệu thị trường theo mục tiêu — bản thiết kế chờ duyệt

Ngày: 2026-11-23. Trạng thái: chủ sản phẩm đã đồng ý mọi đề xuất mặc định (2026-11-23). Backend phần 0–3 đã làm (chưa commit); app và admin web chưa làm.

Khác bản thiết kế khi làm: API chủ đề là `GET /api/v1/saving-goals/market-topics` (không phải `/market-data/goal-topics`); giá vàng dùng chung luật xác nhận của giá xăng; nguồn thông cáo Bộ Công Thương là loại nguồn `fuel_release` admin thêm như mọi nguồn (không phải địa chỉ trong cấu hình); mẫu mục tiêu chưa mang chủ đề mặc định (chưa có mẫu "mua vàng"); chưa có sự cố giám sát riêng cho giá vàng.

## Vì sao

Hôm nay "dữ liệu thị trường" chỉ phục vụ **chi tiêu**: giá xăng thật từ bảng giá, các loại khác (điện, nước, thực phẩm, gas) từ tin bài có người duyệt; cảnh báo `market_change` chỉ gửi người đang chi cho khoản đó và ngân sách đang căng. Mục tiêu tiết kiệm không nhận được gì từ thị trường:

- Màn mục tiêu (`GoalForecastCalculator`) tính ngày đạt theo tốc độ góp, coi số tiền mục tiêu là cố định.
- Agent (`forecast_goal`) có dùng giả định tăng giá (`get_assumptions`), nhưng giả định do người nhập tay, trừ CPI (`EconomicAssumptionRefresher` tự đọc Cục Thống kê mỗi tháng và **tự duyệt** khi lệch ≤ 1 điểm %). `goal_plan` ghi rõ "không giả định giá tăng".
- Chưa có giá vàng.

Bản này đề xuất một luật chung (0) chỉ lấy từ nguồn uy tín, và 3 phần, làm theo thứ tự: (1) giá vàng thật → (2) giả định nháp từ dữ liệu thị trường → (3) cảnh báo theo mục tiêu. Phần 3 cần phần 1; phần 2 cần đủ lịch sử giá nên ra sau cùng về thời gian dù code độc lập.

## Đã kiểm khi viết bản này

Đọc bằng `User-Agent: FinMateBot/1.0`, chỉ `robots.txt` + HTML tĩnh, không chạy JavaScript. Ngày 2026-11-23 khoảng 14:50.

| Trang | robots.txt | HTML tĩnh có giá? | Ghi chú |
|---|---|---|---|
| `sjc.com.vn/gia-vang-online`, `sjc.com.vn` | cho phép `*` (chặn riêng bot AI) | **Không** — khung bảng trống, số nạp bằng JS | Nguồn gốc nhưng không dùng được |
| `pnj.com.vn/site/gia-vang`, `/blog/gia-vang/` | cho phép | Không | |
| `giavang.doji.vn` → `banggia.doji.vn` | cho phép | Không (trang 44 ký tự chữ) | |
| `btmc.vn` | cho phép | Không thấy bảng | |
| `mihong.vn/vi/gia-vang-trong-nuoc` | cho phép | 404 | |
| `giavang.net` | cho phép | Không khớp dòng SJC | |
| `tygia.com` | **cấm `*`** | — | Tên miền đang rao bán. Loại |
| **`webgia.com/gia-vang/sjc/`** | cho phép | **Có**: "Vàng SJC 1L, 10L, 1KG 13.000.000 13.500.000", đơn vị **đồng/chỉ**; có cả nhẫn SJC 99,99 | Các vùng ngoài TP.HCM bị thay bằng chữ "webgia.com" (chống chép) → chỉ đọc dòng TP.HCM |
| **`webgia.com/gia-vang/`** | cho phép | **Có**: bảng theo hệ thống (SJC, PNJ, DOJI, Mi Hồng, Ngọc Thẩm), đồng/chỉ | Cùng tên miền với dòng trên, chỉ tính một phiếu |
| **`giavang.org`** | cho phép | **Có**: "Giá vàng Miếng SJC Mua vào 130.000 x1000đ/lượng", bảng theo hệ thống | Có cột tăng/giảm xen giữa |
| **`cafef.vn/du-lieu/gia-vang-hom-nay/trong-nuoc.chn`** | cho phép | **Có**: "vàng miếng SJC Mua vào 130,000 1,500 (1.17%) Bán ra 135,000", nghìn đồng/lượng | Nhẫn không có trong HTML tĩnh |
| **`24h.com.vn/gia-vang-hom-nay-c425.html`** | cho phép (trừ `/ajax/`…) | **Có**: "SJC 130,000 1500 135,000 1500 128,500 133,500" (hôm nay + hôm qua) | Có cả DOJI, BTMH, PNJ |

Có **4 tên miền** đọc được giá vàng miếng SJC và **khớp nhau** (mua 130.000.000đ, bán 135.000.000đ mỗi lượng). Cả 4 đều là trang tổng hợp chép từ SJC, nên khớp nhau chỉ chứng minh **đọc đúng**, không chứng minh giá đúng. Áp định nghĩa nguồn uy tín ở mục dưới thì chỉ còn **24h và cafef** (hạng B); webgia và giavang.org là hạng C, bị loại.

Kiểm thêm chân trang (giấy phép, người chịu trách nhiệm nội dung):

| Trang | Thông tin chủ quản trên trang | Hạng |
|---|---|---|
| `24h.com.vn` | "Giấy phép thiết lập trang thông tin điện tử tổng hợp" số 180/GP-STTTT, Sở TT&TT Hà Nội, cấp 11/12/2024 | B |
| `cafef.vn` | Ghi "Theo Trí thức trẻ", có địa chỉ và người chịu trách nhiệm nội dung; chưa thấy số giấy phép trong HTML tĩnh | B (cần xác nhận số giấy phép) |
| `petrolimex.com.vn` | Giấy phép số 1900/GP-TTĐT, Sở TT&TT Hà Nội | A, nhưng trang chủ không có bảng giá trong HTML tĩnh |
| `pvoil.com.vn/tin-gia-xang-dau` | — | A, nhưng trả 403 với bot → không dùng |
| `webgia.com`, `giavang.org` | Không có giấy phép hay đơn vị chủ quản | C |

Lãi suất tiết kiệm: `webgia.com/lai-suat/`, `cafef.vn/du-lieu/lai-suat-ngan-hang.chn`, `giavang.org/lai-suat/` **không** có bảng lãi suất trong HTML tĩnh → lãi suất chưa tự đọc được, giữ nhập tay. Chưa đọc điều khoản sử dụng của trang nào (`robots.txt` không thay thế điều khoản).

---

## Phần 0 — Chỉ lấy từ nguồn uy tín (áp cho mọi dữ liệu thị trường)

Hôm nay admin thêm được bất kỳ link nào. Đổi thành: **chỉ tên miền nằm trong danh sách nguồn uy tín mới được thêm và mới được tính**. Áp cho cả giá xăng, giá vàng, tin bài và CPI.

### Định nghĩa

| Hạng | Là gì | Ví dụ | Luật nhận giá |
|---|---|---|---|
| **A — nguồn gốc** | Nơi tự công bố con số của chính mình: cơ quan nhà nước (`.gov.vn`) hoặc doanh nghiệp phát hành giá | Cục Thống kê, Bộ Công Thương (giá xăng điều hành), Ngân hàng Nhà nước; Petrolimex, SJC, PNJ, DOJI, Bảo Tín Minh Châu, EVN, ngân hàng | **1 nguồn là đủ** |
| **B — có giấy phép** | Cơ quan báo chí, hoặc trang thông tin điện tử tổng hợp **ghi rõ số giấy phép và người chịu trách nhiệm nội dung** ở chân trang | vnexpress, tuoitre, thanhnien, dantri, vietnamnet, vneconomy, nld, cafef, 24h (mỗi trang phải kiểm giấy phép khi đưa vào danh sách) | **≥ 2 tên miền hạng B khớp đúng cùng con số** trong một lượt (luật `minAgreeingSources` hiện có, nay chỉ đếm hạng B) |
| **C — không rõ chủ quản** | Không công bố giấy phép hay đơn vị chịu trách nhiệm | webgia.com, giavang.org, giaxanghomnay.com | **Không dùng**: không thêm được, không tính |

Tin bài (điện, nước, thực phẩm, gas): sự kiện cần `minEventDomains` tên miền, nay chỉ đếm hạng A và B. CPI: `economicFeeds.cpiIndexUrl` phải thuộc hạng A.

Hạng là của **tên miền** (tên miền con tính chung, dùng lại `MarketDomains`), không phải của từng trang. Nguồn uy tín vẫn có thể sai hoặc chép của nhau; hạng chỉ cho biết có ai chịu trách nhiệm về con số. Luật khớp số, khoảng hợp lý và trần thay đổi vẫn giữ nguyên.

### Cấu hình admin

Nhóm mới `marketData.trustedDomains` trong `/admin/system-config` (có phiên bản, ghi nhật ký như mọi cấu hình):

| Trường | Ý nghĩa |
|---|---|
| `domain` | Tên miền gốc, ví dụ `24h.com.vn` |
| `tier` | `official` (A) hoặc `licensed` (B). Không có hạng C trong danh sách: không có tên = không dùng |
| `owner` | Đơn vị chủ quản, ví dụ "Công ty CP Quảng cáo trực tuyến 24H" |
| `licenseRef` | Số giấy phép hoặc căn cứ, ví dụ "180/GP-STTTT, Sở TT&TT Hà Nội, 11/12/2024". Bắt buộc với hạng B |
| `checkedAt` | Ngày người thêm đã kiểm giấy phép |

Không có mặc định trong code ngoài danh sách khởi đầu dưới đây; admin thêm/bớt không cần sửa code. Kiểm hợp lệ khi lưu: tên miền không trùng, hạng B phải có `licenseRef`.

Danh sách khởi đầu đề xuất (đã kiểm hôm nay: chỉ 24h có số giấy phép rõ; các dòng khác cần người kiểm điền `licenseRef` trước khi bật):

| Tên miền | Hạng |
|---|---|
| `nso.gov.vn`, `moit.gov.vn`, `sbv.gov.vn` | A |
| `petrolimex.com.vn`, `sjc.com.vn`, `pnj.com.vn`, `doji.vn`, `btmc.vn` | A |
| `24h.com.vn`, `cafef.vn` | B |
| `vnexpress.net`, `tuoitre.vn`, `thanhnien.vn`, `dantri.com.vn`, `vietnamnet.vn`, `vneconomy.vn`, `nld.com.vn` | B (RSS kinh tế đã đọc thử) |

### Schema / migration

Không bắt buộc: hạng đọc từ cấu hình lúc chạy theo tên miền của nguồn. Không lưu hạng vào `market_sources`, để đổi danh sách là có hiệu lực ngay, không lệch dữ liệu.

### API

| API | Thay đổi |
|---|---|
| `POST /api/v1/admin/market-sources` (+ `/bulk`) | Tên miền ngoài danh sách → 422 `MARKET_SOURCE_NOT_TRUSTED` |
| `GET /api/v1/admin/market-sources` | Thêm `trustTier` (`official` / `licensed` / `untrusted`) cho từng nguồn |
| `MarketDataRefreshJob` | Nguồn đã có mà tên miền bị bỏ khỏi danh sách: vẫn giữ dòng nhưng bỏ qua khi đọc, `lastErrorCode = SOURCE_NOT_TRUSTED` |
| Lịch sử giá (`fuel-prices`, `gold-prices`) | Thêm danh sách tên miền đã khớp ở lượt xác nhận, để thấy giá dựa trên nguồn nào |

### Màn hình

- **Admin web**, trang Thông tin thị trường: cột "Hạng nguồn" (badge A/B/không tin cậy); thông báo lỗi rõ khi thêm link ngoài danh sách. Trang Cấu hình hệ thống: bảng `trustedDomains`.
- **App**: không đổi. Có thể hiện "Nguồn: 24h, CafeF" ở dòng giá vàng của mục tiêu (lấy `owner`, chữ do admin đặt).

### Hệ quả cần biết

- **Giá vàng** còn đúng 2 nguồn hạng B đọc được (24h, cafef): đủ luật nhưng mong manh, một trang lỗi là lượt đó không xác nhận.
- **Giá xăng mất cả 2 nguồn đã đọc thử** (webgia, giaxanghomnay đều hạng C). Thay bằng thông cáo của Bộ Công Thương (hạng A), xem mục dưới.
- Thêm luật này **trước** khi admin thêm nguồn thật (DB dev đang có 0 nguồn), để khỏi phải dọn nguồn hạng C sau.

### Nguồn giá xăng hạng A/B (kiểm 2026-11-23)

| Trang | Hạng | Kết quả |
|---|---|---|
| `petrolimex.com.vn/ndi/thong-cao-bao-chi/…` (thông cáo điều chỉnh giá, ví dụ số 64/2026/PLX-TCBC ngày 19/11) | A | robots cho phép, HTML tĩnh có thông cáo nhưng **bảng giá là ảnh chụp** (`Screenshot … .png`) → không đọc được (không làm OCR) |
| `pvoil.com.vn/tin-gia-xang-dau` | A | 403 với bot → không dùng |
| **`moit.gov.vn/tin-tuc/thong-tin-ve-viec-dieu-hang-gia-xang-dau-ngay-19-11-2026.html`** | **A** | robots cho phép `/tin-tuc/`; HTML tĩnh có **câu mẫu cố định**: "Xăng E5RON92: không cao hơn 18.576 đồng/lít …; Xăng E10RON95-III: không cao hơn 19.100 đồng/lít …; Dầu điêzen 0.05S: không cao hơn 26.347 đồng/lít …; Dầu madút 180CST 3.5S: không cao hơn 18.671 đồng/kg"; có giờ hiệu lực "15 giờ 00’ ngày 19 tháng 11 năm 2026". **Dùng được** |
| `24h.com.vn/gia-xang-gia-dau-hom-nay-c46e1783.html` và bài hằng ngày | B | Chỉ có câu văn ("hạ 745 đồng/lít, từ 19.845 đồng/lít xuống 19.100 đồng/lít"), một câu có hai giá → không đọc bằng luật bảng được; chỉ một tên miền B nên cũng không đủ phiếu |
| `cafef.vn/du-lieu/gia-xang-dau.chn` | B | 404 |
| `vnexpress.net/chu-de/gia-xang-dau-3026` | B | Danh sách bài, không có bảng |

Đối chiếu: số của Bộ Công Thương (E5RON92 18.576, E10RON95-III 19.100, DO 0,05S 26.347) trùng số trong bài 24h cùng ngày.

**Đề xuất: đọc giá xăng từ thông cáo điều hành giá của Bộ Công Thương**, làm như CPI đang làm với Cục Thống kê:

- `FuelReleaseParser` (mới, cạnh `CpiReleaseParser`): trang chỉ mục → link đầu tiên có chữ hiển thị "Thông tin về việc điều hành giá xăng dầu ngày" → đọc câu "{tên hàng}: không cao hơn {số} đồng/lít" và giờ hiệu lực. Tên hàng → khóa: `Xăng E5RON92` → `e5_ron92_2`, `Xăng E10RON95-III` → `e10_ron95_3` (đúng `referenceFuelKey` mặc định của cảnh báo), `Dầu điêzen 0.05S` → `do_005s_2`. Bảng tên hàng → khóa nằm trong cấu hình.
- Là **nguồn hạng A nên một nguồn là đủ**; vẫn qua khoảng hợp lý và `maxChangePercent`. Thông cáo đã đọc (theo link) thì không đọc lại.
- Chạy cùng `MarketDataRefreshJob` (3 giờ). Kỳ điều hành giá thường là chiều thứ Năm hằng tuần, nên trễ tối đa khoảng 3 giờ.
- Trang chỉ mục: trang chủ `moit.gov.vn` có link tới thông cáo mới nhất (kiểm hôm nay); ô tìm kiếm của trang không trả kết quả trong HTML tĩnh. Admin thêm trang chỉ mục như mọi nguồn khác; hệ thống nhận ra loại nguồn mới `fuel_release` (chỉ với tên miền official). Trang đổi giao diện thì nguồn báo lỗi, nhiều lượt không xác nhận được giá thì mở sự cố `market_data`, giữ giá cũ.
- `FuelTableParser` (đọc bảng) giữ nguyên cho nguồn bảng hạng A/B nếu sau này có.

**Những gì mất so với bảng giá cũ:**

- Chỉ có **giá trần toàn quốc** (≈ vùng 1), **không có vùng 2**. Cảnh báo chỉ dùng vùng 1 nên không ảnh hưởng; trang admin và API lịch sử giá trả vùng 2 rỗng. Migration `AddFuelReleaseSourceAndNullableRegion2`: `fuel_prices.region2_price_cents` thành NULL được, thêm `fuel_prices.source_domains` (tên miền đã khớp), thêm loại nguồn `fuel_release`.
- Không có E10 RON95 mức V (`e10_ron95_5`), DO 0,001S-V, dầu hỏa: 3 khóa này không còn cập nhật tự động. Bảng `FuelKeys.Required` (đòi cả `e10_ron95_5`) phải nới cho nguồn thông cáo.
- Thêm dầu madút (đồng/kg) nếu muốn; mặc định bỏ qua vì người dùng cá nhân không mua.

---

## Phần 1 — Bảng giá vàng

### Cách làm

Làm giống hệt giá xăng, dùng lại toàn bộ đường đọc nguồn `price_table` (lịch 3 giờ, `SafeWebFetcher`, `RobotsRules`, giới hạn 1 lần/giây mỗi tên miền, sự cố `market_data`).

- **`GoldTableParser`** (mới, cạnh `FuelTableParser`): đọc chữ của trang (`HtmlTextExtractor.PageText`), tìm dòng theo tên (`Vàng SJC 1L…`, `vàng miếng SJC`, `SJC` trong bảng hệ thống), lấy các số sau tên.
  - **Đơn vị**: nhận ra chú thích đơn vị trên trang (`đồng/chỉ`, `nghìn đồng/lượng`, `x1000đ/lượng`, `triệu đồng/lượng`) và quy về **đồng/lượng** (1 lượng = 10 chỉ). Không thấy chú thích thì không đọc trang đó. Danh sách cách viết đơn vị nằm trong cấu hình (luật dự án: dữ liệu đổi được thì không hard-code).
  - **Cột tăng/giảm xen giữa** (`1,500 (1.17%)`, `1500`): sau khi quy đơn vị, bỏ mọi số ngoài khoảng `[minPriceCents, maxPriceCents]`; hai số đầu tiên còn lại là mua, bán. 24h có cả cột "hôm qua" nên chỉ lấy hai số hợp lệ đầu tiên.
  - Chỉ dòng **TP.HCM / toàn quốc** (dòng đầu tiên của SJC); không đọc vùng khác.
- **Khóa giá** (`gold_key`): `sjc_bar` (vàng miếng SJC). Nhẫn SJC 99,99 chỉ có ở webgia và giavang.org (hạng C) nên chưa lấy (xem Q1.2).
- **Xác nhận giá**: dùng lại luật của xăng — đủ `minAgreeingSources` (2) tên miền khác nhau cho **đúng cùng cặp mua/bán** trong một lượt; thêm: mua ≤ bán, chênh mua–bán ≤ `maxSpreadPercent`, đổi so với giá đang lưu ≤ `maxChangePercent` (vàng có ngày biến động mạnh, mặc định 10%, xem Q1.4). Chỉ ghi dòng mới khi giá đổi. Admin sửa tay được, nguồn báo đúng giá cũ thì không ghi đè (cùng luật xăng).
- **Nhận dạng nguồn**: `DetectKind` hiện coi `price_table` = có bảng xăng. Đổi thành "có bảng xăng **hoặc** bảng vàng"; mỗi lượt chạy cả hai bộ đọc trên trang `price_table`. Không thêm loại nguồn mới, không đổi bảng `market_sources`.
- Vàng **không** là loại chi phí (`costTypes`): mua vàng là để dành, cảnh báo "giá vàng đang tăng" cho người chi nhiều vào vàng sẽ đọc như lời khuyên đầu tư. Vàng chỉ là **chủ đề mục tiêu** (phần 3) và **nguồn giả định** (phần 2). Không gửi tin vàng cho AI đọc (đã có giá thật, giống xăng).

### Schema / migration

`AddGoldPrices` — bảng `gold_prices`, cùng dạng `fuel_prices`:

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `id` | uuid | |
| `gold_key` | varchar(32) | `sjc_bar`… |
| `buy_cents`, `sell_cents` | bigint | đồng/lượng; check `buy_cents > 0 AND sell_cents >= buy_cents` |
| `first_seen_at` | timestamptz | |
| `origin` | varchar | `sources` / `manual` (cùng enum `FuelPriceOrigin`, đổi tên chung nếu cần) |
| `created_by_user_id`, `note` | | bắt buộc `note` khi `manual` |
| `alerts_processed_at` | timestamptz null | để job cảnh báo mục tiêu (phần 3) đánh dấu |

Chỉ mục `(gold_key, first_seen_at DESC)`. Giữ vô thời hạn (vài dòng mỗi ngày).

### API (Admin)

| API | Việc |
|---|---|
| `GET /api/v1/admin/gold-prices?goldKey=&offset=&limit=` | Lịch sử giá đã xác nhận, phân trang trên server |
| `POST /api/v1/admin/gold-prices` | Nhập tay khi nguồn hỏng: `{ goldKey, buyCents, sellCents, note }`, `note` bắt buộc, ghi nhật ký admin |
| `POST /api/v1/admin/market-sources/{id}/test` | Đã có; trả thêm `goldReadings` bên cạnh giá xăng |

### Cấu hình admin

Nhóm mới `marketData.gold` trong `/admin/system-config`:

| Khóa | Mặc định | Ý nghĩa |
|---|---|---|
| `enabled` | true | Tắt thì không chạy bộ đọc vàng |
| `keys` | `[sjc_bar]` | Loại vàng được đọc |
| `minPriceCents`, `maxPriceCents` | 50.000.000 / 500.000.000 | Khoảng hợp lý mỗi lượng; bắt luôn lỗi nhầm đơn vị |
| `maxChangePercent` | 10 | Đổi hơn mức này trong một lượt thì không nhận |
| `maxSpreadPercent` | 10 | Chênh mua–bán tối đa |
| `unitPatterns` | xem trên | Cách viết đơn vị → hệ số quy đổi ra đồng/lượng |

`minAgreeingSources` dùng chung với xăng.

### Màn hình

- **Admin web** (`FinMate_Admin`), trang Thông tin thị trường: thêm tab **Giá vàng** (lịch sử + nút nhập tay) giống tab Giá xăng; kết quả "Đọc thử" hiện thêm giá vàng. Trang Cấu hình hệ thống: nhóm `gold`.
- **App**: không đổi ở phần này.
- Vận hành: admin thêm 2 nguồn hạng B đọc được là 24h và cafef (DB dev hiện có 0 nguồn). webgia và giavang.org là hạng C, không thêm (Phần 0).

### Rủi ro

- Chỉ còn đúng 2 nguồn hạng B; SJC đổi giá nhiều lần trong ngày, trang chép lệch giờ → nhiều lượt không đủ 2 nguồn khớp. Chấp nhận (lượt sau xác nhận), nhưng một trang hỏng là ngừng hẳn cho tới khi có nguồn thứ ba hoặc admin nhập tay.
- Nếu SJC/PNJ/DOJI sau này có giá trong HTML tĩnh (hạng A), một nguồn đó là đủ.
- Giao diện trang đổi → bộ đọc không ra số → sự cố `market_data`, giữ giá cũ, không đoán.
- Chưa kiểm điều khoản sử dụng của 24h và cafef trước khi phát hành thật.

---

## Phần 2 — Dữ liệu thị trường → giả định kinh tế **nháp**

### Cách làm

`MarketAssumptionDrafter` (mới, trong `FinMate.Application/Economics`, cạnh `EconomicAssumptionRefresher`), chạy sau mỗi lần xác nhận giá mới và một lần mỗi tháng:

1. Với mỗi luật trong `marketData.assumptionRules` (ví dụ `gold` → khóa giả định `gold_price_growth`), lấy giá đã xác nhận mới nhất và giá cách đây `windowDays` ngày (giá có hiệu lực tại ngày đó).
2. **Chưa đủ `windowDays` ngày lịch sử thì không làm gì.** Không ngoại suy từ vài tuần dữ liệu (vàng dao động mạnh, 3 tuần tăng 5% ≠ 80%/năm).
3. Tính tốc độ năm hóa bằng số nguyên (điểm cơ bản): với `windowDays = 365` đó đúng là % thay đổi 12 tháng.
4. Chỉ tạo nháp khi lệch so với bản **Approved** hiện tại ≥ `minDeltaBps`, hoặc chưa có bản nào.
5. Gọi `IAssumptionService.CreateDraftAsync` với người tạo = mã hệ thống (như CPI), `SourceName` là **mẫu cố định**: "FinMate — giá vàng miếng SJC (bán ra) đã xác nhận từ ≥2 nguồn: {giá cũ} ngày {d1} → {giá mới} ngày {d2}", `SourceUrl` = link một nguồn đã khớp ở lượt mới nhất, `AsOf` = ngày giá mới.
6. Đã có một **nháp do hệ thống tạo** cho khóa đó đang chờ → gỡ (`RetireAsync`) nháp cũ rồi tạo nháp mới, để người duyệt chỉ thấy một bản.
7. **Không bao giờ tự duyệt.** Bản nháp đi vào trang Giả định kinh tế có sẵn; người có `ApproveAgentContent` bấm duyệt thì agent mới dùng.

Lãi suất: không có nguồn HTML tĩnh (xem trên) → không tự tạo, vẫn nhập tay. Tin bài đã duyệt (ví dụ "giá điện tăng 4,8%") **không** thành giả định: đó là một lần tăng, không phải tốc độ năm; đổi một cái thành cái kia sẽ làm sai dự báo.

Agent: `forecast_goal` đã nhận `growth_key`; cần thêm `gold_price_growth` vào danh sách khóa planner được biết (ai-service, cấu hình chứ không hard-code) để "mua 2 lượng vàng sau 3 năm" dùng đúng giả định.

### Schema / migration

Không có. `economic_assumptions` đã đủ (phiên bản, trạng thái, nguồn, người tạo). Nháp hệ thống nhận ra qua `CreatedBy` = mã hệ thống.

### API

Không có API mới. `GET /api/v1/admin/assumptions` đã có; thêm trường `createdBySystem: bool` vào `AssumptionDto` để trang duyệt gắn nhãn.

### Cấu hình admin

`marketData.assumptionRules` (danh sách, admin sửa):

| Trường | Mặc định cho vàng |
|---|---|
| `priceSeries` | `gold:sjc_bar:sell` |
| `assumptionKey` | `gold_price_growth` |
| `windowDays` | 365 |
| `minDeltaBps` | 200 (2 điểm %) |
| `enabled` | true |

### Màn hình

- **Admin web** `/ops/assumptions`: nhãn "Hệ thống tạo từ dữ liệu thị trường" trên nháp; hiện nguồn + 2 mốc giá để người duyệt so. Không cần màn mới.
- **App**: không đổi (bằng chứng agent đã có `sourceName`, `asOf`).

### Rủi ro

- Phải chờ 365 ngày dữ liệu vàng mới có nháp đầu tiên (xem Q2.1 để đi nhanh hơn).
- Tốc độ quá khứ không dự báo được tương lai; giả định được duyệt sẽ được agent trích như một bằng chứng. Người duyệt là chốt chặn; câu trả lời vẫn ghi nguồn và ngày.
- Bất đồng với CPI: CPI đang **tự duyệt** khi lệch nhỏ, còn phần này không bao giờ tự duyệt (xem Q2.3).

---

## Phần 3 — Cảnh báo theo mục tiêu

### Cách làm

**Ghép mục tiêu với chủ đề thị trường bằng trường mới trên mục tiêu**, không qua mẫu mục tiêu: các mẫu hiện có (quỹ khẩn cấp, quỹ chi bất thường, mục tiêu cụ thể) không gắn với giá nào, nên "theo mẫu" không ghép được mục tiêu nào hôm nay. Mẫu chỉ là **gợi ý mặc định**: `DefaultParams` của mẫu có thể chứa `{"marketTopic":"gold"}` nếu admin tạo mẫu "Mua vàng".

- Người dùng chọn chủ đề khi tạo/sửa mục tiêu ("Mục tiêu này theo giá: Vàng"). App gợi ý khi tên mục tiêu chứa từ khóa của chủ đề (`nameKeywords`, ví dụ "vàng"); **không tự gắn**.
- Khi gắn, backend lưu **giá mốc** = giá đã xác nhận mới nhất của chuỗi giá đó (giá bán ra), và thời điểm.
- Job `GoalMarketAlertService` chạy sau mỗi giá vàng mới (`alerts_processed_at` của `gold_prices`, giống xăng):
  1. Lấy mục tiêu cá nhân `Active`, có chủ đề, chưa đạt.
  2. `số tiền điều chỉnh = TargetCents × giá mới / giá mốc` (số nguyên, làm tròn lên 100.000đ).
  3. Chạy `GoalForecastCalculator` hai lần (số gốc và số điều chỉnh) với cùng lịch sử góp → `lùi = ngày đạt (điều chỉnh) − ngày đạt (gốc)`.
  4. Báo khi **đủ cả**: giá đổi ≥ `minChangePercent` so với mốc; lùi ≥ `minShiftDays`; hoặc mục tiêu có hạn và đang `on_track` chuyển thành `behind`; chưa báo mục tiêu này trong `cooldownDays`.
  5. Không có tốc độ góp (chưa góp lần nào) → không tính được ngày → **không báo** (xem Q3.4).
- Gửi qua outbox có sẵn `forecast_alerts`: kind mới `goal_market_shift`, dedup `goal:{goalId}:{goldPriceId}`, theo công tắc `forecastAlertsEnabled` và giờ yên lặng 21:00–08:00 (dùng lại `MarketAlertRules.NotBefore`). Bật bằng cờ mới `features.goalMarketAlerts`, mặc định **tắt** cho tới khi app xử lý kind mới.
- **Nội dung là mẫu cố định** (không chữ nào từ web/AI; tên mục tiêu là chữ của chính người dùng):
  - Tiêu đề: "Giá {label} tăng ảnh hưởng mục tiêu của bạn"
  - Thân: "Giá {label} đã tăng {x}% kể từ khi bạn đặt mục tiêu "{tên}". Với mức góp hiện tại, ngày đạt dự kiến lùi khoảng {n} tháng. Số tiền tương đương bây giờ khoảng {số điều chỉnh}."
  - Không có chữ "nên mua/bán", không lời khuyên đầu tư.
- Nút hành động (theo `ForecastAlertActions.For`, code tính, không LLM):
  - `update_goal_target` — `amountCents` = số điều chỉnh, `targetId` = id mục tiêu → app gọi `PATCH /saving-goals/{id}` đổi số tiền. Đổi số tiền mục tiêu đang gắn chủ đề thì backend **đặt lại giá mốc** = giá hiện tại.
  - `open_goal` — chỉ điều hướng.
- Màn mục tiêu hiện thêm một dòng "Giá vàng {+x}% từ khi đặt mục tiêu, tương đương {số}" (đọc, không đổi gì).

Chỉ chủ đề **có chuỗi giá thật** (vàng) dùng được: tin bài chỉ có chiều tăng/giảm, không có số, nên không tính được ngày lùi. Chủ đề theo giả định (xe, nhà theo `car_price_growth`…) để giai đoạn sau (Q3.5).

### Schema / migration

1. `AddGoalMarketLink` — `saving_goals` thêm `market_topic varchar(32) NULL`, `market_baseline_cents bigint NULL`, `market_baseline_at timestamptz NULL`; check: ba cột cùng null hoặc cùng có giá trị; check `market_topic IS NULL OR user_id IS NOT NULL` (không áp cho mục tiêu nhóm, xem Q3.6).
2. `AddGoalMarketShiftAlertKind` — nới `chk_forecast_alerts_kind` thêm `goal_market_shift`.

### API (người dùng, JWT)

| API | Thay đổi |
|---|---|
| `GET /api/v1/market-data/goal-topics` | **Mới**. Chủ đề đang bật có giá đã xác nhận: `[{ key, label, priceCents, asOf }]` |
| `POST /saving-goals`, `PATCH /saving-goals/{id}` | Thêm `marketTopic` (tùy chọn; `null` để bỏ gắn). Chủ đề lạ/chưa có giá → 422 `MARKET_TOPIC_UNAVAILABLE` |
| `GET /saving-goals/{id}` và dự báo mục tiêu | Thêm khối `market`: `{ topic, label, baselinePriceCents, baselineAt, latestPriceCents, latestAt, changeBps, adjustedTargetCents, adjustedLikelyDate }`; null khi không gắn |
| `GET /forecast/alerts` | Kind mới `goal_market_shift`, `data`: `{ goalId, topic, changeBps, shiftDays, adjustedTargetCents }`, `actions` như trên |

### Cấu hình admin

`marketData.goalTopics` (danh sách):

| Trường | Mặc định |
|---|---|
| `key` | `gold` |
| `label` | `vàng` |
| `priceSeries` | `gold:sjc_bar:sell` |
| `nameKeywords` | `["vàng"]` |
| `enabled` | true |

`marketData.goalAlerts`: `minChangePercent` 5, `minShiftDays` 30, `cooldownDays` 30, `notifyOnDrop` false. Cờ `features.goalMarketAlerts` (tắt).

### Màn hình

- **App** (`FinMate_FE`): form tạo/sửa mục tiêu — ô chọn "Theo giá thị trường" (từ `goal-topics`) + gợi ý theo tên; chi tiết mục tiêu — dòng biến động giá; danh sách cảnh báo — kind `goal_market_shift` với 2 nút. Kind/nút lạ app đã bỏ qua nên bản cũ không vỡ.
- **Admin web**: Cấu hình hệ thống — nhóm `goalTopics`, `goalAlerts`; trang mẫu mục tiêu (nếu có sửa `DefaultParams`) — không cần màn mới.
- **Agent**: `goals_progress` có thể thêm bằng chứng giá mốc/giá mới; không bắt buộc ở đợt này.

### Rủi ro

- Vàng dao động mạnh → báo dồn dập. Chặn bằng ngưỡng %, ngưỡng ngày lùi, thời gian nghỉ 30 ngày mỗi mục tiêu.
- Giá mốc của mục tiêu cũ là giá **lúc gắn**, không phải lúc đặt mục tiêu (không có lịch sử giá trước đó). App phải ghi rõ "từ khi gắn theo giá vàng".
- Ngày đạt dựa trên tốc độ góp quá khứ; người mới góp 1–2 tháng thì ngày dao động — đã có `MonthsOfData`, có thể đòi tối thiểu 2 tháng.
- Dễ đọc thành tư vấn đầu tư. Câu chữ chỉ mô tả ảnh hưởng tới mục tiêu người dùng tự đặt.

---

## Ngoài phạm vi

Lãi suất tự động; quy mục tiêu ra đơn vị (lượng/chỉ) thay vì tiền; cảnh báo vàng cho chi tiêu; đọc trang cần JavaScript; dự báo giá tương lai.

## Thứ tự làm đề xuất

0. Phần 0 (danh sách nguồn uy tín + chặn khi thêm nguồn), kèm `FuelReleaseParser` đọc thông cáo giá xăng của Bộ Công Thương.
1. Phần 1 (backend + tab admin + thêm 24h, cafef) — bắt đầu tích lũy lịch sử ngay.
2. Phần 3 (backend sau cờ tắt → app → bật cờ).
3. Phần 2 (code xong sớm cũng được, nhưng nháp đầu tiên chỉ có khi đủ lịch sử).

---

## Quyết định cần chủ sản phẩm chốt

Mỗi mục có **đề xuất mặc định**; không trả lời thì làm theo mặc định.

**Phần 0 — nguồn uy tín**

- **Q0.1** Định nghĩa 3 hạng (A nguồn gốc: 1 nguồn đủ; B có giấy phép: ≥ 2 tên miền khớp; C không rõ chủ quản: không dùng). → *Mặc định: đồng ý.*
- **Q0.2** Danh sách tên miền khởi đầu như bảng ở Phần 0; ai chịu trách nhiệm kiểm và điền số giấy phép cho từng dòng hạng B? → *Mặc định: chủ sản phẩm (hoặc người được giao) kiểm trước khi bật; dòng chưa có `licenseRef` không lưu được.*
- **Q0.3** Giá xăng lấy từ thông cáo của Bộ Công Thương (hạng A, một nguồn), chấp nhận mất giá vùng 2 và 3 loại (E10 RON95-V, DO 0,001S-V, dầu hỏa)? → *Mặc định: đồng ý; làm `FuelReleaseParser` cùng lúc với Phần 0 để giá xăng không bị ngừng.*

**Phần 1 — giá vàng**

- **Q1.1** Nguồn: chỉ 24h và cafef (hạng B); trang chính thức SJC/PNJ/DOJI nạp giá bằng JS nên chưa dùng được. → *Mặc định: đồng ý, và chủ sản phẩm kiểm điều khoản sử dụng 2 trang trước khi phát hành.*
- **Q1.2** Loại vàng: chỉ vàng miếng SJC. Nhẫn SJC 99,99 chỉ có ở webgia và giavang.org (hạng C), nên theo Phần 0 không lấy được. → *Mặc định: chỉ vàng miếng SJC; thêm nhẫn khi có nguồn A/B.*
- **Q1.3** Dùng giá **bán ra** (giá người dùng phải trả khi mua) làm giá của mục tiêu. → *Mặc định: bán ra.*
- **Q1.4** Ngưỡng bỏ số lạ: đổi > 10% trong một lượt thì không nhận. → *Mặc định: 10%.*
- **Q1.5** Bảng riêng `gold_prices` (giống `fuel_prices`) hay gộp một bảng giá chung cho mọi loại? → *Mặc định: bảng riêng; gộp khi có loại giá thứ ba.*

**Phần 2 — giả định nháp**

- **Q2.1** Cửa sổ tính tốc độ: 365 ngày (phải chờ 1 năm mới có nháp đầu tiên) hay cho admin nhập tay lịch sử giá 12 tháng qua để có ngay? → *Mặc định: 365 ngày, và admin được nhập tay vài mốc giá cũ (đường `POST /admin/gold-prices` có ghi chú nguồn).*
- **Q2.2** Lệch bao nhiêu mới tạo nháp mới: 2 điểm %. → *Mặc định: 2 điểm %.*
- **Q2.3** CPI hiện đang **tự duyệt** khi lệch ≤ 1 điểm %. Giữ nguyên, hay chuyển CPI về luôn nháp cho khớp nguyên tắc "không tự áp"? → *Mặc định: giữ nguyên CPI (nguồn nhà nước, đã chạy); giả định từ giá thị trường thì luôn nháp.*

**Phần 3 — cảnh báo theo mục tiêu**

- **Q3.1** Gắn mục tiêu bằng **trường mới do người dùng chọn** (có gợi ý theo tên), mẫu mục tiêu chỉ là mặc định. → *Mặc định: đồng ý.*
- **Q3.2** Ngưỡng báo: giá đổi ≥ 5% so với mốc **và** ngày đạt lùi ≥ 30 ngày (hoặc mục tiêu có hạn chuyển sang trễ hạn); nghỉ 30 ngày mỗi mục tiêu. → *Mặc định: như trên.*
- **Q3.3** Có báo khi giá **giảm** (tin tốt: đạt sớm hơn) không? → *Mặc định: không.*
- **Q3.4** Mục tiêu chưa có lần góp nào (không tính được ngày): bỏ qua, hay vẫn báo "số tiền tương đương tăng lên X"? → *Mặc định: bỏ qua.*
- **Q3.5** Đợt này chỉ vàng. Có cần ngay chủ đề theo giả định (xe, nhà, giá chung theo CPI) — báo khi giả định đã duyệt đổi? → *Mặc định: để đợt sau.*
- **Q3.6** Mục tiêu **nhóm** có được gắn chủ đề không? → *Mặc định: không (chỉ mục tiêu cá nhân).*
- **Q3.7** Nút "Cập nhật số tiền mục tiêu" đổi số tiền ngay khi bấm (có Hoàn tác như thẻ agent), hay mở form sửa để người dùng tự xem? → *Mặc định: mở form sửa điền sẵn số mới.*
