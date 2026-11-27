# Backtest dự báo số dư — mô hình `mean_dow_income_sumband_v3`

> Tệp do `FinMate.Backtest` sinh ra, không sửa tay. Dữ liệu là **dữ liệu tổng hợp**, không có dữ liệu người dùng thật.
> Chế độ duy nhất của tầng 1 là **đáp án**: mô hình được biết đúng các khoản định kỳ (chỉ với thông tin trước mốc), nên đây là **cận trên** của độ chính xác.

## Tái lập

| Mục | Giá trị |
|---|---|
| Commit | `6572548ad40f` |
| SDK / runtime | SDK 9.0.321, .NET 9.0.23, X64 |
| Đặc tả bộ sinh | v1.2 (`docs/evaluation/forecast-backtest-generator-spec.md`) |
| Hạt gốc | 20261128 |
| Người gốc | 5 hồ sơ × 20 hạt giống |
| Bước mốc | 1 ngày, từ 2025-07-01 tới 2026-06-30 |
| Tham số `forecast` | phiên bản 0, SHA-256 `009930c219ba66ac` |
| Phương pháp mô hình | `mean_dow_income_sumband_v3` (`ForecastMethods.Current`) |
| Thế giới / dòng kết quả | 800 / 785100 |

Cùng image SDK, cùng hạt, cùng tham số → cùng tệp này từng byte (S4).

## Tóm tắt

| # | Kết quả |
|---|---|
| 1 | Tầm 7 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 3.066.725 đ (60,5% quy mô dòng tiền thực tế), trung vị 1.257.865 đ. Tốt hơn "số dư giữ nguyên" 39,5% (CI 95%: 30,1% – 48,3%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): 18,3% (CI 95%: 11,4% – 25,6%). |
| 2 | Tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 4.352.208 đ (62,7% quy mô dòng tiền thực tế), trung vị 1.965.788 đ. Tốt hơn "số dư giữ nguyên" 37,3% (CI 95%: 27,7% – 46,6%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): 27,6% (CI 95%: 19,9% – 35,1%). |
| 3 | Tầm 30 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 6.804.939 đ (92,5% quy mô dòng tiền thực tế), trung vị 3.344.333 đ. Tốt hơn "số dư giữ nguyên" 7,5% (CI 95%: -0,5% – 15,9%); so với baseline mạnh nhất còn lại (B-naive (lặp lại h+1 ngày trước)): 25,8% (CI 95%: 21,9% – 29,9%). |
| 4 | Hồ sơ stable_worker, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 1.045.915 đ, dự báo trung bình thấp hơn thực tế 250.487 đ; tốt hơn "số dư giữ nguyên" 82,8% (CI 95%: 80,8% – 84,6%). |
| 5 | Hồ sơ freelancer, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 11.635.713 đ, dự báo trung bình cao hơn thực tế 223.679 đ; tốt hơn "số dư giữ nguyên" -12,5% (CI 95%: -16,0% – -9,0%). |
| 6 | Hồ sơ student, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 2.379.367 đ, dự báo trung bình thấp hơn thực tế 153.516 đ; tốt hơn "số dư giữ nguyên" 1,8% (CI 95%: -3,2% – 6,4%). |
| 7 | Hồ sơ family, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 2.930.323 đ, dự báo trung bình thấp hơn thực tế 102.143 đ; tốt hơn "số dư giữ nguyên" 72,7% (CI 95%: 71,0% – 74,3%). |
| 8 | Hồ sơ small_trader, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 3.769.724 đ, dự báo trung bình cao hơn thực tế 1.874 đ; tốt hơn "số dư giữ nguyên" 26,5% (CI 95%: 17,5% – 34,9%). |
| 9 | Chi linh hoạt 15 ngày tới (tầm 14), chế độ đáp án: lệch WAPE 16,4% (CI 95%: 13,8% – 19,9%), thiên lệch -0,6% (âm = dự báo chi ít hơn thực tế). B-mean30: 22,1%, B-naive: 24,7%. |
| 10 | Dải thận trọng–lạc quan (cộng P75/P25 chi linh hoạt theo từng ngày), chế độ đáp án, K0 đối chứng: tỉ lệ số dư thật nằm trong dải — tầm 7: 48,6% (CI 95%: 46,5% – 50,7%), rộng trung bình 4.552.282 đ; tầm 14: 45,8% (CI 95%: 43,8% – 47,7%), rộng trung bình 6.463.998 đ; tầm 30: 45,1% (CI 95%: 42,8% – 47,1%), rộng trung bình 10.000.754 đ. Một dải P25–P75 đúng nghĩa của tổng sẽ chứa khoảng 50%. |
| 11 | K1 chi đột xuất lớn (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2922): sai số số dư tăng 91,5% (CI 95%: 66,1% – 125,6%) so với K0 đối chứng trên cùng người và mốc (8.629.750 đ so với 4.505.504 đ). |
| 12 | K2 thu nhập về trễ / đổi số tiền (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3000): sai số số dư tăng 42,9% (CI 95%: 28,9% – 60,1%) so với K0 đối chứng trên cùng người và mốc (5.759.082 đ so với 4.028.904 đ). |
| 13 | K3 bỏ một kỳ định kỳ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2325): sai số số dư tăng 13,5% (CI 95%: 7,3% – 21,6%) so với K0 đối chứng trên cùng người và mốc (4.969.365 đ so với 4.379.850 đ). |
| 14 | K4 khoản định kỳ đổi giá (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2936): sai số số dư tăng 3,4% (CI 95%: 1,8% – 5,6%) so với K0 đối chứng trên cùng người và mốc (5.150.355 đ so với 4.979.312 đ). |
| 15 | K5 Tết, chưa khai ngày lễ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 68,1% (CI 95%: 43,1% – 102,9%) so với K0 đối chứng trên cùng người và mốc (7.828.350 đ so với 4.657.870 đ). |
| 16 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 72,1% (CI 95%: 45,1% – 111,5%) so với K0 đối chứng trên cùng người và mốc (8.018.332 đ so với 4.657.870 đ). |
| 17 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 2,4% (CI 95%: -0,2% – 5,5%) so với K5 Tết, chưa khai ngày lễ trên cùng người và mốc (8.018.332 đ so với 7.828.350 đ). |
| 18 | K6 người dùng mới (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 6200): sai số số dư tăng 17,2% (CI 95%: 10,3% – 26,3%) so với K0 đối chứng trên cùng người và mốc (5.111.428 đ so với 4.361.513 đ). |

CI 95% lấy bằng bootstrap theo người gốc (1.000 lần). "Tốt hơn X%" = 1 − MAE mô hình / MAE baseline; âm là kém hơn.

## 1. Số dư theo tầm và phương pháp (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | MAE | Trung vị | Thiên lệch | % quy mô | Mô hình tốt hơn |
|---|---|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 3.066.725 đ | 1.257.865 đ | −154.309 đ | 60,5% | — |
| 7 | B-hold (số dư giữ nguyên) | 36500 | 5.064.785 đ | 3.062.000 đ | −764.755 đ | 100,0% | 39,5% (CI 95%: 30,1% – 48,3%) |
| 7 | B0 (chỉ khoản định kỳ) | 36500 | 3.755.711 đ | 2.303.500 đ | −121.433 đ | 74,2% | 18,3% (CI 95%: 11,4% – 25,6%) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 5.198.873 đ | 1.385.767 đ | −4.376.609 đ | 102,6% | 41,0% (CI 95%: 28,0% – 52,0%) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 7.702.704 đ | 4.855.000 đ | −7.075 đ | 152,1% | 60,2% (CI 95%: 54,2% – 66,0%) |
| 14 | Mô hình | 36500 | 4.352.208 đ | 1.965.788 đ | −56.119 đ | 62,7% | — |
| 14 | B-hold (số dư giữ nguyên) | 36500 | 6.943.635 đ | 5.269.000 đ | −1.431.531 đ | 100,0% | 37,3% (CI 95%: 27,7% – 46,6%) |
| 14 | B0 (chỉ khoản định kỳ) | 36500 | 6.012.744 đ | 4.009.000 đ | 6.024 đ | 86,6% | 27,6% (CI 95%: 19,9% – 35,1%) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 9.144.567 đ | 2.711.250 đ | −7.972.431 đ | 131,7% | 52,4% (CI 95%: 40,3% – 62,3%) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 11.437.376 đ | 9.077.000 đ | −9.717 đ | 164,7% | 61,9% (CI 95%: 54,8% – 68,6%) |
| 30 | Mô hình | 36500 | 6.804.939 đ | 3.344.333 đ | 113.583 đ | 92,5% | — |
| 30 | B-hold (số dư giữ nguyên) | 36500 | 7.354.432 đ | 3.850.000 đ | −3.002.356 đ | 100,0% | 7,5% (CI 95%: -0,5% – 15,9%) |
| 30 | B0 (chỉ khoản định kỳ) | 36500 | 11.182.451 đ | 8.150.000 đ | 241.335 đ | 152,1% | 39,1% (CI 95%: 30,1% – 47,7%) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 18.091.686 đ | 5.802.100 đ | −16.247.472 đ | 246,0% | 62,4% (CI 95%: 52,4% – 70,3%) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 9.169.534 đ | 4.722.500 đ | −69.846 đ | 124,7% | 25,8% (CI 95%: 21,9% – 29,9%) |

Thiên lệch = trung bình (dự kiến − thực tế); âm là dự báo thấp hơn thực tế. % quy mô = MAE / trung bình |số dư thực tế − số dư lúc dự báo|.

## 2. Theo kịch bản (chế độ đáp án, mọi hồ sơ, mô hình)

| Kịch bản | Tầm | MAE cả năm (n) | MAE mốc bị ảnh hưởng (n) | So với đối chứng cùng mốc |
|---|---|---|---|---|
| K0 đối chứng | 7 | 3.066.725 đ (36500) | — | — |
| K0 đối chứng | 14 | 4.352.208 đ (36500) | — | — |
| K0 đối chứng | 30 | 6.804.939 đ (36500) | — | — |
| K1 chi đột xuất lớn | 7 | 3.342.732 đ (36500) | 7.829.081 đ (1580) | K0: 146,6% (CI 105,4% – 200,8%) |
| K1 chi đột xuất lớn | 14 | 4.842.345 đ (36500) | 8.629.750 đ (2922) | K0: 91,5% (CI 66,1% – 125,6%) |
| K1 chi đột xuất lớn | 30 | 7.663.310 đ (36500) | 10.224.784 đ (5882) | K0: 44,6% (CI 32,0% – 61,4%) |
| K2 thu nhập về trễ / đổi số tiền | 7 | 3.422.445 đ (36500) | 5.650.093 đ (1600) | K0: 87,6% (CI 63,3% – 120,1%) |
| K2 thu nhập về trễ / đổi số tiền | 14 | 4.849.711 đ (36500) | 5.759.082 đ (3000) | K0: 42,9% (CI 28,9% – 60,1%) |
| K2 thu nhập về trễ / đổi số tiền | 30 | 7.626.324 đ (36500) | 7.556.685 đ (6200) | K0: 19,5% (CI 10,6% – 29,5%) |
| K3 bỏ một kỳ định kỳ | 7 | 3.128.530 đ (36500) | 3.462.901 đ (1245) | K0: 17,5% (CI 8,9% – 28,7%) |
| K3 bỏ một kỳ định kỳ | 14 | 4.441.994 đ (36500) | 4.969.365 đ (2325) | K0: 13,5% (CI 7,3% – 21,6%) |
| K3 bỏ một kỳ định kỳ | 30 | 6.931.563 đ (36500) | 7.617.728 đ (4734) | K0: 7,0% (CI 3,4% – 11,8%) |
| K4 khoản định kỳ đổi giá | 7 | 3.089.971 đ (36500) | 4.004.032 đ (1589) | K0: 4,5% (CI 2,6% – 7,0%) |
| K4 khoản định kỳ đổi giá | 14 | 4.380.803 đ (36500) | 5.150.355 đ (2936) | K0: 3,4% (CI 1,8% – 5,6%) |
| K4 khoản định kỳ đổi giá | 30 | 6.838.957 đ (36500) | 7.649.751 đ (5931) | K0: 1,7% (CI 0,8% – 3,0%) |
| K5 Tết, chưa khai ngày lễ | 7 | 3.309.334 đ (36500) | 5.892.246 đ (2900) | K0: 81,4% (CI 53,8% – 118,9%) |
| K5 Tết, chưa khai ngày lễ | 14 | 4.755.355 đ (36500) | 7.828.350 đ (3600) | K0: 68,1% (CI 43,1% – 102,9%) |
| K5 Tết, chưa khai ngày lễ | 30 | 7.568.331 đ (36500) | 11.002.324 đ (5200) | K0: 52,2% (CI 33,7% – 79,7%) |
| K5h Tết, đã khai ngày lễ 150% | 7 | 3.305.772 đ (36500) | 5.847.421 đ (2900) | K0: 80,1% (CI 53,2% – 117,1%); K5: -0,8% (CI -2,9% – 1,5%) |
| K5h Tết, đã khai ngày lễ 150% | 14 | 4.774.093 đ (36500) | 8.018.332 đ (3600) | K0: 72,1% (CI 45,1% – 111,5%); K5: 2,4% (CI -0,2% – 5,5%) |
| K5h Tết, đã khai ngày lễ 150% | 30 | 7.653.384 đ (36500) | 11.599.331 đ (5200) | K0: 60,4% (CI 39,2% – 90,9%); K5: 5,4% (CI 2,4% – 8,5%) |
| K6 người dùng mới | 7 | 3.507.869 đ (6200) | 3.507.869 đ (6200) | K0: 11,5% (CI 6,8% – 17,3%) |
| K6 người dùng mới | 14 | 5.111.428 đ (6200) | 5.111.428 đ (6200) | K0: 17,2% (CI 10,3% – 26,3%) |
| K6 người dùng mới | 30 | 8.762.925 đ (6200) | 8.762.925 đ (6200) | K0: 29,6% (CI 18,3% – 42,5%) |

Mốc bị ảnh hưởng: đoạn [T, T+h] chứa một ngày sự kiện của kịch bản (với K6: mọi mốc 14–75 ngày sau ngày bắt đầu). So với đối chứng: MAE kịch bản / MAE đối chứng − 1 trên cùng (người, mốc, tầm).

## 3. Theo hồ sơ (K0 đối chứng, tầm 14, chế độ đáp án)

| Hồ sơ | n | MAE mô hình | Thiên lệch | % quy mô | Tốt hơn B-hold | Tốt hơn B-mean30 | Tốt hơn B-naive | Trong dải |
|---|---|---|---|---|---|---|---|---|
| stable_worker | 7300 | 1.045.915 đ | −250.487 đ | 17,2% | 82,8% (CI 95%: 80,8% – 84,6%) | 6,1% (CI 95%: 4,0% – 8,1%) | 91,0% (CI 95%: 89,9% – 92,0%) | 43,2% (CI 95%: 40,4% – 45,9%) |
| freelancer | 7300 | 11.635.713 đ | 223.679 đ | 112,5% | -12,5% (CI 95%: -16,0% – -9,0%) | 7,9% (CI 95%: 4,1% – 12,1%) | 19,6% (CI 95%: 14,3% – 24,4%) | 44,4% (CI 95%: 41,3% – 47,3%) |
| student | 7300 | 2.379.367 đ | −153.516 đ | 98,2% | 1,8% (CI 95%: -3,2% – 6,4%) | 28,9% (CI 95%: 25,7% – 31,5%) | 45,0% (CI 95%: 41,7% – 47,6%) | 60,2% (CI 95%: 58,2% – 62,3%) |
| family | 7300 | 2.930.323 đ | −102.143 đ | 27,3% | 72,7% (CI 95%: 71,0% – 74,3%) | 4,7% (CI 95%: 3,0% – 6,2%) | 85,8% (CI 95%: 84,8% – 86,6%) | 34,2% (CI 95%: 31,7% – 36,8%) |
| small_trader | 7300 | 3.769.724 đ | 1.874 đ | 73,5% | 26,5% (CI 95%: 17,5% – 34,9%) | 85,3% (CI 95%: 84,4% – 86,0%) | 38,9% (CI 95%: 35,9% – 41,6%) | 46,8% (CI 95%: 44,3% – 49,2%) |

## 4. Theo mức tin cậy mô hình tự báo (chế độ đáp án, tầm 14, mọi kịch bản gộp theo từng kịch bản)

| Kịch bản | Tin cậy | n | MAE | % quy mô |
|---|---|---|---|---|
| K0 đối chứng | normal | 36500 | 4.352.208 đ | 62,7% |
| K0 đối chứng | low | 0 | — | — |
| K6 người dùng mới | normal | 4474 | 4.584.168 đ | 63,7% |
| K6 người dùng mới | low | 1726 | 6.478.149 đ | 89,9% |

## 5. Dải thận trọng–lạc quan theo tầm (chế độ đáp án, mọi hồ sơ)

Dải của engine = cộng chi linh hoạt P75 (thận trọng) / P25 (lạc quan) **của từng ngày**. Tổng các P75 lớn hơn P75 của tổng, nên dải rộng dần theo tầm; bảng này chỉ đo, không sửa (S5, quyết định riêng ở REQUIREMENTS §13.4).

| Kịch bản | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình | Rộng trung vị |
|---|---|---|---|---|---|
| K0 đối chứng | 7 | 36500 | 48,6% (CI 95%: 46,5% – 50,7%) | 4.552.282 đ | 2.449.657 đ |
| K0 đối chứng | 14 | 36500 | 45,8% (CI 95%: 43,8% – 47,7%) | 6.463.998 đ | 3.523.847 đ |
| K0 đối chứng | 30 | 36500 | 45,1% (CI 95%: 42,8% – 47,1%) | 10.000.754 đ | 5.871.839 đ |
| K1 chi đột xuất lớn | 7 | 36500 | 54,3% (CI 95%: 52,3% – 56,2%) | 5.110.731 đ | 3.638.979 đ |
| K1 chi đột xuất lớn | 14 | 36500 | 49,4% (CI 95%: 47,7% – 51,3%) | 7.255.536 đ | 5.222.906 đ |
| K1 chi đột xuất lớn | 30 | 36500 | 44,6% (CI 95%: 42,6% – 46,6%) | 11.222.317 đ | 8.136.455 đ |
| K2 thu nhập về trễ / đổi số tiền | 7 | 36500 | 47,9% (CI 95%: 45,9% – 49,9%) | 4.781.246 đ | 2.531.189 đ |
| K2 thu nhập về trễ / đổi số tiền | 14 | 36500 | 44,3% (CI 95%: 42,3% – 46,3%) | 6.788.528 đ | 3.713.619 đ |
| K2 thu nhập về trễ / đổi số tiền | 30 | 36500 | 43,3% (CI 95%: 41,0% – 45,4%) | 10.501.592 đ | 6.250.424 đ |
| K3 bỏ một kỳ định kỳ | 7 | 36500 | 47,5% (CI 95%: 45,4% – 49,5%) | 4.552.285 đ | 2.449.657 đ |
| K3 bỏ một kỳ định kỳ | 14 | 36500 | 44,4% (CI 95%: 42,4% – 46,2%) | 6.464.003 đ | 3.523.847 đ |
| K3 bỏ một kỳ định kỳ | 30 | 36500 | 43,3% (CI 95%: 41,2% – 45,4%) | 10.000.763 đ | 5.871.839 đ |
| K4 khoản định kỳ đổi giá | 7 | 36500 | 48,2% (CI 95%: 46,2% – 50,3%) | 4.552.289 đ | 2.449.657 đ |
| K4 khoản định kỳ đổi giá | 14 | 36500 | 45,4% (CI 95%: 43,4% – 47,4%) | 6.464.009 đ | 3.523.847 đ |
| K4 khoản định kỳ đổi giá | 30 | 36500 | 44,8% (CI 95%: 42,7% – 47,1%) | 10.000.770 đ | 5.871.839 đ |
| K5 Tết, chưa khai ngày lễ | 7 | 36500 | 52,4% (CI 95%: 50,8% – 53,9%) | 5.186.773 đ | 3.936.643 đ |
| K5 Tết, chưa khai ngày lễ | 14 | 36500 | 49,7% (CI 95%: 47,9% – 51,5%) | 7.363.310 đ | 5.614.396 đ |
| K5 Tết, chưa khai ngày lễ | 30 | 36500 | 47,4% (CI 95%: 45,6% – 49,3%) | 11.388.623 đ | 8.664.297 đ |
| K5h Tết, đã khai ngày lễ 150% | 7 | 36500 | 52,6% (CI 95%: 51,0% – 54,1%) | 5.186.773 đ | 3.936.643 đ |
| K5h Tết, đã khai ngày lễ 150% | 14 | 36500 | 49,7% (CI 95%: 48,0% – 51,3%) | 7.363.310 đ | 5.614.396 đ |
| K5h Tết, đã khai ngày lễ 150% | 30 | 36500 | 47,1% (CI 95%: 45,2% – 48,8%) | 11.388.623 đ | 8.664.297 đ |
| K6 người dùng mới | 7 | 6200 | 38,9% (CI 95%: 35,9% – 42,1%) | 4.093.956 đ | 1.452.639 đ |
| K6 người dùng mới | 14 | 6200 | 39,4% (CI 95%: 35,6% – 43,1%) | 6.390.813 đ | 2.160.220 đ |
| K6 người dùng mới | 30 | 6200 | 38,2% (CI 95%: 34,0% – 42,5%) | 11.180.533 đ | 3.695.959 đ |

Theo hồ sơ (K0 đối chứng):

| Hồ sơ | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình |
|---|---|---|---|---|
| stable_worker | 7 | 7300 | 43,7% (CI 95%: 42,2% – 45,4%) | 781.998 đ |
| stable_worker | 14 | 7300 | 43,2% (CI 95%: 40,4% – 45,9%) | 1.108.397 đ |
| stable_worker | 30 | 7300 | 43,1% (CI 95%: 38,8% – 47,3%) | 1.710.575 đ |
| freelancer | 7 | 7300 | 54,0% (CI 95%: 49,9% – 57,4%) | 13.320.917 đ |
| freelancer | 14 | 7300 | 44,4% (CI 95%: 41,3% – 47,3%) | 18.880.979 đ |
| freelancer | 30 | 7300 | 48,8% (CI 95%: 44,1% – 53,6%) | 29.138.879 đ |
| student | 7 | 7300 | 62,9% (CI 95%: 61,6% – 64,3%) | 2.635.454 đ |
| student | 14 | 7300 | 60,2% (CI 95%: 58,2% – 62,3%) | 3.735.481 đ |
| student | 30 | 7300 | 52,1% (CI 95%: 49,9% – 54,4%) | 5.764.957 đ |
| family | 7 | 7300 | 35,7% (CI 95%: 34,0% – 37,3%) | 1.747.136 đ |
| family | 14 | 7300 | 34,2% (CI 95%: 31,7% – 36,8%) | 2.534.590 đ |
| family | 30 | 7300 | 33,5% (CI 95%: 29,4% – 37,3%) | 4.036.438 đ |
| small_trader | 7 | 7300 | 46,7% (CI 95%: 45,1% – 48,4%) | 4.275.906 đ |
| small_trader | 14 | 7300 | 46,8% (CI 95%: 44,3% – 49,2%) | 6.060.545 đ |
| small_trader | 30 | 7300 | 47,8% (CI 95%: 44,5% – 51,2%) | 9.352.921 đ |

## 6. Chi linh hoạt (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | WAPE | Thiên lệch | MAPE (n, bị loại) |
|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 20,2% (CI 95%: 17,1% – 23,9%) | -0,6% | 31,2% (36500, 0) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 28,0% (CI 95%: 25,9% – 31,0%) | -0,1% | 36,3% (36500, 0) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 34,6% (CI 95%: 32,3% – 37,3%) | 0,1% | 43,7% (36500, 0) |
| 14 | Mô hình | 36500 | 16,4% (CI 95%: 13,8% – 19,9%) | -0,6% | 24,9% (36500, 0) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 22,1% (CI 95%: 19,7% – 25,0%) | -0,1% | 29,4% (36500, 0) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 24,7% (CI 95%: 22,2% – 27,9%) | -0,1% | 32,4% (36500, 0) |
| 30 | Mô hình | 36500 | 13,5% (CI 95%: 11,1% – 16,5%) | -0,6% | 21,1% (36500, 0) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 19,8% (CI 95%: 17,8% – 22,6%) | -0,2% | 26,1% (36500, 0) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 20,7% (CI 95%: 18,7% – 23,1%) | -0,2% | 26,4% (36500, 0) |

MAPE chỉ tính trên mốc có chi linh hoạt thực tế ≥ 100.000 đ.

## 6b. Thu không định kỳ (chế độ đáp án, mọi hồ sơ có thu không định kỳ)

| Kịch bản | Tầm | Cửa sổ | n | WAPE | Thiên lệch |
|---|---|---|---|---|---|
| K0 đối chứng | 7 | all | 36500 | 50,6% (CI 95%: 38,0% – 65,6%) | 0,9% |
| K0 đối chứng | 14 | all | 36500 | 39,2% (CI 95%: 28,7% – 51,3%) | 0,7% |
| K0 đối chứng | 30 | all | 36500 | 29,8% (CI 95%: 21,3% – 39,6%) | 0,3% |
| K1 chi đột xuất lớn | 7 | affected | 1580 | 55,0% (CI 95%: 38,5% – 77,7%) | 7,5% |
| K1 chi đột xuất lớn | 7 | all | 36500 | 50,6% (CI 95%: 38,1% – 67,1%) | 0,9% |
| K1 chi đột xuất lớn | 14 | affected | 2922 | 40,6% (CI 95%: 28,9% – 57,0%) | 4,1% |
| K1 chi đột xuất lớn | 14 | all | 36500 | 39,2% (CI 95%: 29,4% – 51,7%) | 0,7% |
| K1 chi đột xuất lớn | 30 | affected | 5882 | 30,8% (CI 95%: 21,9% – 43,3%) | 2,9% |
| K1 chi đột xuất lớn | 30 | all | 36500 | 29,8% (CI 95%: 22,0% – 38,9%) | 0,3% |
| K2 thu nhập về trễ / đổi số tiền | 7 | affected | 1600 | 60,4% (CI 95%: 46,5% – 78,4%) | 16,8% |
| K2 thu nhập về trễ / đổi số tiền | 7 | all | 36500 | 53,7% (CI 95%: 40,5% – 68,7%) | 0,7% |
| K2 thu nhập về trễ / đổi số tiền | 14 | affected | 3000 | 43,7% (CI 95%: 32,2% – 60,8%) | 10,6% |
| K2 thu nhập về trễ / đổi số tiền | 14 | all | 36500 | 42,3% (CI 95%: 31,8% – 54,3%) | 0,4% |
| K2 thu nhập về trễ / đổi số tiền | 30 | affected | 6200 | 29,6% (CI 95%: 21,5% – 39,6%) | 0,4% |
| K2 thu nhập về trễ / đổi số tiền | 30 | all | 36500 | 33,1% (CI 95%: 24,4% – 44,2%) | -0,2% |
| K3 bỏ một kỳ định kỳ | 7 | affected | 1245 | 47,7% (CI 95%: 33,1% – 66,0%) | 12,9% |
| K3 bỏ một kỳ định kỳ | 7 | all | 36500 | 50,6% (CI 95%: 38,0% – 66,0%) | 0,9% |
| K3 bỏ một kỳ định kỳ | 14 | affected | 2325 | 36,9% (CI 95%: 25,9% – 51,9%) | 5,7% |
| K3 bỏ một kỳ định kỳ | 14 | all | 36500 | 39,2% (CI 95%: 28,9% – 52,2%) | 0,7% |
| K3 bỏ một kỳ định kỳ | 30 | affected | 4734 | 28,1% (CI 95%: 19,6% – 39,3%) | 0,9% |
| K3 bỏ một kỳ định kỳ | 30 | all | 36500 | 29,8% (CI 95%: 22,0% – 39,7%) | 0,3% |
| K4 khoản định kỳ đổi giá | 7 | affected | 1589 | 60,1% (CI 95%: 44,5% – 77,6%) | -8,8% |
| K4 khoản định kỳ đổi giá | 7 | all | 36500 | 50,6% (CI 95%: 37,8% – 67,1%) | 0,9% |
| K4 khoản định kỳ đổi giá | 14 | affected | 2936 | 44,2% (CI 95%: 31,2% – 58,1%) | -2,6% |
| K4 khoản định kỳ đổi giá | 14 | all | 36500 | 39,2% (CI 95%: 29,7% – 52,3%) | 0,7% |
| K4 khoản định kỳ đổi giá | 30 | affected | 5931 | 30,8% (CI 95%: 22,2% – 40,8%) | -4,2% |
| K4 khoản định kỳ đổi giá | 30 | all | 36500 | 29,8% (CI 95%: 21,3% – 39,6%) | 0,3% |
| K5 Tết, chưa khai ngày lễ | 7 | affected | 2900 | 75,6% (CI 95%: 65,9% – 86,6%) | -38,3% |
| K5 Tết, chưa khai ngày lễ | 7 | all | 36500 | 57,7% (CI 95%: 44,8% – 73,8%) | 0,9% |
| K5 Tết, chưa khai ngày lễ | 14 | affected | 3600 | 63,8% (CI 95%: 53,9% – 75,1%) | -33,7% |
| K5 Tết, chưa khai ngày lễ | 14 | all | 36500 | 46,6% (CI 95%: 36,2% – 59,8%) | 0,7% |
| K5 Tết, chưa khai ngày lễ | 30 | affected | 5200 | 48,6% (CI 95%: 40,1% – 58,9%) | -26,9% |
| K5 Tết, chưa khai ngày lễ | 30 | all | 36500 | 37,5% (CI 95%: 29,3% – 48,6%) | 0,3% |
| K5h Tết, đã khai ngày lễ 150% | 7 | affected | 2900 | 75,6% (CI 95%: 66,2% – 87,0%) | -38,3% |
| K5h Tết, đã khai ngày lễ 150% | 7 | all | 36500 | 57,7% (CI 95%: 45,3% – 73,8%) | 0,9% |
| K5h Tết, đã khai ngày lễ 150% | 14 | affected | 3600 | 63,8% (CI 95%: 53,6% – 75,4%) | -33,7% |
| K5h Tết, đã khai ngày lễ 150% | 14 | all | 36500 | 46,6% (CI 95%: 36,7% – 60,2%) | 0,7% |
| K5h Tết, đã khai ngày lễ 150% | 30 | affected | 5200 | 48,6% (CI 95%: 39,7% – 59,9%) | -26,9% |
| K5h Tết, đã khai ngày lễ 150% | 30 | all | 36500 | 37,5% (CI 95%: 28,8% – 49,7%) | 0,3% |
| K6 người dùng mới | 7 | affected | 6200 | 53,0% (CI 95%: 40,5% – 69,0%) | -6,4% |
| K6 người dùng mới | 7 | all | 6200 | 53,0% (CI 95%: 40,6% – 68,5%) | -6,4% |
| K6 người dùng mới | 14 | affected | 6200 | 40,4% (CI 95%: 30,6% – 52,5%) | -7,1% |
| K6 người dùng mới | 14 | all | 6200 | 40,4% (CI 95%: 29,5% – 52,4%) | -7,1% |
| K6 người dùng mới | 30 | affected | 6200 | 31,9% (CI 95%: 23,3% – 42,3%) | -7,6% |
| K6 người dùng mới | 30 | all | 6200 | 31,9% (CI 95%: 23,6% – 41,8%) | -7,6% |

Thu không định kỳ = khoản thu không phải khoản thu định kỳ của đáp án, không vay, không hoàn. Thiên lệch âm: mô hình chiếu ít hơn thực tế. Trước Phase 97 mô hình không chiếu khoản nào (thiên lệch −100%).

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
