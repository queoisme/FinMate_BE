# Backtest dự báo số dư — Phase 96, tầng 1

> Tệp do `FinMate.Backtest` sinh ra, không sửa tay. Dữ liệu là **dữ liệu tổng hợp**, không có dữ liệu người dùng thật.
> Chế độ duy nhất của tầng 1 là **đáp án**: mô hình được biết đúng các khoản định kỳ (chỉ với thông tin trước mốc), nên đây là **cận trên** của độ chính xác.

## Tái lập

| Mục | Giá trị |
|---|---|
| Commit | `không rõ` |
| SDK / runtime | SDK 9.0.321, .NET 9.0.23, X64 |
| Đặc tả bộ sinh | v1.2 (`docs/evaluation/forecast-backtest-generator-spec.md`) |
| Hạt gốc | 20261127 |
| Người gốc | 5 hồ sơ × 20 hạt giống |
| Bước mốc | 1 ngày, từ 2025-07-01 tới 2026-06-30 |
| Tham số `forecast` | phiên bản 0, SHA-256 `009930c219ba66ac` |
| Thế giới / dòng kết quả | 800 / 785100 |

Cùng image SDK, cùng hạt, cùng tham số → cùng tệp này từng byte (S4).

## Tóm tắt

| # | Kết quả |
|---|---|
| 1 | Tầm 7 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 5.167.933 đ (97,0% quy mô dòng tiền thực tế), trung vị 1.226.750 đ. Tốt hơn "số dư giữ nguyên" 3,0% (CI 95%: -17,1% – 22,0%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): -30,6% (CI 95%: -49,7% – -10,2%). |
| 2 | Tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 9.054.863 đ (122,8% quy mô dòng tiền thực tế), trung vị 2.478.500 đ. Tốt hơn "số dư giữ nguyên" -22,8% (CI 95%: -52,3% – 2,6%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): -43,5% (CI 95%: -73,2% – -18,1%). |
| 3 | Tầm 30 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 17.832.458 đ (226,6% quy mô dòng tiền thực tế), trung vị 5.486.000 đ. Tốt hơn "số dư giữ nguyên" -126,6% (CI 95%: -173,6% – -89,4%); so với baseline mạnh nhất còn lại (B-naive (lặp lại h+1 ngày trước)): -78,5% (CI 95%: -118,9% – -43,0%). |
| 4 | Hồ sơ stable_worker, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 1.192.060 đ, dự báo trung bình cao hơn thực tế 509.234 đ; tốt hơn "số dư giữ nguyên" 80,6% (CI 95%: 78,8% – 82,1%). |
| 5 | Hồ sơ freelancer, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 13.969.894 đ, dự báo trung bình thấp hơn thực tế 13.257.108 đ; tốt hơn "số dư giữ nguyên" -17,7% (CI 95%: -20,5% – -15,0%). |
| 6 | Hồ sơ student, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 1.965.563 đ, dự báo trung bình cao hơn thực tế 38.978 đ; tốt hơn "số dư giữ nguyên" 19,6% (CI 95%: 14,5% – 25,3%). |
| 7 | Hồ sơ family, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 3.544.555 đ, dự báo trung bình cao hơn thực tế 1.629.470 đ; tốt hơn "số dư giữ nguyên" 68,6% (CI 95%: 66,5% – 70,7%). |
| 8 | Hồ sơ small_trader, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 24.602.242 đ, dự báo trung bình thấp hơn thực tế 24.602.242 đ; tốt hơn "số dư giữ nguyên" -382,0% (CI 95%: -417,5% – -354,0%). |
| 9 | Chi linh hoạt 15 ngày tới (tầm 14), chế độ đáp án: lệch WAPE 19,3% (CI 95%: 16,6% – 22,8%), thiên lệch -16,7% (âm = dự báo chi ít hơn thực tế). B-mean30: 21,6%, B-naive: 24,2%. |
| 10 | Dải thận trọng–lạc quan (cộng P75/P25 chi linh hoạt theo từng ngày), chế độ đáp án, K0 đối chứng: tỉ lệ số dư thật nằm trong dải — tầm 7: 36,4% (CI 95%: 32,4% – 40,6%), rộng trung bình 1.892.600 đ; tầm 14: 35,2% (CI 95%: 30,8% – 39,7%), rộng trung bình 3.550.210 đ; tầm 30: 35,0% (CI 95%: 29,9% – 40,4%), rộng trung bình 7.339.102 đ. Một dải P25–P75 đúng nghĩa của tổng sẽ chứa khoảng 50%. |
| 11 | K1 chi đột xuất lớn (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2933): sai số số dư tăng 17,2% (CI 95%: 4,4% – 36,7%) so với K0 đối chứng trên cùng người và mốc (11.279.459 đ so với 9.620.020 đ). |
| 12 | K2 thu nhập về trễ / đổi số tiền (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3000): sai số số dư tăng 12,9% (CI 95%: 4,7% – 23,4%) so với K0 đối chứng trên cùng người và mốc (10.680.485 đ so với 9.457.792 đ). |
| 13 | K3 bỏ một kỳ định kỳ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2312): sai số số dư tăng 7,4% (CI 95%: 4,9% – 10,5%) so với K0 đối chứng trên cùng người và mốc (9.880.749 đ so với 9.202.513 đ). |
| 14 | K4 khoản định kỳ đổi giá (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2909): sai số số dư tăng 1,3% (CI 95%: 0,4% – 2,4%) so với K0 đối chứng trên cùng người và mốc (9.297.354 đ so với 9.179.417 đ). |
| 15 | K5 Tết, chưa khai ngày lễ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 21,1% (CI 95%: 10,0% – 36,3%) so với K0 đối chứng trên cùng người và mốc (11.225.214 đ so với 9.266.294 đ). |
| 16 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 31,9% (CI 95%: 21,0% – 46,0%) so với K0 đối chứng trên cùng người và mốc (12.224.995 đ so với 9.266.294 đ). |
| 17 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 8,9% (CI 95%: 6,8% – 10,9%) so với K5 Tết, chưa khai ngày lễ trên cùng người và mốc (12.224.995 đ so với 11.225.214 đ). |
| 18 | K6 người dùng mới (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 6200): sai số số dư giảm 3,8% (CI 95%: -6,4% – -0,7%) so với K0 đối chứng trên cùng người và mốc (9.282.481 đ so với 9.650.273 đ). |

CI 95% lấy bằng bootstrap theo người gốc (1.000 lần). "Tốt hơn X%" = 1 − MAE mô hình / MAE baseline; âm là kém hơn.

## 1. Số dư theo tầm và phương pháp (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | MAE | Trung vị | Thiên lệch | % quy mô | Mô hình tốt hơn |
|---|---|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 5.167.933 đ | 1.226.750 đ | −3.931.081 đ | 97,0% | — |
| 7 | B-hold (số dư giữ nguyên) | 36500 | 5.329.684 đ | 3.141.500 đ | −1.023.205 đ | 100,0% | 3,0% (CI 95%: -17,1% – 22,0%) |
| 7 | B0 (chỉ khoản định kỳ) | 36500 | 3.958.536 đ | 2.356.000 đ | −332.654 đ | 74,3% | -30,6% (CI 95%: -49,7% – -10,2%) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 5.462.126 đ | 1.387.000 đ | −4.638.243 đ | 102,5% | 5,4% (CI 95%: 4,3% – 6,6%) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 8.145.365 đ | 4.987.000 đ | −10.226 đ | 152,8% | 36,6% (CI 95%: 22,7% – 49,5%) |
| 14 | Mô hình | 36500 | 9.054.863 đ | 2.478.500 đ | −7.136.334 đ | 122,8% | — |
| 14 | B-hold (số dư giữ nguyên) | 36500 | 7.372.208 đ | 5.321.000 đ | −1.914.937 đ | 100,0% | -22,8% (CI 95%: -52,3% – 2,6%) |
| 14 | B0 (chỉ khoản định kỳ) | 36500 | 6.311.653 đ | 4.075.000 đ | −388.825 đ | 85,6% | -43,5% (CI 95%: -73,2% – -18,1%) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 9.615.354 đ | 2.720.000 đ | −8.461.806 đ | 130,4% | 5,8% (CI 95%: 4,6% – 7,2%) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 12.261.417 đ | 9.425.500 đ | −27.838 đ | 166,3% | 26,2% (CI 95%: 6,0% – 43,2%) |
| 30 | Mô hình | 36500 | 17.832.458 đ | 5.486.000 đ | −14.488.286 đ | 226,6% | — |
| 30 | B-hold (số dư giữ nguyên) | 36500 | 7.869.774 đ | 4.013.000 đ | −3.970.164 đ | 100,0% | -126,6% (CI 95%: -173,6% – -89,4%) |
| 30 | B0 (chỉ khoản định kỳ) | 36500 | 11.649.293 đ | 8.216.500 đ | −543.897 đ | 148,0% | -53,1% (CI 95%: -86,4% – -22,0%) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 19.039.082 đ | 6.016.250 đ | −17.228.057 đ | 241,9% | 6,3% (CI 95%: 5,0% – 7,9%) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 9.989.458 đ | 5.066.500 đ | −109.932 đ | 126,9% | -78,5% (CI 95%: -118,9% – -43,0%) |

Thiên lệch = trung bình (dự kiến − thực tế); âm là dự báo thấp hơn thực tế. % quy mô = MAE / trung bình |số dư thực tế − số dư lúc dự báo|.

## 2. Theo kịch bản (chế độ đáp án, mọi hồ sơ, mô hình)

| Kịch bản | Tầm | MAE cả năm (n) | MAE mốc bị ảnh hưởng (n) | So với đối chứng cùng mốc |
|---|---|---|---|---|
| K0 đối chứng | 7 | 5.167.933 đ (36500) | — | — |
| K0 đối chứng | 14 | 9.054.863 đ (36500) | — | — |
| K0 đối chứng | 30 | 17.832.458 đ (36500) | — | — |
| K1 chi đột xuất lớn | 7 | 5.267.492 đ (36500) | 7.850.014 đ (1575) | K0: 41,8% (CI 20,0% – 75,3%) |
| K1 chi đột xuất lớn | 14 | 9.187.214 đ (36500) | 11.279.459 đ (2933) | K0: 17,2% (CI 4,4% – 36,7%) |
| K1 chi đột xuất lớn | 30 | 18.002.217 đ (36500) | 19.576.071 đ (5954) | K0: 5,7% (CI -0,4% – 15,3%) |
| K2 thu nhập về trễ / đổi số tiền | 7 | 5.509.947 đ (36500) | 8.123.174 đ (1600) | K0: 42,6% (CI 25,2% – 67,3%) |
| K2 thu nhập về trễ / đổi số tiền | 14 | 9.503.552 đ (36500) | 10.680.485 đ (3000) | K0: 12,9% (CI 4,7% – 23,4%) |
| K2 thu nhập về trễ / đổi số tiền | 30 | 18.510.018 đ (36500) | 18.744.564 đ (6200) | K0: 2,8% (CI -0,1% – 6,1%) |
| K3 bỏ một kỳ định kỳ | 7 | 5.240.293 đ (36500) | 6.077.637 đ (1240) | K0: 15,4% (CI 11,1% – 21,4%) |
| K3 bỏ một kỳ định kỳ | 14 | 9.148.661 đ (36500) | 9.880.749 đ (2312) | K0: 7,4% (CI 4,9% – 10,5%) |
| K3 bỏ một kỳ định kỳ | 30 | 17.931.860 đ (36500) | 18.504.427 đ (4709) | K0: 2,5% (CI 1,0% – 3,9%) |
| K4 khoản định kỳ đổi giá | 7 | 5.187.123 đ (36500) | 5.271.137 đ (1572) | K0: 2,5% (CI 0,8% – 4,6%) |
| K4 khoản định kỳ đổi giá | 14 | 9.077.992 đ (36500) | 9.297.354 đ (2909) | K0: 1,3% (CI 0,4% – 2,4%) |
| K4 khoản định kỳ đổi giá | 30 | 17.861.703 đ (36500) | 18.167.798 đ (5827) | K0: 0,6% (CI 0,1% – 1,3%) |
| K5 Tết, chưa khai ngày lễ | 7 | 5.347.803 đ (36500) | 7.229.000 đ (2900) | K0: 36,5% (CI 21,5% – 56,8%) |
| K5 Tết, chưa khai ngày lễ | 14 | 9.297.542 đ (36500) | 11.225.214 đ (3600) | K0: 21,1% (CI 10,0% – 36,3%) |
| K5 Tết, chưa khai ngày lễ | 30 | 18.140.982 đ (36500) | 19.565.773 đ (5200) | K0: 7,8% (CI 2,3% – 15,9%) |
| K5h Tết, đã khai ngày lễ 150% | 7 | 5.388.384 đ (36500) | 7.739.757 đ (2900) | K0: 46,2% (CI 31,6% – 68,7%); K5: 7,1% (CI 4,3% – 9,7%) |
| K5h Tết, đã khai ngày lễ 150% | 14 | 9.396.150 đ (36500) | 12.224.995 đ (3600) | K0: 31,9% (CI 21,0% – 46,0%); K5: 8,9% (CI 6,8% – 10,9%) |
| K5h Tết, đã khai ngày lễ 150% | 30 | 18.374.775 đ (36500) | 21.206.819 đ (5200) | K0: 16,8% (CI 10,7% – 25,4%); K5: 8,4% (CI 7,2% – 9,4%) |
| K6 người dùng mới | 7 | 5.238.730 đ (6200) | 5.238.730 đ (6200) | K0: -3,9% (CI -6,2% – -1,4%) |
| K6 người dùng mới | 14 | 9.282.481 đ (6200) | 9.282.481 đ (6200) | K0: -3,8% (CI -6,4% – -0,7%) |
| K6 người dùng mới | 30 | 18.640.580 đ (6200) | 18.640.580 đ (6200) | K0: -3,6% (CI -6,7% – -0,2%) |

Mốc bị ảnh hưởng: đoạn [T, T+h] chứa một ngày sự kiện của kịch bản (với K6: mọi mốc 14–75 ngày sau ngày bắt đầu). So với đối chứng: MAE kịch bản / MAE đối chứng − 1 trên cùng (người, mốc, tầm).

## 3. Theo hồ sơ (K0 đối chứng, tầm 14, chế độ đáp án)

| Hồ sơ | n | MAE mô hình | Thiên lệch | % quy mô | Tốt hơn B-hold | Tốt hơn B-mean30 | Tốt hơn B-naive | Trong dải |
|---|---|---|---|---|---|---|---|---|
| stable_worker | 7300 | 1.192.060 đ | 509.234 đ | 19,4% | 80,6% (CI 95%: 78,8% – 82,1%) | -14,9% (CI 95%: -18,3% – -12,1%) | 89,9% (CI 95%: 89,0% – 90,7%) | 65,6% (CI 95%: 63,0% – 68,2%) |
| freelancer | 7300 | 13.969.894 đ | −13.257.108 đ | 117,7% | -17,7% (CI 95%: -20,5% – -15,0%) | 5,7% (CI 95%: 4,9% – 6,4%) | 17,5% (CI 95%: 13,8% – 21,2%) | 22,4% (CI 95%: 20,0% – 25,1%) |
| student | 7300 | 1.965.563 đ | 38.978 đ | 80,4% | 19,6% (CI 95%: 14,5% – 25,3%) | 39,5% (CI 95%: 38,3% – 40,7%) | 56,3% (CI 95%: 53,5% – 59,4%) | 44,0% (CI 95%: 38,1% – 50,0%) |
| family | 7300 | 3.544.555 đ | 1.629.470 đ | 31,4% | 68,6% (CI 95%: 66,5% – 70,7%) | -14,6% (CI 95%: -18,1% – -11,2%) | 83,7% (CI 95%: 82,5% – 84,7%) | 44,2% (CI 95%: 42,4% – 45,8%) |
| small_trader | 7300 | 24.602.242 đ | −24.602.242 đ | 482,0% | -382,0% (CI 95%: -417,5% – -354,0%) | 5,0% (CI 95%: 4,3% – 5,6%) | -287,2% (CI 95%: -303,2% – -272,2%) | 0,0% (CI 95%: 0,0% – 0,0%) |

## 4. Theo mức tin cậy mô hình tự báo (chế độ đáp án, tầm 14, mọi kịch bản gộp theo từng kịch bản)

| Kịch bản | Tin cậy | n | MAE | % quy mô |
|---|---|---|---|---|
| K0 đối chứng | normal | 36500 | 9.054.863 đ | 122,8% |
| K0 đối chứng | low | 0 | — | — |
| K6 người dùng mới | normal | 4479 | 9.888.858 đ | 123,6% |
| K6 người dùng mới | low | 1721 | 7.704.352 đ | 98,1% |

## 5. Dải thận trọng–lạc quan theo tầm (chế độ đáp án, mọi hồ sơ)

Dải của engine = cộng chi linh hoạt P75 (thận trọng) / P25 (lạc quan) **của từng ngày**. Tổng các P75 lớn hơn P75 của tổng, nên dải rộng dần theo tầm; bảng này chỉ đo, không sửa (S5, quyết định riêng ở REQUIREMENTS §13.4).

| Kịch bản | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình | Rộng trung vị |
|---|---|---|---|---|---|
| K0 đối chứng | 7 | 36500 | 36,4% (CI 95%: 32,4% – 40,6%) | 1.892.600 đ | 1.667.000 đ |
| K0 đối chứng | 14 | 36500 | 35,2% (CI 95%: 30,8% – 39,7%) | 3.550.210 đ | 3.140.125 đ |
| K0 đối chứng | 30 | 36500 | 35,0% (CI 95%: 29,9% – 40,4%) | 7.339.102 đ | 6.496.250 đ |
| K1 chi đột xuất lớn | 7 | 36500 | 35,1% (CI 95%: 31,2% – 38,9%) | 1.900.855 đ | 1.676.500 đ |
| K1 chi đột xuất lớn | 14 | 36500 | 32,7% (CI 95%: 28,2% – 37,2%) | 3.565.624 đ | 3.159.625 đ |
| K1 chi đột xuất lớn | 30 | 36500 | 30,6% (CI 95%: 25,6% – 35,3%) | 7.370.840 đ | 6.535.500 đ |
| K2 thu nhập về trễ / đổi số tiền | 7 | 36500 | 35,0% (CI 95%: 31,0% – 38,7%) | 1.891.453 đ | 1.671.500 đ |
| K2 thu nhập về trễ / đổi số tiền | 14 | 36500 | 33,8% (CI 95%: 29,7% – 37,9%) | 3.548.023 đ | 3.145.500 đ |
| K2 thu nhập về trễ / đổi số tiền | 30 | 36500 | 33,8% (CI 95%: 28,7% – 39,3%) | 7.334.508 đ | 6.520.625 đ |
| K3 bỏ một kỳ định kỳ | 7 | 36500 | 35,5% (CI 95%: 31,3% – 39,3%) | 1.894.521 đ | 1.667.500 đ |
| K3 bỏ một kỳ định kỳ | 14 | 36500 | 34,2% (CI 95%: 29,8% – 38,5%) | 3.553.820 đ | 3.142.500 đ |
| K3 bỏ một kỳ định kỳ | 30 | 36500 | 34,4% (CI 95%: 29,0% – 39,7%) | 7.346.519 đ | 6.503.875 đ |
| K4 khoản định kỳ đổi giá | 7 | 36500 | 36,2% (CI 95%: 31,9% – 40,0%) | 1.895.885 đ | 1.669.000 đ |
| K4 khoản định kỳ đổi giá | 14 | 36500 | 34,9% (CI 95%: 30,4% – 39,4%) | 3.556.357 đ | 3.144.875 đ |
| K4 khoản định kỳ đổi giá | 30 | 36500 | 34,7% (CI 95%: 29,4% – 40,0%) | 7.351.807 đ | 6.506.375 đ |
| K5 Tết, chưa khai ngày lễ | 7 | 36500 | 35,6% (CI 95%: 31,5% – 39,4%) | 1.989.017 đ | 1.753.875 đ |
| K5 Tết, chưa khai ngày lễ | 14 | 36500 | 34,8% (CI 95%: 30,1% – 39,1%) | 3.730.657 đ | 3.293.375 đ |
| K5 Tết, chưa khai ngày lễ | 30 | 36500 | 34,1% (CI 95%: 28,8% – 39,2%) | 7.711.526 đ | 6.832.000 đ |
| K5h Tết, đã khai ngày lễ 150% | 7 | 36500 | 36,2% (CI 95%: 32,2% – 40,1%) | 2.030.172 đ | 1.783.500 đ |
| K5h Tết, đã khai ngày lễ 150% | 14 | 36500 | 35,2% (CI 95%: 30,7% – 39,6%) | 3.805.665 đ | 3.352.000 đ |
| K5h Tết, đã khai ngày lễ 150% | 30 | 36500 | 34,3% (CI 95%: 28,6% – 39,5%) | 7.861.230 đ | 6.938.625 đ |
| K6 người dùng mới | 7 | 6200 | 24,6% (CI 95%: 21,5% – 27,8%) | 1.231.408 đ | 1.060.625 đ |
| K6 người dùng mới | 14 | 6200 | 24,7% (CI 95%: 20,6% – 29,1%) | 2.310.728 đ | 1.996.875 đ |
| K6 người dùng mới | 30 | 6200 | 23,8% (CI 95%: 18,7% – 28,7%) | 4.776.535 đ | 4.125.125 đ |

Theo hồ sơ (K0 đối chứng):

| Hồ sơ | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình |
|---|---|---|---|---|
| stable_worker | 7 | 7300 | 60,8% (CI 95%: 59,1% – 62,6%) | 1.194.726 đ |
| stable_worker | 14 | 7300 | 65,6% (CI 95%: 63,0% – 68,2%) | 2.240.753 đ |
| stable_worker | 30 | 7300 | 70,5% (CI 95%: 67,2% – 73,8%) | 4.631.660 đ |
| freelancer | 7 | 7300 | 35,7% (CI 95%: 33,2% – 38,4%) | 1.915.145 đ |
| freelancer | 14 | 7300 | 22,4% (CI 95%: 20,0% – 25,1%) | 3.592.195 đ |
| freelancer | 30 | 7300 | 9,5% (CI 95%: 7,4% – 11,7%) | 7.424.720 đ |
| student | 7 | 7300 | 41,4% (CI 95%: 37,6% – 45,3%) | 986.777 đ |
| student | 14 | 7300 | 44,0% (CI 95%: 38,1% – 50,0%) | 1.851.103 đ |
| student | 30 | 7300 | 43,8% (CI 95%: 36,4% – 50,6%) | 3.826.654 đ |
| family | 7 | 7300 | 44,0% (CI 95%: 41,9% – 45,9%) | 2.511.517 đ |
| family | 14 | 7300 | 44,2% (CI 95%: 42,4% – 45,8%) | 4.711.147 đ |
| family | 30 | 7300 | 51,3% (CI 95%: 48,6% – 54,3%) | 9.739.770 đ |
| small_trader | 7 | 7300 | 0,0% (CI 95%: 0,0% – 0,0%) | 2.854.833 đ |
| small_trader | 14 | 7300 | 0,0% (CI 95%: 0,0% – 0,0%) | 5.355.852 đ |
| small_trader | 30 | 7300 | 0,0% (CI 95%: 0,0% – 0,0%) | 11.072.707 đ |

## 6. Chi linh hoạt (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | WAPE | Thiên lệch | MAPE (n, bị loại) |
|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 21,7% (CI 95%: 19,0% – 25,3%) | -16,8% | 25,2% (36500, 0) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 27,4% (CI 95%: 25,4% – 30,2%) | -0,3% | 35,8% (36500, 0) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 34,0% (CI 95%: 31,8% – 36,7%) | 0,0% | 43,0% (36500, 0) |
| 14 | Mô hình | 36500 | 19,3% (CI 95%: 16,6% – 22,8%) | -16,7% | 22,8% (36500, 0) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 21,6% (CI 95%: 19,3% – 24,3%) | -0,2% | 29,0% (36500, 0) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 24,2% (CI 95%: 21,8% – 27,2%) | -0,1% | 32,1% (36500, 0) |
| 30 | Mô hình | 36500 | 17,9% (CI 95%: 15,2% – 21,2%) | -16,7% | 22,4% (36500, 0) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 19,5% (CI 95%: 17,7% – 22,2%) | -0,2% | 26,0% (36500, 0) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 20,3% (CI 95%: 18,5% – 22,9%) | -0,3% | 26,3% (36500, 0) |

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
