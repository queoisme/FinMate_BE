# Backtest dự báo số dư — mô hình `mean_dow_income_v2`

> Tệp do `FinMate.Backtest` sinh ra, không sửa tay. Dữ liệu là **dữ liệu tổng hợp**, không có dữ liệu người dùng thật.
> Chế độ duy nhất của tầng 1 là **đáp án**: mô hình được biết đúng các khoản định kỳ (chỉ với thông tin trước mốc), nên đây là **cận trên** của độ chính xác.

## Tái lập

| Mục | Giá trị |
|---|---|
| Commit | `8ff80cded6ed` |
| SDK / runtime | SDK 9.0.321, .NET 9.0.23, X64 |
| Đặc tả bộ sinh | v1.2 (`docs/evaluation/forecast-backtest-generator-spec.md`) |
| Hạt gốc | 20261127 |
| Người gốc | 5 hồ sơ × 20 hạt giống |
| Bước mốc | 1 ngày, từ 2025-07-01 tới 2026-06-30 |
| Tham số `forecast` | phiên bản 0, SHA-256 `009930c219ba66ac` |
| Phương pháp mô hình | `mean_dow_income_v2` (`ForecastMethods.Current`) |
| Thế giới / dòng kết quả | 800 / 785100 |

Cùng image SDK, cùng hạt, cùng tham số → cùng tệp này từng byte (S4).

## Tóm tắt

| # | Kết quả |
|---|---|
| 1 | Tầm 7 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 3.297.006 đ (61,9% quy mô dòng tiền thực tế), trung vị 1.268.474 đ. Tốt hơn "số dư giữ nguyên" 38,1% (CI 95%: 28,5% – 47,3%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): 16,7% (CI 95%: 9,8% – 24,1%). |
| 2 | Tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 4.677.578 đ (63,4% quy mô dòng tiền thực tế), trung vị 1.958.067 đ. Tốt hơn "số dư giữ nguyên" 36,6% (CI 95%: 27,1% – 46,2%); so với baseline mạnh nhất còn lại (B0 (chỉ khoản định kỳ)): 25,9% (CI 95%: 18,8% – 33,3%). |
| 3 | Tầm 30 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 7.277.500 đ (92,5% quy mô dòng tiền thực tế), trung vị 3.428.216 đ. Tốt hơn "số dư giữ nguyên" 7,5% (CI 95%: 0,7% – 15,1%); so với baseline mạnh nhất còn lại (B-naive (lặp lại h+1 ngày trước)): 27,1% (CI 95%: 23,1% – 31,4%). |
| 4 | Hồ sơ stable_worker, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 1.002.850 đ, dự báo trung bình thấp hơn thực tế 342.236 đ; tốt hơn "số dư giữ nguyên" 83,7% (CI 95%: 82,2% – 85,0%). |
| 5 | Hồ sơ freelancer, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 13.249.648 đ, dự báo trung bình thấp hơn thực tế 431.657 đ; tốt hơn "số dư giữ nguyên" -11,6% (CI 95%: -15,7% – -7,2%). |
| 6 | Hồ sơ student, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 2.349.258 đ, dự báo trung bình thấp hơn thực tế 115.148 đ; tốt hơn "số dư giữ nguyên" 4,0% (CI 95%: -2,0% – 10,3%). |
| 7 | Hồ sơ family, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 2.977.994 đ, dự báo trung bình thấp hơn thực tế 307.328 đ; tốt hơn "số dư giữ nguyên" 73,6% (CI 95%: 71,6% – 75,5%). |
| 8 | Hồ sơ small_trader, tầm 14 ngày, chế độ đáp án, K0 đối chứng: số dư lệch trung bình 3.808.139 đ, dự báo trung bình thấp hơn thực tế 74.311 đ; tốt hơn "số dư giữ nguyên" 25,4% (CI 95%: 19,3% – 30,5%). |
| 9 | Chi linh hoạt 15 ngày tới (tầm 14), chế độ đáp án: lệch WAPE 16,5% (CI 95%: 13,9% – 19,8%), thiên lệch 0,3% (âm = dự báo chi ít hơn thực tế). B-mean30: 21,6%, B-naive: 24,2%. |
| 10 | Dải thận trọng–lạc quan (cộng P75/P25 chi linh hoạt theo từng ngày), chế độ đáp án, K0 đối chứng: tỉ lệ số dư thật nằm trong dải — tầm 7: 41,5% (CI 95%: 38,5% – 44,6%), rộng trung bình 2.852.724 đ; tầm 14: 50,3% (CI 95%: 47,0% – 53,7%), rộng trung bình 5.350.582 đ; tầm 30: 58,1% (CI 95%: 53,9% – 62,3%), rộng trung bình 11.059.880 đ. Một dải P25–P75 đúng nghĩa của tổng sẽ chứa khoảng 50%. |
| 11 | K1 chi đột xuất lớn (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2933): sai số số dư tăng 70,6% (CI 95%: 49,0% – 101,9%) so với K0 đối chứng trên cùng người và mốc (9.193.870 đ so với 5.390.241 đ). |
| 12 | K2 thu nhập về trễ / đổi số tiền (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3000): sai số số dư tăng 40,9% (CI 95%: 25,8% – 61,1%) so với K0 đối chứng trên cùng người và mốc (6.845.244 đ so với 4.856.943 đ). |
| 13 | K3 bỏ một kỳ định kỳ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2312): sai số số dư tăng 10,1% (CI 95%: 5,6% – 16,7%) so với K0 đối chứng trên cùng người và mốc (5.757.665 đ so với 5.229.171 đ). |
| 14 | K4 khoản định kỳ đổi giá (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 2909): sai số số dư tăng 2,3% (CI 95%: 0,8% – 4,1%) so với K0 đối chứng trên cùng người và mốc (4.836.645 đ so với 4.729.852 đ). |
| 15 | K5 Tết, chưa khai ngày lễ (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 74,0% (CI 95%: 47,4% – 109,8%) so với K0 đối chứng trên cùng người và mốc (8.218.708 đ so với 4.723.381 đ). |
| 16 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 76,1% (CI 95%: 49,4% – 116,2%) so với K0 đối chứng trên cùng người và mốc (8.317.202 đ so với 4.723.381 đ). |
| 17 | K5h Tết, đã khai ngày lễ 150% (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 3600): sai số số dư tăng 1,2% (CI 95%: -2,0% – 4,1%) so với K5 Tết, chưa khai ngày lễ trên cùng người và mốc (8.317.202 đ so với 8.218.708 đ). |
| 18 | K6 người dùng mới (tầm 14 ngày, chế độ đáp án, chỉ các mốc bị kịch bản ảnh hưởng, n = 6200): sai số số dư tăng 18,4% (CI 95%: 11,4% – 26,2%) so với K0 đối chứng trên cùng người và mốc (5.866.902 đ so với 4.954.055 đ). |

CI 95% lấy bằng bootstrap theo người gốc (1.000 lần). "Tốt hơn X%" = 1 − MAE mô hình / MAE baseline; âm là kém hơn.

## 1. Số dư theo tầm và phương pháp (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | MAE | Trung vị | Thiên lệch | % quy mô | Mô hình tốt hơn |
|---|---|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 3.297.006 đ | 1.268.474 đ | −260.533 đ | 61,9% | — |
| 7 | B-hold (số dư giữ nguyên) | 36500 | 5.329.684 đ | 3.141.500 đ | −1.023.205 đ | 100,0% | 38,1% (CI 95%: 28,5% – 47,3%) |
| 7 | B0 (chỉ khoản định kỳ) | 36500 | 3.958.536 đ | 2.356.000 đ | −332.654 đ | 74,3% | 16,7% (CI 95%: 9,8% – 24,1%) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 5.462.126 đ | 1.387.000 đ | −4.638.243 đ | 102,5% | 39,6% (CI 95%: 26,5% – 51,0%) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 8.145.365 đ | 4.987.000 đ | −10.226 đ | 152,8% | 59,5% (CI 95%: 52,8% – 65,5%) |
| 14 | Mô hình | 36500 | 4.677.578 đ | 1.958.067 đ | −254.136 đ | 63,4% | — |
| 14 | B-hold (số dư giữ nguyên) | 36500 | 7.372.208 đ | 5.321.000 đ | −1.914.937 đ | 100,0% | 36,6% (CI 95%: 27,1% – 46,2%) |
| 14 | B0 (chỉ khoản định kỳ) | 36500 | 6.311.653 đ | 4.075.000 đ | −388.825 đ | 85,6% | 25,9% (CI 95%: 18,8% – 33,3%) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 9.615.354 đ | 2.720.000 đ | −8.461.806 đ | 130,4% | 51,4% (CI 95%: 39,6% – 61,2%) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 12.261.417 đ | 9.425.500 đ | −27.838 đ | 166,3% | 61,9% (CI 95%: 55,0% – 68,7%) |
| 30 | Mô hình | 36500 | 7.277.500 đ | 3.428.216 đ | −265.623 đ | 92,5% | — |
| 30 | B-hold (số dư giữ nguyên) | 36500 | 7.869.774 đ | 4.013.000 đ | −3.970.164 đ | 100,0% | 7,5% (CI 95%: 0,7% – 15,1%) |
| 30 | B0 (chỉ khoản định kỳ) | 36500 | 11.649.293 đ | 8.216.500 đ | −543.897 đ | 148,0% | 37,5% (CI 95%: 29,1% – 45,7%) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 19.039.082 đ | 6.016.250 đ | −17.228.057 đ | 241,9% | 61,8% (CI 95%: 51,4% – 69,8%) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 9.989.458 đ | 5.066.500 đ | −109.932 đ | 126,9% | 27,1% (CI 95%: 23,1% – 31,4%) |

Thiên lệch = trung bình (dự kiến − thực tế); âm là dự báo thấp hơn thực tế. % quy mô = MAE / trung bình |số dư thực tế − số dư lúc dự báo|.

## 2. Theo kịch bản (chế độ đáp án, mọi hồ sơ, mô hình)

| Kịch bản | Tầm | MAE cả năm (n) | MAE mốc bị ảnh hưởng (n) | So với đối chứng cùng mốc |
|---|---|---|---|---|
| K0 đối chứng | 7 | 3.297.006 đ (36500) | — | — |
| K0 đối chứng | 14 | 4.677.578 đ (36500) | — | — |
| K0 đối chứng | 30 | 7.277.500 đ (36500) | — | — |
| K1 chi đột xuất lớn | 7 | 3.555.219 đ (36500) | 8.186.292 đ (1575) | K0: 119,4% (CI 87,6% – 165,0%) |
| K1 chi đột xuất lớn | 14 | 5.124.833 đ (36500) | 9.193.870 đ (2933) | K0: 70,6% (CI 49,0% – 101,9%) |
| K1 chi đột xuất lớn | 30 | 8.119.036 đ (36500) | 11.347.513 đ (5954) | K0: 37,8% (CI 25,5% – 56,0%) |
| K2 thu nhập về trễ / đổi số tiền | 7 | 3.724.520 đ (36500) | 6.527.402 đ (1600) | K0: 89,8% (CI 61,0% – 129,4%) |
| K2 thu nhập về trễ / đổi số tiền | 14 | 5.280.638 đ (36500) | 6.845.244 đ (3000) | K0: 40,9% (CI 25,8% – 61,1%) |
| K2 thu nhập về trễ / đổi số tiền | 30 | 8.337.471 đ (36500) | 8.984.641 đ (6200) | K0: 17,8% (CI 9,8% – 27,5%) |
| K3 bỏ một kỳ định kỳ | 7 | 3.360.875 đ (36500) | 4.029.153 đ (1240) | K0: 17,7% (CI 10,3% – 27,7%) |
| K3 bỏ một kỳ định kỳ | 14 | 4.786.673 đ (36500) | 5.757.665 đ (2312) | K0: 10,1% (CI 5,6% – 16,7%) |
| K3 bỏ một kỳ định kỳ | 30 | 7.456.631 đ (36500) | 8.771.663 đ (4709) | K0: 5,8% (CI 2,7% – 9,9%) |
| K4 khoản định kỳ đổi giá | 7 | 3.336.476 đ (36500) | 3.445.529 đ (1572) | K0: 3,5% (CI 1,7% – 5,8%) |
| K4 khoản định kỳ đổi giá | 14 | 4.735.164 đ (36500) | 4.836.645 đ (2909) | K0: 2,3% (CI 0,8% – 4,1%) |
| K4 khoản định kỳ đổi giá | 30 | 7.370.296 đ (36500) | 7.858.501 đ (5827) | K0: 1,0% (CI 0,1% – 2,1%) |
| K5 Tết, chưa khai ngày lễ | 7 | 3.565.548 đ (36500) | 6.181.080 đ (2900) | K0: 90,0% (CI 61,5% – 130,6%) |
| K5 Tết, chưa khai ngày lễ | 14 | 5.124.925 đ (36500) | 8.218.708 đ (3600) | K0: 74,0% (CI 47,4% – 109,8%) |
| K5 Tết, chưa khai ngày lễ | 30 | 8.138.854 đ (36500) | 11.905.200 đ (5200) | K0: 56,1% (CI 35,6% – 85,8%) |
| K5h Tết, đã khai ngày lễ 150% | 7 | 3.554.225 đ (36500) | 6.038.564 đ (2900) | K0: 85,7% (CI 57,0% – 127,2%); K5: -2,3% (CI -4,1% – -0,5%) |
| K5h Tết, đã khai ngày lễ 150% | 14 | 5.134.639 đ (36500) | 8.317.202 đ (3600) | K0: 76,1% (CI 49,4% – 116,2%); K5: 1,2% (CI -2,0% – 4,1%) |
| K5h Tết, đã khai ngày lễ 150% | 30 | 8.207.178 đ (36500) | 12.384.780 đ (5200) | K0: 62,4% (CI 39,7% – 98,3%); K5: 4,0% (CI 0,5% – 7,6%) |
| K6 người dùng mới | 7 | 3.779.688 đ (6200) | 3.779.688 đ (6200) | K0: 11,2% (CI 6,9% – 16,6%) |
| K6 người dùng mới | 14 | 5.866.902 đ (6200) | 5.866.902 đ (6200) | K0: 18,4% (CI 11,4% – 26,2%) |
| K6 người dùng mới | 30 | 10.315.958 đ (6200) | 10.315.958 đ (6200) | K0: 31,5% (CI 20,1% – 45,3%) |

Mốc bị ảnh hưởng: đoạn [T, T+h] chứa một ngày sự kiện của kịch bản (với K6: mọi mốc 14–75 ngày sau ngày bắt đầu). So với đối chứng: MAE kịch bản / MAE đối chứng − 1 trên cùng (người, mốc, tầm).

## 3. Theo hồ sơ (K0 đối chứng, tầm 14, chế độ đáp án)

| Hồ sơ | n | MAE mô hình | Thiên lệch | % quy mô | Tốt hơn B-hold | Tốt hơn B-mean30 | Tốt hơn B-naive | Trong dải |
|---|---|---|---|---|---|---|---|---|
| stable_worker | 7300 | 1.002.850 đ | −342.236 đ | 16,3% | 83,7% (CI 95%: 82,2% – 85,0%) | 3,3% (CI 95%: 0,8% – 5,7%) | 91,5% (CI 95%: 90,8% – 92,2%) | 75,1% (CI 95%: 71,8% – 78,1%) |
| freelancer | 7300 | 13.249.648 đ | −431.657 đ | 111,6% | -11,6% (CI 95%: -15,7% – -7,2%) | 10,5% (CI 95%: 5,5% – 15,2%) | 21,8% (CI 95%: 17,2% – 25,5%) | 26,5% (CI 95%: 22,8% – 30,1%) |
| student | 7300 | 2.349.258 đ | −115.148 đ | 96,0% | 4,0% (CI 95%: -2,0% – 10,3%) | 27,7% (CI 95%: 25,4% – 30,2%) | 47,8% (CI 95%: 44,9% – 51,1%) | 39,8% (CI 95%: 38,1% – 41,8%) |
| family | 7300 | 2.977.994 đ | −307.328 đ | 26,4% | 73,6% (CI 95%: 71,6% – 75,5%) | 3,7% (CI 95%: 2,3% – 5,2%) | 86,3% (CI 95%: 85,3% – 87,2%) | 56,8% (CI 95%: 54,7% – 59,3%) |
| small_trader | 7300 | 3.808.139 đ | −74.311 đ | 74,6% | 25,4% (CI 95%: 19,3% – 30,5%) | 85,3% (CI 95%: 84,6% – 86,0%) | 40,1% (CI 95%: 37,9% – 42,3%) | 53,6% (CI 95%: 50,4% – 56,8%) |

## 4. Theo mức tin cậy mô hình tự báo (chế độ đáp án, tầm 14, mọi kịch bản gộp theo từng kịch bản)

| Kịch bản | Tin cậy | n | MAE | % quy mô |
|---|---|---|---|---|
| K0 đối chứng | normal | 36500 | 4.677.578 đ | 63,4% |
| K0 đối chứng | low | 0 | — | — |
| K6 người dùng mới | normal | 4479 | 5.482.746 đ | 68,5% |
| K6 người dùng mới | low | 1721 | 6.866.689 đ | 87,4% |

## 5. Dải thận trọng–lạc quan theo tầm (chế độ đáp án, mọi hồ sơ)

Dải của engine = cộng chi linh hoạt P75 (thận trọng) / P25 (lạc quan) **của từng ngày**. Tổng các P75 lớn hơn P75 của tổng, nên dải rộng dần theo tầm; bảng này chỉ đo, không sửa (S5, quyết định riêng ở REQUIREMENTS §13.4).

| Kịch bản | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình | Rộng trung vị |
|---|---|---|---|---|---|
| K0 đối chứng | 7 | 36500 | 41,5% (CI 95%: 38,5% – 44,6%) | 2.852.724 đ | 2.075.875 đ |
| K0 đối chứng | 14 | 36500 | 50,3% (CI 95%: 47,0% – 53,7%) | 5.350.582 đ | 3.913.760 đ |
| K0 đối chứng | 30 | 36500 | 58,1% (CI 95%: 53,9% – 62,3%) | 11.059.880 đ | 8.150.594 đ |
| K1 chi đột xuất lớn | 7 | 36500 | 35,4% (CI 95%: 33,1% – 37,7%) | 2.861.000 đ | 2.085.625 đ |
| K1 chi đột xuất lớn | 14 | 36500 | 41,2% (CI 95%: 38,3% – 44,0%) | 5.366.034 đ | 3.930.125 đ |
| K1 chi đột xuất lớn | 30 | 36500 | 46,2% (CI 95%: 42,8% – 49,5%) | 11.091.693 đ | 8.175.528 đ |
| K2 thu nhập về trễ / đổi số tiền | 7 | 36500 | 37,5% (CI 95%: 35,1% – 39,8%) | 2.929.414 đ | 2.104.500 đ |
| K2 thu nhập về trễ / đổi số tiền | 14 | 36500 | 44,3% (CI 95%: 41,6% – 47,0%) | 5.494.669 đ | 3.967.625 đ |
| K2 thu nhập về trễ / đổi số tiền | 30 | 36500 | 50,5% (CI 95%: 47,4% – 53,2%) | 11.358.742 đ | 8.265.327 đ |
| K3 bỏ một kỳ định kỳ | 7 | 36500 | 40,1% (CI 95%: 37,0% – 43,1%) | 2.854.635 đ | 2.073.674 đ |
| K3 bỏ một kỳ định kỳ | 14 | 36500 | 48,4% (CI 95%: 44,8% – 51,7%) | 5.354.171 đ | 3.910.674 đ |
| K3 bỏ một kỳ định kỳ | 30 | 36500 | 55,5% (CI 95%: 51,2% – 59,4%) | 11.067.250 đ | 8.149.783 đ |
| K4 khoản định kỳ đổi giá | 7 | 36500 | 39,8% (CI 95%: 37,0% – 42,6%) | 2.856.020 đ | 2.080.331 đ |
| K4 khoản định kỳ đổi giá | 14 | 36500 | 48,5% (CI 95%: 45,3% – 51,6%) | 5.356.749 đ | 3.921.467 đ |
| K4 khoản định kỳ đổi giá | 30 | 36500 | 56,4% (CI 95%: 52,7% – 60,1%) | 11.072.627 đ | 8.161.250 đ |
| K5 Tết, chưa khai ngày lễ | 7 | 36500 | 42,9% (CI 95%: 39,9% – 46,1%) | 3.149.326 đ | 2.332.520 đ |
| K5 Tết, chưa khai ngày lễ | 14 | 36500 | 50,6% (CI 95%: 47,0% – 54,2%) | 5.906.383 đ | 4.412.125 đ |
| K5 Tết, chưa khai ngày lễ | 30 | 36500 | 56,6% (CI 95%: 52,8% – 60,3%) | 12.208.051 đ | 9.216.718 đ |
| K5h Tết, đã khai ngày lễ 150% | 7 | 36500 | 43,4% (CI 95%: 40,0% – 46,5%) | 3.190.476 đ | 2.354.784 đ |
| K5h Tết, đã khai ngày lễ 150% | 14 | 36500 | 51,0% (CI 95%: 47,6% – 54,7%) | 5.981.377 đ | 4.457.172 đ |
| K5h Tết, đã khai ngày lễ 150% | 30 | 36500 | 56,3% (CI 95%: 52,7% – 60,3%) | 12.357.719 đ | 9.353.158 đ |
| K6 người dùng mới | 7 | 6200 | 44,7% (CI 95%: 41,0% – 48,5%) | 5.590.482 đ | 1.968.775 đ |
| K6 người dùng mới | 14 | 6200 | 49,7% (CI 95%: 45,2% – 53,9%) | 10.486.488 đ | 3.629.875 đ |
| K6 người dùng mới | 30 | 6200 | 51,3% (CI 95%: 46,4% – 56,5%) | 21.674.331 đ | 7.535.500 đ |

Theo hồ sơ (K0 đối chứng):

| Hồ sơ | Tầm | n | Tỉ lệ nằm trong dải | Rộng trung bình |
|---|---|---|---|---|
| stable_worker | 7 | 7300 | 62,1% (CI 95%: 59,6% – 64,4%) | 1.194.483 đ |
| stable_worker | 14 | 7300 | 75,1% (CI 95%: 71,8% – 78,1%) | 2.240.292 đ |
| stable_worker | 30 | 7300 | 84,6% (CI 95%: 81,7% – 87,6%) | 4.630.704 đ |
| freelancer | 7 | 7300 | 17,9% (CI 95%: 14,4% – 22,1%) | 5.727.104 đ |
| freelancer | 14 | 7300 | 26,5% (CI 95%: 22,8% – 30,1%) | 10.740.268 đ |
| freelancer | 30 | 7300 | 36,5% (CI 95%: 32,6% – 40,2%) | 22.197.464 đ |
| student | 7 | 7300 | 38,1% (CI 95%: 35,9% – 40,4%) | 1.051.356 đ |
| student | 14 | 7300 | 39,8% (CI 95%: 38,1% – 41,8%) | 1.972.228 đ |
| student | 30 | 7300 | 34,5% (CI 95%: 32,6% – 36,7%) | 4.076.966 đ |
| family | 7 | 7300 | 47,0% (CI 95%: 45,3% – 48,9%) | 2.510.911 đ |
| family | 14 | 7300 | 56,8% (CI 95%: 54,7% – 59,3%) | 4.709.977 đ |
| family | 30 | 7300 | 67,8% (CI 95%: 64,9% – 70,9%) | 9.737.302 đ |
| small_trader | 7 | 7300 | 42,2% (CI 95%: 40,1% – 44,2%) | 3.779.766 đ |
| small_trader | 14 | 7300 | 53,6% (CI 95%: 50,4% – 56,8%) | 7.090.148 đ |
| small_trader | 30 | 7300 | 67,0% (CI 95%: 62,7% – 71,6%) | 14.656.962 đ |

## 6. Chi linh hoạt (K0 đối chứng, chế độ đáp án, mọi hồ sơ)

| Tầm | Phương pháp | n | WAPE | Thiên lệch | MAPE (n, bị loại) |
|---|---|---|---|---|---|
| 7 | Mô hình | 36500 | 20,1% (CI 95%: 17,3% – 23,9%) | 0,2% | 32,0% (36500, 0) |
| 7 | B-mean30 (trung bình 30 ngày) | 36500 | 27,4% (CI 95%: 25,4% – 30,2%) | -0,3% | 35,8% (36500, 0) |
| 7 | B-naive (lặp lại h+1 ngày trước) | 36500 | 34,0% (CI 95%: 31,8% – 36,7%) | 0,0% | 43,0% (36500, 0) |
| 14 | Mô hình | 36500 | 16,5% (CI 95%: 13,9% – 19,8%) | 0,3% | 25,6% (36500, 0) |
| 14 | B-mean30 (trung bình 30 ngày) | 36500 | 21,6% (CI 95%: 19,3% – 24,3%) | -0,2% | 29,0% (36500, 0) |
| 14 | B-naive (lặp lại h+1 ngày trước) | 36500 | 24,2% (CI 95%: 21,8% – 27,2%) | -0,1% | 32,1% (36500, 0) |
| 30 | Mô hình | 36500 | 13,6% (CI 95%: 11,1% – 16,7%) | 0,3% | 21,9% (36500, 0) |
| 30 | B-mean30 (trung bình 30 ngày) | 36500 | 19,5% (CI 95%: 17,7% – 22,2%) | -0,2% | 26,0% (36500, 0) |
| 30 | B-naive (lặp lại h+1 ngày trước) | 36500 | 20,3% (CI 95%: 18,5% – 22,9%) | -0,3% | 26,3% (36500, 0) |

MAPE chỉ tính trên mốc có chi linh hoạt thực tế ≥ 100.000 đ.

## 6b. Thu không định kỳ (chế độ đáp án, mọi hồ sơ có thu không định kỳ)

| Kịch bản | Tầm | Cửa sổ | n | WAPE | Thiên lệch |
|---|---|---|---|---|---|
| K0 đối chứng | 7 | all | 36500 | 52,7% (CI 95%: 39,0% – 67,8%) | -0,8% |
| K0 đối chứng | 14 | all | 36500 | 40,8% (CI 95%: 30,1% – 53,1%) | -0,9% |
| K0 đối chứng | 30 | all | 36500 | 30,9% (CI 95%: 22,3% – 40,3%) | -1,0% |
| K1 chi đột xuất lớn | 7 | affected | 1575 | 56,0% (CI 95%: 38,7% – 74,6%) | -2,3% |
| K1 chi đột xuất lớn | 7 | all | 36500 | 52,7% (CI 95%: 39,3% – 68,9%) | -0,8% |
| K1 chi đột xuất lớn | 14 | affected | 2933 | 45,9% (CI 95%: 32,9% – 62,1%) | -3,4% |
| K1 chi đột xuất lớn | 14 | all | 36500 | 40,8% (CI 95%: 30,6% – 53,0%) | -0,9% |
| K1 chi đột xuất lớn | 30 | affected | 5954 | 34,5% (CI 95%: 23,7% – 48,1%) | -0,7% |
| K1 chi đột xuất lớn | 30 | all | 36500 | 30,9% (CI 95%: 23,2% – 40,4%) | -1,0% |
| K2 thu nhập về trễ / đổi số tiền | 7 | affected | 1600 | 59,9% (CI 95%: 45,9% – 76,3%) | 5,5% |
| K2 thu nhập về trễ / đổi số tiền | 7 | all | 36500 | 57,7% (CI 95%: 44,0% – 72,5%) | 2,2% |
| K2 thu nhập về trễ / đổi số tiền | 14 | affected | 3000 | 45,6% (CI 95%: 33,2% – 62,0%) | 2,1% |
| K2 thu nhập về trễ / đổi số tiền | 14 | all | 36500 | 45,6% (CI 95%: 34,9% – 58,9%) | 2,1% |
| K2 thu nhập về trễ / đổi số tiền | 30 | affected | 6200 | 34,2% (CI 95%: 25,1% – 45,2%) | -0,5% |
| K2 thu nhập về trễ / đổi số tiền | 30 | all | 36500 | 36,0% (CI 95%: 27,2% – 46,9%) | 1,8% |
| K3 bỏ một kỳ định kỳ | 7 | affected | 1240 | 52,7% (CI 95%: 35,4% – 72,1%) | -0,2% |
| K3 bỏ một kỳ định kỳ | 7 | all | 36500 | 52,7% (CI 95%: 39,7% – 67,5%) | -0,8% |
| K3 bỏ một kỳ định kỳ | 14 | affected | 2312 | 44,9% (CI 95%: 29,8% – 63,1%) | -2,9% |
| K3 bỏ một kỳ định kỳ | 14 | all | 36500 | 40,8% (CI 95%: 30,4% – 53,3%) | -0,9% |
| K3 bỏ một kỳ định kỳ | 30 | affected | 4709 | 34,7% (CI 95%: 23,8% – 48,3%) | -4,2% |
| K3 bỏ một kỳ định kỳ | 30 | all | 36500 | 30,9% (CI 95%: 22,9% – 40,6%) | -1,0% |
| K4 khoản định kỳ đổi giá | 7 | affected | 1572 | 53,7% (CI 95%: 38,5% – 71,0%) | -2,1% |
| K4 khoản định kỳ đổi giá | 7 | all | 36500 | 52,7% (CI 95%: 39,0% – 68,3%) | -0,8% |
| K4 khoản định kỳ đổi giá | 14 | affected | 2909 | 41,6% (CI 95%: 29,5% – 55,7%) | -4,6% |
| K4 khoản định kỳ đổi giá | 14 | all | 36500 | 40,8% (CI 95%: 30,8% – 54,0%) | -0,9% |
| K4 khoản định kỳ đổi giá | 30 | affected | 5827 | 33,8% (CI 95%: 23,8% – 45,1%) | -3,8% |
| K4 khoản định kỳ đổi giá | 30 | all | 36500 | 30,9% (CI 95%: 22,4% – 40,3%) | -1,0% |
| K5 Tết, chưa khai ngày lễ | 7 | affected | 2900 | 76,0% (CI 95%: 66,2% – 86,8%) | -34,6% |
| K5 Tết, chưa khai ngày lễ | 7 | all | 36500 | 59,5% (CI 95%: 46,3% – 75,1%) | -0,8% |
| K5 Tết, chưa khai ngày lễ | 14 | affected | 3600 | 63,7% (CI 95%: 52,4% – 74,8%) | -29,4% |
| K5 Tết, chưa khai ngày lễ | 14 | all | 36500 | 48,0% (CI 95%: 38,0% – 61,5%) | -0,9% |
| K5 Tết, chưa khai ngày lễ | 30 | affected | 5200 | 52,0% (CI 95%: 42,6% – 64,2%) | -21,0% |
| K5 Tết, chưa khai ngày lễ | 30 | all | 36500 | 38,5% (CI 95%: 30,2% – 49,2%) | -1,0% |
| K5h Tết, đã khai ngày lễ 150% | 7 | affected | 2900 | 76,0% (CI 95%: 66,0% – 86,8%) | -34,6% |
| K5h Tết, đã khai ngày lễ 150% | 7 | all | 36500 | 59,5% (CI 95%: 47,1% – 74,5%) | -0,8% |
| K5h Tết, đã khai ngày lễ 150% | 14 | affected | 3600 | 63,7% (CI 95%: 52,9% – 75,3%) | -29,4% |
| K5h Tết, đã khai ngày lễ 150% | 14 | all | 36500 | 48,0% (CI 95%: 38,1% – 60,8%) | -0,9% |
| K5h Tết, đã khai ngày lễ 150% | 30 | affected | 5200 | 52,0% (CI 95%: 42,5% – 65,0%) | -21,0% |
| K5h Tết, đã khai ngày lễ 150% | 30 | all | 36500 | 38,5% (CI 95%: 29,7% – 50,0%) | -1,0% |
| K6 người dùng mới | 7 | affected | 6200 | 54,2% (CI 95%: 40,1% – 70,1%) | -3,9% |
| K6 người dùng mới | 7 | all | 6200 | 54,2% (CI 95%: 40,6% – 69,8%) | -3,9% |
| K6 người dùng mới | 14 | affected | 6200 | 44,8% (CI 95%: 33,8% – 58,0%) | -4,9% |
| K6 người dùng mới | 14 | all | 6200 | 44,8% (CI 95%: 32,4% – 58,5%) | -4,9% |
| K6 người dùng mới | 30 | affected | 6200 | 37,6% (CI 95%: 26,9% – 51,0%) | -6,0% |
| K6 người dùng mới | 30 | all | 6200 | 37,6% (CI 95%: 27,1% – 50,2%) | -6,0% |

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
