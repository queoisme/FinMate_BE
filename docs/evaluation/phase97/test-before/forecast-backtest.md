# Backtest dự báo số dư — Phase 96, tầng 1

> Tệp do `FinMate.Backtest` sinh ra, không sửa tay. Dữ liệu là **dữ liệu tổng hợp**, không có dữ liệu người dùng thật.
> Chế độ duy nhất của tầng 1 là **đáp án**: mô hình được biết đúng các khoản định kỳ (chỉ với thông tin trước mốc), nên đây là **cận trên** của độ chính xác.

## Tái lập

| Mục | Giá trị |
|---|---|
| Commit | `không rõ` |
| SDK / runtime | SDK 9.0.321, .NET 9.0.23, X64 |
| Đặc tả bộ sinh | v1.2 (`docs/evaluation/forecast-backtest-generator-spec.md`) |
| Hạt gốc | 20261128 |
| Người gốc | 5 hồ sơ × 20 hạt giống |
| Bước mốc | 1 ngày, từ 2025-07-01 tới 2026-06-30 |
| Tham số `forecast` | phiên bản 0, SHA-256 `009930c219ba66ac` |
| Thế giới / dòng kết quả | 800 / 785100 |

Cùng image SDK, cùng hạt, cùng tham số → cùng tệp này từng byte (S4).

## Tóm tắt

| # | Kết quả |
|---|---|
| 1 | Tầm 7 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 4.912.197 đ (97,0% quy mô dòng tiền thực tế), trung vị 1.202.750 đ. Tốt hơn "số dư giữ nguyên" 3,0% (CI 95%: -17,8% – 21,7%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): -30,8% (CI 95%: -50,8% – -8,9%). |
| 2 | Tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 8.602.764 đ (123,9% quy mô dòng tiền thực tế), trung vị 2.384.750 đ. Tốt hơn "số dư giữ nguyên" -23,9% (CI 95%: -56,1% – 3,1%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): -43,1% (CI 95%: -73,1% – -17,6%). |
| 3 | Tầm 30 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 16.956.243 đ (230,6% quy mô dòng tiền thực tế), trung vị 5.258.750 đ. Tốt hơn "số dư giữ nguyên" -130,6% (CI 95%: -179,3% – -91,2%); so với baseline mạnh nhất còn lại (B-naive (lặp lại h+1 ngày trước)): -84,9% (CI 95%: -130,2% – -47,9%). |
| 4 | Hồ sơ stable_worker, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 1.213.791 đ, dự báo trung bình cao hơn thực tế 511.978 đ; tốt hơn "số dư giữ nguyên" 80,0% (CI 95%: 78,2% – 81,9%). |
| 5 | Hồ sơ freelancer, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 11.832.617 đ, dự báo trung bình thấp hơn thực tế 11.002.785 đ; tốt hơn "số dư giữ nguyên" -14,4% (CI 95%: -17,8% – -11,1%). |
| 6 | Hồ sơ student, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 2.042.369 đ, dự báo trung bình thấp hơn thực tế 34.215 đ; tốt hơn "số dư giữ nguyên" 15,7% (CI 95%: 11,4% – 19,7%). |
| 7 | Hồ sơ family, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 3.460.020 đ, dự báo trung bình cao hơn thực tế 1.666.387 đ; tốt hơn "số dư giữ nguyên" 67,8% (CI 95%: 66,2% – 69,4%). |
| 8 | Hồ sơ small_trader, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 24.465.025 đ, dự báo trung bình thấp hơn thực tế 24.465.025 đ; tốt hơn "số dư giữ nguyên" -376,8% (CI 95%: -425,9% – -330,8%). |
| 9 | Chi linh hoạt 15 ngày tới (tầm 14), chế độ đáp án: lệch WAPE 19,2% (CI 95%: 16,5% – 22,8%), thiên lệch -16,6% (âm = dự báo chi ít hơn thực tế). B-mean30: 22,1%, B-naive: 24,7%. |
| 10 | Dải thận trọng–lạc quan (cộng P75/P25 chi linh hoạt theo từng ngày), chế độ đáp án, K0 đối chứng: tỉ lệ số dư thật nằm trong dải — tầm 7: 36,5% (CI 95%: 32,3% – 40,8%), rộng trung bình 1.848.442 đ; tầm 14: 36,0% (CI 95%: 31,6% – 40,9%), rộng trung bình 3.467.126 đ; tầm 30: 36,0% (CI 95%: 30,8% – 41,4%), rộng trung bình 7.166.731 đ. Một dải P25–P75 đúng nghĩa của tổng sẽ chứa khoảng 50%. |
| 11 | K1 chi đột xuất lớn (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2922): sai số số dư tăng 22,6% (CI 95%: 8,3% – 44,0%) so với K0 đối chứng trên cùng người và mốc (10.633.434 đ so với 8.670.081 đ). |
| 12 | K2 thu nhập về trễ / đổi số tiền (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3000): sai số số dư tăng 10,9% (CI 95%: 3,5% – 20,6%) so với K0 đối chứng trên cùng người và mốc (9.326.455 đ so với 8.410.441 đ). |
| 13 | K3 bỏ một kỳ định kỳ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2325): sai số số dư tăng 8,8% (CI 95%: 6,2% – 12,1%) so với K0 đối chứng trên cùng người và mốc (9.466.726 đ so với 8.700.965 đ). |
| 14 | K4 khoản định kỳ đổi giá (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2936): sai số số dư tăng 1,6% (CI 95%: 0,6% – 2,9%) so với K0 đối chứng trên cùng người và mốc (9.316.193 đ so với 9.168.144 đ). |
| 15 | K5 Tết, chưa khai ngày lễ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 19,4% (CI 95%: 8,9% – 33,7%) so với K0 đối chứng trên cùng người và mốc (10.907.776 đ so với 9.134.016 đ). |
| 16 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 29,7% (CI 95%: 18,8% – 45,0%) so với K0 đối chứng trên cùng người và mốc (11.843.107 đ so với 9.134.016 đ). |
| 17 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 8,6% (CI 95%: 6,3% – 10,5%) so với K5 Tết, chưa khai ngày lễ trên cùng người và mốc (11.843.107 đ so với 10.907.776 đ). |
| 18 | K6 người dùng mới (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 6200): sai số số dư giảm 5,3% (CI 95%: -8,0% – -2,0%) so với K0 đối chứng trên cùng người và mốc (8.558.459 đ so với 9.035.329 đ). |

CI 95% lấy bằng bootstrap theo người gốc (1.000 lần). "Tốt hơn X%" = 1 − MAE mô hình / MAE baseline; âm là kém hơn.

## 1. Số dư theo tầm và phương pháp (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | MAE | Trung vị | Thiên lệch | % quy mô | Mô hình tốt hơn |
|---|---|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 4.912.197 đ | 1.202.750 đ | −3.678.839 đ | 97,0% | — |
| 7 | B-hold (số dư giữ nguyên) | 36500 | 5.064.785 đ | 3.062.000 đ | −764.755 đ | 100,0% | 3,0% (CI 95%: -17,8% – 21,7%) |
| 7 | B0 (chỉ khoản định kỳ) | 36500 | 3.755.711 đ | 2.303.500 đ | −121.433 đ | 74,2% | -30,8% (CI 95%: -50,8% – -8,9%) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 5.198.873 đ | 1.385.767 đ | −4.376.609 đ | 102,6% | 5,5% (CI 95%: 4,5% – 6,8%) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 7.702.704 đ | 4.855.000 đ | −7.075 đ | 152,1% | 36,2% (CI 95%: 21,1% – 49,8%) |
| 14 | Mô hình | 36500 | 8.602.764 đ | 2.384.750 đ | −6.664.732 đ | 123,9% | — |
| 14 | B-hold (số dư giữ nguyên) | 36500 | 6.943.635 đ | 5.269.000 đ | −1.431.531 đ | 100,0% | -23,9% (CI 95%: -56,1% – 3,1%) |
| 14 | B0 (chỉ khoản định kỳ) | 36500 | 6.012.744 đ | 4.009.000 đ | 6.024 đ | 86,6% | -43,1% (CI 95%: -73,1% – -17,6%) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 9.144.567 đ | 2.711.250 đ | −7.972.431 đ | 131,7% | 5,9% (CI 95%: 4,6% – 7,6%) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 11.437.376 đ | 9.077.000 đ | −9.717 đ | 164,7% | 24,8% (CI 95%: 3,3% – 42,8%) |
| 30 | Mô hình | 36500 | 16.956.243 đ | 5.258.750 đ | −13.544.858 đ | 230,6% | — |
| 30 | B-hold (số dư giữ nguyên) | 36500 | 7.354.432 đ | 3.850.000 đ | −3.002.356 đ | 100,0% | -130,6% (CI 95%: -179,3% – -91,2%) |
| 30 | B0 (chỉ khoản định kỳ) | 36500 | 11.182.451 đ | 8.150.000 đ | 241.335 đ | 152,1% | -51,6% (CI 95%: -85,5% – -20,5%) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 18.091.686 đ | 5.802.100 đ | −16.247.472 đ | 246,0% | 6,3% (CI 95%: 4,7% – 8,1%) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 9.169.534 đ | 4.722.500 đ | −69.846 đ | 124,7% | -84,9% (CI 95%: -130,2% – -47,9%) |

Thiên lệch = trung bình (dự kiến − thực tế); âm là dự báo thấp hơn thực tế. % quy mô = MAE / trung bình |số dư thực tế − số dư lúc dự báo|.

## 2. Theo kịch bản (chế độ đáp án, mọi hồ sơ, mô hình)

| Kịch bản | Tầm | MAE cả năm (n) | MAE mốc bị ảnh hưởng (n) | So với đối chứng cùng mốc |
|---|---|---|---|---|
| K0 đối chứng | 7 | 4.912.197 đ (36500) | — | — |
| K0 đối chứng | 14 | 8.602.764 đ (36500) | — | — |
| K0 đối chứng | 30 | 16.956.243 đ (36500) | — | — |
| K1 chi đột xuất lớn | 7 | 5.022.249 đ (36500) | 7.404.006 đ (1580) | K0: 52,2% (CI 27,7% – 87,5%) |
| K1 chi đột xuất lớn | 14 | 8.760.214 đ (36500) | 10.633.434 đ (2922) | K0: 22,6% (CI 8,3% – 44,0%) |
| K1 chi đột xuất lớn | 30 | 17.176.203 đ (36500) | 18.318.693 đ (5882) | K0: 8,1% (CI 1,7% – 18,9%) |
| K2 thu nhập về trễ / đổi số tiền | 7 | 5.190.761 đ (36500) | 7.024.405 đ (1600) | K0: 42,1% (CI 24,3% – 65,4%) |
| K2 thu nhập về trễ / đổi số tiền | 14 | 8.972.624 đ (36500) | 9.326.455 đ (3000) | K0: 10,9% (CI 3,5% – 20,6%) |
| K2 thu nhập về trễ / đổi số tiền | 30 | 17.526.448 đ (36500) | 17.390.425 đ (6200) | K0: 1,7% (CI -2,0% – 5,9%) |
| K3 bỏ một kỳ định kỳ | 7 | 4.992.230 đ (36500) | 5.569.704 đ (1245) | K0: 17,6% (CI 12,8% – 24,6%) |
| K3 bỏ một kỳ định kỳ | 14 | 8.704.542 đ (36500) | 9.466.726 đ (2325) | K0: 8,8% (CI 6,2% – 12,1%) |
| K3 bỏ một kỳ định kỳ | 30 | 17.093.329 đ (36500) | 18.542.683 đ (4734) | K0: 3,0% (CI 1,7% – 4,4%) |
| K4 khoản định kỳ đổi giá | 7 | 4.934.629 đ (36500) | 5.616.038 đ (1589) | K0: 2,9% (CI 1,2% – 5,2%) |
| K4 khoản định kỳ đổi giá | 14 | 8.631.373 đ (36500) | 9.316.193 đ (2936) | K0: 1,6% (CI 0,6% – 2,9%) |
| K4 khoản định kỳ đổi giá | 30 | 16.996.714 đ (36500) | 18.532.829 đ (5931) | K0: 0,8% (CI 0,3% – 1,5%) |
| K5 Tết, chưa khai ngày lễ | 7 | 5.078.656 đ (36500) | 7.000.815 đ (2900) | K0: 34,5% (CI 20,2% – 55,9%) |
| K5 Tết, chưa khai ngày lễ | 14 | 8.818.455 đ (36500) | 10.907.776 đ (3600) | K0: 19,4% (CI 8,9% – 33,7%) |
| K5 Tết, chưa khai ngày lễ | 30 | 17.189.425 đ (36500) | 19.273.493 đ (5200) | K0: 5,6% (CI 1,1% – 12,0%) |
| K5h Tết, đã khai ngày lễ 150% | 7 | 5.117.206 đ (36500) | 7.486.010 đ (2900) | K0: 43,8% (CI 29,3% – 65,9%); K5: 6,9% (CI 4,1% – 9,8%) |
| K5h Tết, đã khai ngày lễ 150% | 14 | 8.910.707 đ (36500) | 11.843.107 đ (3600) | K0: 29,7% (CI 18,8% – 45,0%); K5: 8,6% (CI 6,3% – 10,5%) |
| K5h Tết, đã khai ngày lễ 150% | 30 | 17.412.979 đ (36500) | 20.842.672 đ (5200) | K0: 14,2% (CI 8,8% – 22,4%); K5: 8,1% (CI 6,9% – 9,4%) |
| K6 người dùng mới | 7 | 4.860.571 đ (6200) | 4.860.571 đ (6200) | K0: -5,0% (CI -7,2% – -2,4%) |
| K6 người dùng mới | 14 | 8.558.459 đ (6200) | 8.558.459 đ (6200) | K0: -5,3% (CI -8,0% – -2,0%) |
| K6 người dùng mới | 30 | 17.031.560 đ (6200) | 17.031.560 đ (6200) | K0: -4,9% (CI -8,1% – -1,2%) |

Mốc bị ảnh hưởng: đoạn [T, T+h] chứa một ngày sự kiện của kịch bản (với K6: mọi mốc 14–75 ngày sau ngày bắt đầu). So với đối chứng: MAE kịch bản / MAE đối chứng − 1 trên cùng (người, mốc, tầm).

## 3. Theo hồ sơ (K0 đối chứng, tầm 14, chế độ đáp án)

| Hồ sơ | n | MAE mô hình | Thiên lệch | % quy mô | Tốt hơn B-hold | Tốt hơn B-mean30 | Tốt hơn B-naive | Trong dải |
|---|---|---|---|---|---|---|---|---|
| stable_worker | 7300 | 1.213.791 đ | 511.978 đ | 20,0% | 80,0% (CI 95%: 78,2% – 81,9%) | -9,0% (CI 95%: -16,4% – 0,0%) | 89,6% (CI 95%: 88,6% – 90,6%) | 67,7% (CI 95%: 65,5% – 69,7%) |
| freelancer | 7300 | 11.832.617 đ | −11.002.785 đ | 114,4% | -14,4% (CI 95%: -17,8% – -11,1%) | 6,3% (CI 95%: 5,6% – 7,1%) | 18,2% (CI 95%: 13,6% – 22,8%) | 24,6% (CI 95%: 21,4% – 27,6%) |
| student | 7300 | 2.042.369 đ | −34.215 đ | 84,3% | 15,7% (CI 95%: 11,4% – 19,7%) | 38,9% (CI 95%: 37,6% – 40,2%) | 52,8% (CI 95%: 50,3% – 55,1%) | 41,7% (CI 95%: 37,0% – 46,6%) |
| family | 7300 | 3.460.020 đ | 1.666.387 đ | 32,2% | 67,8% (CI 95%: 66,2% – 69,4%) | -12,6% (CI 95%: -15,1% – -10,0%) | 83,2% (CI 95%: 82,2% – 84,0%) | 46,0% (CI 95%: 43,5% – 48,8%) |
| small_trader | 7300 | 24.465.025 đ | −24.465.025 đ | 476,8% | -376,8% (CI 95%: -425,9% – -330,8%) | 4,3% (CI 95%: 3,7% – 5,0%) | -296,6% (CI 95%: -314,3% – -279,3%) | 0,0% (CI 95%: 0,0% – 0,0%) |

## 4. Theo mức tin cậy mô hình tự báo (chế độ đáp án, tầm 14, mọi kịch bản gộp theo từng kịch bản)

| Kịch bản | Tin cậy | n | MAE | % quy mô |
|---|---|---|---|---|
| K0 đối chứng | normal | 36500 | 8.602.764 đ | 123,9% |
| K0 đối chứng | low | 0 | — | — |
| K6 người dùng mới | normal | 4474 | 9.378.027 đ | 130,3% |
| K6 người dùng mới | low | 1726 | 6.434.039 đ | 89,3% |

## 5. Dải thận trọng–lạc quan theo tầm (chế độ đáp án, mọi hồ sơ)

Dải của engine = cộng chi linh hoạt P75 (thận trọng) / P25 (lạc quan) **của từng ngày**. Tổng các P75 lớn hơn P75 của tổng, nên dải rộng dần theo tầm; bảng này chỉ đo, không sửa (S5, quyết định riêng ở REQUIREMENTS §13.4).

| Kịch bản | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình | Rộng trung vị |
|---|---|---|---|---|---|
| K0 đối chứng | 7 | 36500 | 36,5% (CI 95%: 32,3% – 40,8%) | 1.848.442 đ | 1.587.375 đ |
| K0 đối chứng | 14 | 36500 | 36,0% (CI 95%: 31,6% – 40,9%) | 3.467.126 đ | 2.981.000 đ |
| K0 đối chứng | 30 | 36500 | 36,0% (CI 95%: 30,8% – 41,4%) | 7.166.731 đ | 6.169.625 đ |
| K1 chi đột xuất lớn | 7 | 36500 | 35,3% (CI 95%: 31,6% – 39,1%) | 1.855.341 đ | 1.595.750 đ |
| K1 chi đột xuất lớn | 14 | 36500 | 33,7% (CI 95%: 29,2% – 38,1%) | 3.480.098 đ | 2.995.000 đ |
| K1 chi đột xuất lớn | 30 | 36500 | 31,6% (CI 95%: 26,7% – 36,4%) | 7.193.601 đ | 6.201.250 đ |
| K2 thu nhập về trễ / đổi số tiền | 7 | 36500 | 35,2% (CI 95%: 31,0% – 39,0%) | 1.847.307 đ | 1.586.500 đ |
| K2 thu nhập về trễ / đổi số tiền | 14 | 36500 | 34,3% (CI 95%: 29,9% – 38,5%) | 3.464.896 đ | 2.980.625 đ |
| K2 thu nhập về trễ / đổi số tiền | 30 | 36500 | 34,1% (CI 95%: 29,0% – 39,6%) | 7.161.944 đ | 6.167.750 đ |
| K3 bỏ một kỳ định kỳ | 7 | 36500 | 35,5% (CI 95%: 31,3% – 39,5%) | 1.850.382 đ | 1.587.875 đ |
| K3 bỏ một kỳ định kỳ | 14 | 36500 | 34,8% (CI 95%: 30,4% – 38,8%) | 3.470.700 đ | 2.984.500 đ |
| K3 bỏ một kỳ định kỳ | 30 | 36500 | 34,9% (CI 95%: 29,5% – 40,1%) | 7.174.063 đ | 6.172.250 đ |
| K4 khoản định kỳ đổi giá | 7 | 36500 | 36,2% (CI 95%: 32,0% – 40,3%) | 1.851.477 đ | 1.591.500 đ |
| K4 khoản định kỳ đổi giá | 14 | 36500 | 35,7% (CI 95%: 31,1% – 40,4%) | 3.472.729 đ | 2.988.750 đ |
| K4 khoản định kỳ đổi giá | 30 | 36500 | 35,6% (CI 95%: 30,4% – 41,0%) | 7.178.238 đ | 6.183.375 đ |
| K5 Tết, chưa khai ngày lễ | 7 | 36500 | 35,8% (CI 95%: 31,8% – 39,9%) | 1.940.430 đ | 1.653.500 đ |
| K5 Tết, chưa khai ngày lễ | 14 | 36500 | 35,6% (CI 95%: 31,1% – 40,4%) | 3.639.414 đ | 3.102.250 đ |
| K5 Tết, chưa khai ngày lễ | 30 | 36500 | 35,0% (CI 95%: 29,2% – 40,2%) | 7.522.482 đ | 6.423.125 đ |
| K5h Tết, đã khai ngày lễ 150% | 7 | 36500 | 36,4% (CI 95%: 32,4% – 40,4%) | 1.980.234 đ | 1.684.000 đ |
| K5h Tết, đã khai ngày lễ 150% | 14 | 36500 | 36,0% (CI 95%: 31,7% – 40,6%) | 3.711.856 đ | 3.168.250 đ |
| K5h Tết, đã khai ngày lễ 150% | 30 | 36500 | 35,1% (CI 95%: 29,2% – 40,7%) | 7.667.016 đ | 6.559.500 đ |
| K6 người dùng mới | 7 | 6200 | 22,4% (CI 95%: 19,4% – 25,8%) | 1.148.936 đ | 1.039.250 đ |
| K6 người dùng mới | 14 | 6200 | 21,5% (CI 95%: 18,0% – 25,3%) | 2.156.587 đ | 1.964.625 đ |
| K6 người dùng mới | 30 | 6200 | 21,5% (CI 95%: 16,9% – 25,5%) | 4.457.874 đ | 4.037.875 đ |

Theo hồ sơ (K0 đối chứng):

| Hồ sơ | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình |
|---|---|---|---|---|
| stable_worker | 7 | 7300 | 62,6% (CI 95%: 61,2% – 64,0%) | 1.174.680 đ |
| stable_worker | 14 | 7300 | 67,7% (CI 95%: 65,5% – 69,7%) | 2.203.470 đ |
| stable_worker | 30 | 7300 | 73,6% (CI 95%: 70,5% – 76,5%) | 4.554.913 đ |
| freelancer | 7 | 7300 | 35,8% (CI 95%: 32,5% – 38,8%) | 1.793.590 đ |
| freelancer | 14 | 7300 | 24,6% (CI 95%: 21,4% – 27,6%) | 3.364.198 đ |
| freelancer | 30 | 7300 | 11,5% (CI 95%: 8,9% – 14,1%) | 6.953.649 đ |
| student | 7 | 7300 | 38,3% (CI 95%: 35,1% – 41,2%) | 1.016.012 đ |
| student | 14 | 7300 | 41,7% (CI 95%: 37,0% – 46,6%) | 1.905.681 đ |
| student | 30 | 7300 | 41,1% (CI 95%: 34,5% – 47,9%) | 3.938.923 đ |
| family | 7 | 7300 | 45,9% (CI 95%: 44,0% – 47,8%) | 2.551.690 đ |
| family | 14 | 7300 | 46,0% (CI 95%: 43,5% – 48,8%) | 4.785.720 đ |
| family | 30 | 7300 | 54,0% (CI 95%: 49,9% – 58,2%) | 9.892.110 đ |
| small_trader | 7 | 7300 | 0,0% (CI 95%: 0,0% – 0,0%) | 2.706.237 đ |
| small_trader | 14 | 7300 | 0,0% (CI 95%: 0,0% – 0,0%) | 5.076.561 đ |
| small_trader | 30 | 7300 | 0,0% (CI 95%: 0,0% – 0,0%) | 10.494.062 đ |

## 6. Chi linh hoạt (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | WAPE | Thiên lệch | MAPE (n, bị loại) |
|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 21,6% (CI 95%: 18,8% – 25,2%) | -16,6% | 25,1% (36500, 0) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 28,0% (CI 95%: 25,9% – 31,0%) | -0,1% | 36,3% (36500, 0) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 34,6% (CI 95%: 32,3% – 37,3%) | 0,1% | 43,7% (36500, 0) |
| 14 | Mô hình | 36500 | 19,2% (CI 95%: 16,5% – 22,8%) | -16,6% | 22,7% (36500, 0) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 22,1% (CI 95%: 19,7% – 25,0%) | -0,1% | 29,4% (36500, 0) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 24,7% (CI 95%: 22,2% – 27,9%) | -0,1% | 32,4% (36500, 0) |
| 30 | Mô hình | 36500 | 17,8% (CI 95%: 15,0% – 21,3%) | -16,7% | 22,3% (36500, 0) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 19,8% (CI 95%: 17,8% – 22,6%) | -0,2% | 26,1% (36500, 0) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 20,7% (CI 95%: 18,7% – 23,1%) | -0,2% | 26,4% (36500, 0) |

MAPE chỉ tính trên mốc có chi linh hoạt thực tế ≥ 100.000 đ.

## 7. Bộ sinh dữ liệu giả định gì

Chi tiết đầy đủ ở đặc tả v1.2. Tóm tắt:

- Năm hồ sơ: lương cố định, freelancer thu không đều, sinh viên (trợ cấp + làm thêm + học phí theo kỳ), gia đình (ba ví, một ví không tính vào dự báo, vay, quỹ nhóm, gửi tiết kiệm), tiểu thương (thu hằng ngày, nhập hàng hằng tuần dao động).
- Chi linh hoạt: số lần mỗi ngày theo Poisson, số tiền log-normal (đuôi nặng), hệ số thứ trong tuần, chi tăng 10–30% trong 5 ngày sau ngày nhận tiền lớn, mức chi trôi −1% đến +2% mỗi tháng. Mô hình không biết các hiệu ứng này.
- Khoản định kỳ trả lệch −2 đến +3 ngày; hóa đơn điện tăng theo mùa hè; nước dao động ±15%; 3% khoản mua sắm được hoàn tiền (đủ hoặc một phần).
- Tên nơi chi đôi khi viết hoa / thêm khoảng trắng / bỏ dấu, nhưng không có biến thể kiểu ngân hàng ("EVN HCMC 0123").
- Kịch bản cài vào: chi đột xuất lớn, thu nhập trễ rồi đổi số tiền, bỏ một kỳ hóa đơn, đổi giá (+5% và +25%), Tết 2026 (17/2/2026, nguồn trong đặc tả) có và không khai ngày lễ, người dùng mới.
- Vay, quỹ nhóm, chuyển ví, hoàn tiền dùng đúng loại giao dịch production lưu (Q16); repository giả được kiểm bằng test hợp đồng với Postgres thật.

## 8. Hạn chế

- **Dữ liệu tổng hợp**: con số cho biết mô hình làm gì trên các mẫu hành vi đã nêu, không phải độ chính xác trên người dùng thật.
- **Cỡ mẫu**: 100 người gốc; các mốc của cùng một người tương quan với nhau, nên CI lấy theo người, không theo mốc.
- **Chế độ đáp án là cận trên**: mô hình được biết trước khoản nào là định kỳ. Chế độ phát lại (tự phát hiện định kỳ) thuộc tầng 2, chưa chạy.
- Không có phản hồi người dùng (đánh dấu loại khỏi dự báo, xác nhận bất thường), không có cặp đảo hay liên kết trùng, không có giao dịch nháp chưa xác nhận.
- Mục tiêu tiết kiệm không được đo: khoản góp do bộ sinh tự đặt, đo nó là tự chấm bài.

## 9. Chạy lại

```
cd backend
dotnet run --project FinMate.Backtest -c Release -- --out ../docs/evaluation
```

Chạy trong container `mcr.microsoft.com/dotnet/sdk:9.0` với thư mục gốc repo được mount (như mọi lệnh backend). Không cần Postgres, Redis hay Docker compose.
