# Phase 97 — mô hình dự báo số dư trước và sau sửa

> Sinh bằng `dotnet run --project FinMate.Backtest -c Release -- --compare-runs <ROOT> --notes <FILE> --out <FILE>` từ CSV của bốn lượt chạy. Bảng không sửa tay. "Trước" = mô hình ở `main` @ `e9664f7`; "sau" = mô hình cuối của Phase 97. Tập phát triển: hạt 20261127 (dùng để chọn mọi phương án); tập kiểm tra: hạt 20261128 (chạy một lần, sau khi mọi phương án đã chốt). Bộ sinh v1.2, chế độ đáp án, K0 trừ khi ghi khác.

Lượt chạy: `dev-before` (trước Phase 97), `dev-after` (mean_dow_income_sumband_v3), `test-before` (trước Phase 97), `test-after` (mean_dow_income_sumband_v3).

## Tóm tắt

Cách đọc: **lệch TB** là số tiền dự báo trật trung bình so với số dư thật sau ngần ấy ngày (nhỏ hơn là tốt). **Thiên lệch** cho biết dự báo hay cao hơn (+) hay thấp hơn (−) thực tế. **Trong dải** là tỉ lệ lần số dư thật nằm giữa đường thận trọng và đường lạc quan; mục tiêu khoảng 50%. **Rộng dải** là khoảng cách trung bình giữa hai đường. Tiền: triệu đồng.

### Tập phát triển

| Hồ sơ | Tầm (ngày) | Lệch TB trước → sau | Giảm | Thiên lệch trước → sau | Trong dải trước → sau | Rộng dải trước → sau |
|---|---|---|---|---|---|---|
| all | 7 | 5,17 → 3,29 | 36,3% | −3,93 → −0,22 | 36,4% → 48,1% | 1,89 → 4,78 |
| all | 14 | 9,05 → 4,67 | 48,4% | −7,14 → −0,18 | 35,2% → 46,2% | 3,55 → 6,79 |
| all | 30 | 17,83 → 7,27 | 59,2% | −14,49 → −0,10 | 35,0% → 45,3% | 7,34 → 10,50 |
| stable_worker | 7 | 0,88 → 0,81 | 7,5% | 0,15 → −0,24 | 60,8% → 42,2% | 1,19 → 0,70 |
| stable_worker | 14 | 1,19 → 0,98 | 17,5% | 0,51 → −0,22 | 65,6% → 43,5% | 2,24 → 0,99 |
| stable_worker | 30 | 1,92 → 1,33 | 30,8% | 1,33 → −0,18 | 70,5% → 43,1% | 4,63 → 1,53 |
| freelancer | 7 | 7,87 → 9,01 | −14,5% | −7,09 → −0,25 | 35,7% → 51,8% | 1,92 → 14,55 |
| freelancer | 14 | 13,97 → 13,25 | 5,2% | −13,26 → −0,43 | 22,4% → 44,3% | 3,59 → 20,62 |
| freelancer | 30 | 27,72 → 20,93 | 24,5% | −27,42 → −0,91 | 9,5% → 48,3% | 7,42 → 31,82 |
| student | 7 | 1,34 → 1,50 | −12,2% | −0,03 → −0,11 | 41,4% → 63,9% | 0,99 → 2,61 |
| student | 14 | 1,97 → 2,35 | −19,5% | 0,04 → −0,12 | 44,0% → 62,7% | 1,85 → 3,69 |
| student | 30 | 3,27 → 4,33 | −32,5% | 0,21 → −0,11 | 43,8% → 53,7% | 3,83 → 5,70 |
| family | 7 | 2,60 → 2,42 | 7,1% | 0,47 → −0,46 | 44,0% → 34,2% | 2,51 → 1,69 |
| family | 14 | 3,54 → 2,98 | 16,1% | 1,63 → −0,12 | 44,2% → 32,0% | 4,71 → 2,46 |
| family | 30 | 5,56 → 4,02 | 27,6% | 4,12 → 0,51 | 51,3% → 31,8% | 9,74 → 3,91 |
| small_trader | 7 | 13,15 → 2,73 | 79,3% | −13,15 → −0,02 | 0,0% → 48,1% | 2,85 → 4,36 |
| small_trader | 14 | 24,60 → 3,81 | 84,5% | −24,60 → 0,01 | 0,0% → 48,6% | 5,36 → 6,19 |
| small_trader | 30 | 50,69 → 5,73 | 88,7% | −50,69 → 0,18 | 0,0% → 49,6% | 11,07 → 9,55 |

### Tập kiểm tra

| Hồ sơ | Tầm (ngày) | Lệch TB trước → sau | Giảm | Thiên lệch trước → sau | Trong dải trước → sau | Rộng dải trước → sau |
|---|---|---|---|---|---|---|
| all | 7 | 4,91 → 3,07 | 37,6% | −3,68 → −0,15 | 36,5% → 48,6% | 1,85 → 4,55 |
| all | 14 | 8,60 → 4,35 | 49,4% | −6,66 → −0,06 | 36,0% → 45,8% | 3,47 → 6,46 |
| all | 30 | 16,96 → 6,80 | 59,9% | −13,54 → 0,11 | 36,0% → 45,1% | 7,17 → 10,00 |
| stable_worker | 7 | 0,88 → 0,83 | 5,3% | 0,15 → −0,26 | 62,6% → 43,7% | 1,17 → 0,78 |
| stable_worker | 14 | 1,21 → 1,05 | 13,8% | 0,51 → −0,25 | 67,7% → 43,2% | 2,20 → 1,11 |
| stable_worker | 30 | 1,95 → 1,45 | 25,9% | 1,35 → −0,23 | 73,6% → 43,1% | 4,55 → 1,71 |
| freelancer | 7 | 6,70 → 7,87 | −17,5% | −5,87 → 0,12 | 35,8% → 54,0% | 1,79 → 13,32 |
| freelancer | 14 | 11,83 → 11,64 | 1,7% | −11,00 → 0,22 | 24,6% → 44,4% | 3,36 → 18,88 |
| freelancer | 30 | 23,48 → 18,55 | 21,0% | −22,95 → 0,26 | 11,5% → 48,8% | 6,95 → 29,14 |
| student | 7 | 1,37 → 1,51 | −10,0% | −0,09 → −0,16 | 38,3% → 62,9% | 1,02 → 2,64 |
| student | 14 | 2,04 → 2,38 | −16,5% | −0,03 → −0,15 | 41,7% → 60,2% | 1,91 → 3,74 |
| student | 30 | 3,47 → 4,42 | −27,4% | 0,10 → −0,15 | 41,1% → 52,1% | 3,94 → 5,76 |
| family | 7 | 2,54 → 2,39 | 5,9% | 0,49 → −0,45 | 45,9% → 35,7% | 2,55 → 1,75 |
| family | 14 | 3,46 → 2,93 | 15,3% | 1,67 → −0,10 | 46,0% → 34,2% | 4,79 → 2,53 |
| family | 30 | 5,44 → 3,88 | 28,6% | 4,23 → 0,57 | 54,0% → 33,5% | 9,89 → 4,04 |
| small_trader | 7 | 13,08 → 2,73 | 79,1% | −13,08 → −0,03 | 0,0% → 46,7% | 2,71 → 4,28 |
| small_trader | 14 | 24,47 → 3,77 | 84,6% | −24,47 → 0,00 | 0,0% → 46,8% | 5,08 → 6,06 |
| small_trader | 30 | 50,44 → 5,73 | 88,6% | −50,44 → 0,12 | 0,0% → 47,8% | 10,49 → 9,35 |

## So với các cách đoán đơn giản

Ô = mô hình tốt hơn cách đoán đó bao nhiêu (1 − lệch TB của mô hình / lệch TB của cách đoán), trước → sau; âm là kém hơn. B-hold: số dư giữ nguyên. B0: chỉ khoản định kỳ. B-mean30: khoản định kỳ + chi trung bình 30 ngày. B-naive: lặp lại dòng tiền gần nhất.

### Tập phát triển

| Hồ sơ | Tầm | B-hold | B0 | B-mean30 | B-naive |
|---|---|---|---|---|---|
| all | 7 | 3,0% → 38,2% | −30,6% → 16,8% | 5,4% → 39,7% | 36,6% → 59,6% |
| all | 14 | −22,8% → 36,6% | −43,5% → 26,0% | 5,8% → 51,4% | 26,2% → 61,9% |
| all | 30 | −126,6% → 7,6% | −53,1% → 37,6% | 6,3% → 61,8% | −78,5% → 27,2% |
| stable_worker | 7 | 80,8% → 82,3% | 54,3% → 57,8% | −5,2% → 2,7% | 88,2% → 89,1% |
| stable_worker | 14 | 80,6% → 84,0% | 64,2% → 70,4% | −14,9% → 5,2% | 89,9% → 91,7% |
| stable_worker | 30 | 40,5% → 58,8% | 71,1% → 80,0% | −30,5% → 9,7% | 26,2% → 48,9% |
| freelancer | 7 | 1,4% → −12,9% | 0,0% → −14,5% | 3,7% → −10,2% | 33,3% → 23,6% |
| freelancer | 14 | −17,7% → −11,6% | −12,2% → −6,4% | 5,7% → 10,5% | 17,5% → 21,8% |
| freelancer | 30 | −47,7% → −11,5% | −29,2% → 2,5% | 8,0% → 30,5% | −9,5% → 17,3% |
| student | 7 | 24,5% → 15,4% | 9,8% → −1,1% | 33,0% → 24,8% | 55,8% → 50,5% |
| student | 14 | 19,6% → 4,0% | 17,7% → 1,6% | 39,5% → 27,7% | 56,3% → 47,8% |
| student | 30 | −11,8% → −48,2% | 28,4% → 5,2% | 44,2% → 26,1% | 34,4% → 13,1% |
| family | 7 | 69,3% → 71,5% | 41,3% → 45,5% | −5,6% → 1,9% | 80,3% → 81,7% |
| family | 14 | 68,6% → 73,7% | 52,7% → 60,3% | −14,6% → 3,8% | 83,7% → 86,3% |
| family | 30 | 8,6% → 33,8% | 63,2% → 73,3% | −26,5% → 8,4% | 31,1% → 50,1% |
| small_trader | 7 | −244,9% → 28,5% | −222,6% → 33,1% | 5,0% → 80,3% | −153,1% → 47,5% |
| small_trader | 14 | −382,0% → 25,4% | −317,0% → 35,5% | 5,0% → 85,3% | −287,2% → 40,1% |
| small_trader | 30 | −507,2% → 31,3% | −383,3% → 45,3% | 5,0% → 89,3% | −464,6% → 36,2% |

### Tập kiểm tra

| Hồ sơ | Tầm | B-hold | B0 | B-mean30 | B-naive |
|---|---|---|---|---|---|
| all | 7 | 3,0% → 39,5% | −30,8% → 18,3% | 5,5% → 41,0% | 36,2% → 60,2% |
| all | 14 | −23,9% → 37,3% | −43,1% → 27,6% | 5,9% → 52,4% | 24,8% → 61,9% |
| all | 30 | −130,6% → 7,5% | −51,6% → 39,1% | 6,3% → 62,4% | −84,9% → 25,8% |
| stable_worker | 7 | 80,7% → 81,8% | 54,7% → 57,2% | −2,1% → 3,3% | 88,2% → 88,8% |
| stable_worker | 14 | 80,0% → 82,8% | 63,8% → 68,8% | −9,0% → 6,1% | 89,6% → 91,0% |
| stable_worker | 30 | 40,4% → 55,8% | 70,8% → 78,4% | −19,9% → 11,1% | 29,6% → 47,8% |
| freelancer | 7 | 4,7% → −12,0% | 1,3% → −16,0% | 3,9% → −12,9% | 34,6% → 23,2% |
| freelancer | 14 | −14,4% → −12,5% | −11,1% → −9,3% | 6,3% → 7,9% | 18,2% → 19,6% |
| freelancer | 30 | −44,3% → −14,0% | −28,9% → −1,9% | 8,8% → 28,0% | −7,7% → 14,9% |
| student | 7 | 21,9% → 14,1% | 9,1% → −0,1% | 32,9% → 26,2% | 53,7% → 49,0% |
| student | 14 | 15,7% → 1,8% | 16,7% → 3,0% | 38,9% → 28,9% | 52,8% → 45,0% |
| student | 30 | −10,9% → −41,2% | 26,5% → 6,4% | 43,5% → 28,0% | 33,4% → 15,2% |
| family | 7 | 69,0% → 70,8% | 42,8% → 46,1% | −3,5% → 2,6% | 80,0% → 81,2% |
| family | 14 | 67,8% → 72,7% | 54,3% → 61,3% | −12,6% → 4,7% | 83,2% → 85,8% |
| family | 30 | 2,5% → 30,4% | 64,7% → 74,8% | −28,5% → 8,2% | 25,2% → 46,6% |
| small_trader | 7 | −245,2% → 27,8% | −218,2% → 33,5% | 4,3% → 80,0% | −153,6% → 47,0% |
| small_trader | 14 | −376,8% → 26,5% | −304,9% → 37,6% | 4,3% → 85,3% | −296,6% → 38,9% |
| small_trader | 30 | −491,9% → 32,7% | −362,6% → 47,4% | 4,3% → 89,1% | −474,0% → 34,8% |

## Tập phát triển so với tập kiểm tra (mô hình sau)

**Khác đáng kể** (ngưỡng chốt trước khi chạy tập kiểm tra): số của tập phát triển nằm ngoài khoảng tin cậy 95% của tập kiểm tra, với tỉ lệ trong dải hoặc với mức tốt hơn B-hold.

| Hồ sơ | Tầm | Lệch TB dev / test | Thiên lệch dev / test | Trong dải dev / test (CI test) | Tốt hơn B-hold dev / test (CI test) | Khác đáng kể |
|---|---|---|---|---|---|---|
| all | 7 | 3,29 / 3,07 | −0,22 / −0,15 | 48,1% / 48,6% (46,5% – 50,7%) | 38,2% / 39,5% (30,1% – 48,3%) | — |
| all | 14 | 4,67 / 4,35 | −0,18 / −0,06 | 46,2% / 45,8% (43,8% – 47,7%) | 36,6% / 37,3% (27,7% – 46,6%) | — |
| all | 30 | 7,27 / 6,80 | −0,10 / 0,11 | 45,3% / 45,1% (42,8% – 47,1%) | 7,6% / 7,5% (−0,5% – 15,9%) | — |
| stable_worker | 7 | 0,81 / 0,83 | −0,24 / −0,26 | 42,2% / 43,7% (42,2% – 45,4%) | 82,3% / 81,8% (80,3% – 83,0%) | — |
| stable_worker | 14 | 0,98 / 1,05 | −0,22 / −0,25 | 43,5% / 43,2% (40,4% – 45,9%) | 84,0% / 82,8% (80,8% – 84,6%) | — |
| stable_worker | 30 | 1,33 / 1,45 | −0,18 / −0,23 | 43,1% / 43,1% (38,8% – 47,3%) | 58,8% / 55,8% (45,8% – 64,3%) | — |
| freelancer | 7 | 9,01 / 7,87 | −0,25 / 0,12 | 51,8% / 54,0% (49,9% – 57,4%) | −12,9% / −12,0% (−14,6% – −9,3%) | — |
| freelancer | 14 | 13,25 / 11,64 | −0,43 / 0,22 | 44,3% / 44,4% (41,3% – 47,3%) | −11,6% / −12,5% (−16,0% – −9,0%) | — |
| freelancer | 30 | 20,93 / 18,55 | −0,91 / 0,26 | 48,3% / 48,8% (44,1% – 53,6%) | −11,5% / −14,0% (−20,3% – −7,4%) | — |
| student | 7 | 1,50 / 1,51 | −0,11 / −0,16 | 63,9% / 62,9% (61,6% – 64,3%) | 15,4% / 14,1% (9,6% – 18,0%) | — |
| student | 14 | 2,35 / 2,38 | −0,12 / −0,15 | 62,7% / 60,2% (58,2% – 62,3%) | 4,0% / 1,8% (−3,2% – 6,4%) | **có**: trong dải |
| student | 30 | 4,33 / 4,42 | −0,11 / −0,15 | 53,7% / 52,1% (49,9% – 54,4%) | −48,2% / −41,2% (−48,6% – −33,3%) | — |
| family | 7 | 2,42 / 2,39 | −0,46 / −0,45 | 34,2% / 35,7% (34,0% – 37,3%) | 71,5% / 70,8% (69,6% – 72,1%) | — |
| family | 14 | 2,98 / 2,93 | −0,12 / −0,10 | 32,0% / 34,2% (31,7% – 36,8%) | 73,7% / 72,7% (71,0% – 74,3%) | — |
| family | 30 | 4,02 / 3,88 | 0,51 / 0,57 | 31,8% / 33,5% (29,4% – 37,3%) | 33,8% / 30,4% (22,5% – 37,1%) | — |
| small_trader | 7 | 2,73 / 2,73 | −0,02 / −0,03 | 48,1% / 46,7% (45,1% – 48,4%) | 28,5% / 27,8% (23,4% – 31,9%) | — |
| small_trader | 14 | 3,81 / 3,77 | 0,01 / 0,00 | 48,6% / 46,8% (44,3% – 49,2%) | 25,4% / 26,5% (17,5% – 34,9%) | — |
| small_trader | 30 | 5,73 / 5,73 | 0,18 / 0,12 | 49,6% / 47,8% (44,5% – 51,2%) | 31,3% / 32,7% (17,7% – 45,3%) | — |

Số ô khác đáng kể: 1 / 18.

## Thu không định kỳ (tầm 14, mô hình sau)

Mô hình trước không chiếu thu không định kỳ (thiên lệch −100%). Ô = WAPE / thiên lệch so với thực tế. K2: khoản thu định kỳ về trễ một tháng, rồi đổi số tiền vĩnh viễn.

| Kịch bản | Mốc | Hồ sơ | Tập phát triển | Tập kiểm tra |
|---|---|---|---|---|
| K0 | mọi mốc | all | 40,8% / −0,9% | 39,2% / 0,7% |
| K0 | mọi mốc | freelancer | 92,5% / −2,6% | 96,0% / 2,1% |
| K0 | mọi mốc | student | 6,8% / −0,0% | 7,2% / 0,2% |
| K0 | mọi mốc | small_trader | 13,7% / −0,1% | 13,8% / 0,1% |
| K2 | mọi mốc | all | 42,6% / −1,0% | 42,3% / 0,4% |
| K2 | mọi mốc | freelancer | 93,2% / −3,1% | 98,3% / 0,3% |
| K2 | mọi mốc | student | 8,9% / 2,3% | 9,1% / 2,4% |
| K2 | mọi mốc | small_trader | 14,8% / −1,2% | 15,1% / −0,2% |
| K2 | bị ảnh hưởng | all | 45,6% / 2,1% | 43,7% / 10,6% |
| K2 | bị ảnh hưởng | freelancer | 110,5% / 8,5% | 124,0% / 35,9% |
| K2 | bị ảnh hưởng | student | 7,2% / −0,2% | 6,8% / 0,7% |
| K2 | bị ảnh hưởng | small_trader | 16,0% / −0,9% | 18,6% / 2,6% |

## Kết luận ngắn

- **Trên cả hai tập, mô hình mới đoán số dư sát hơn nhiều.** Gộp mọi hồ sơ, ở tầm 14 ngày lệch trung bình giảm khoảng một nửa (tập phát triển 9,05 → 4,67 triệu; tập kiểm tra 8,60 → 4,35 triệu). Thiên lệch, trước đây luôn thấp hơn thực tế 4–14 triệu, giờ gần 0 (tập kiểm tra tầm 14: −6,66 → −0,06 triệu).
- **Dải thận trọng–lạc quan giờ có nghĩa:** khoảng một nửa số lần số dư thật nằm trong dải (tập kiểm tra 48,6% / 45,8% / 45,1% ở tầm 7/14/30; trước 36–37%).
- **Tập kiểm tra khớp tập phát triển.** Chỉ 1/18 ô khác đáng kể theo ngưỡng đã chốt trước: sinh viên tầm 14, tỉ lệ trong dải 62,7% ở tập phát triển so với 60,2% (CI 58,2–62,3%) ở tập kiểm tra. Ô này nằm sát mép khoảng tin cậy và cùng phía (dải của sinh viên rộng quá ở cả hai tập).
- **So với các cách đoán đơn giản:** gộp lại, mô hình tốt hơn cả bốn ở mọi tầm, trên cả hai tập. Riêng tầm 30, mức tốt hơn "giữ nguyên số dư" chỉ khoảng 7–8%, và khoảng tin cậy ở tập kiểm tra chạm 0 (−0,5% – 15,9%). Ở tầm này chưa khẳng định được mô hình hơn B-hold.

## Tập phát triển so với tập kiểm tra: chỗ khác

- **Thu không định kỳ của freelancer ở K2, các mốc bị ảnh hưởng:** thiên lệch +8,5% (tập phát triển) so với **+35,9%** (tập kiểm tra) ở tầm 14; gộp mọi hồ sơ +2,1% so với +10,6%. Ở tháng M của K2, thu dự án của freelancer về trễ 7–14 ngày; mô hình không biết trước nên chiếu thu như thường, và các cửa sổ ngắn bắt trúng khoảng tiền chưa về. Mức lệch phụ thuộc số dự án rơi vào tháng M của từng người, mà nhóm này chỉ có 20 người (WAPE 110–148%), nên dao động mạnh giữa hai tập. Ở tầm 30, khi tiền đã về trong cửa sổ, hai tập khớp nhau (+4,2% so với +5,2%). Đây không phải lỗi code, và không có sửa nào sau khi thấy số.
- Các ô khác của bảng thu không định kỳ chênh dưới 6 điểm % giữa hai tập. Lớn nhất cũng là freelancer: WAPE ở K2 mọi mốc 93,2% so với 98,3%; thiên lệch ở K0 −2,6% so với +2,1%.
- **Lệch trung bình của freelancer** thấp hơn 11–15% ở tập kiểm tra ở mọi tầm, cả trước lẫn sau (sau, tầm 14: 13,25 so với 11,64 triệu). Đây là khác biệt của dữ liệu giữa hai hạt, không phải của mô hình; mức tốt hơn các cách đoán đơn giản giữ nguyên.

## Điểm yếu còn lại

1. **Freelancer ở tầm 7 kém mô hình cũ và kém ba cách đoán đơn giản.** Tập kiểm tra: lệch 6,70 → 7,87 triệu; thua B-hold 12%, B0 16%, B-mean30 13% (tập phát triển tương tự). Mô hình mới chiếu thu dự án rải đều mỗi ngày (đúng về kỳ vọng, nên hết thiên lệch), trong khi tiền về từng cục vài lần mỗi tháng. Ở cửa sổ ngắn, "chưa có cục nào" hay gặp hơn "đúng mức trung bình". Mô hình cũ luôn thấp hơn thực tế (không chiếu thu) nhưng ít dao động, nên ở tầm 7 lại lệch ít hơn. Từ tầm 14 trở đi mô hình mới tốt hơn mô hình cũ. Dải của freelancer đã phản ánh bất định này (44–54% trong dải, rộng 13–29 triệu).
2. **Dải của gia đình hẹp quá:** 32–36% trong dải thay vì 50%, ở cả hai tập. Dải chỉ đo bất định của dòng tiền linh hoạt. Phần lệch của gia đình nằm ở khoản định kỳ (lệch ngày, lệch tiền) và khoản vay (trả hằng tháng trước hạn, không được chiếu, xem D8), là những phần dải chưa đo.
3. **Sinh viên kém đi và dải rộng quá.** Lệch tăng ở mọi tầm (tập kiểm tra tầm 30: 3,47 → 4,42 triệu; thua B-hold 41%), dải chứa 52–64% ở cả hai tập. Học phí theo kỳ (8–15 triệu, vài tháng một lần) nằm trong 90 ngày lịch sử, làm trung bình chi và phương sai lớn lên. Đây là cái giá đã chấp nhận khi chọn trung bình (D6): với sinh viên, thiên lệch là lỗi lớn hơn. Thiên lệch hiện gần 0 (−0,15 triệu).
4. **Lương vừa về trễ quá cửa sổ nhận vừa đổi số tiền** vẫn vào thu linh hoạt (ca hẹp, có test ghi lại).
5. **Tầm 30 gộp chỉ hơn B-hold khoảng 7–8%** và chưa có ý nghĩa thống kê ở tập kiểm tra (xem trên).

## Vì sao thiên lệch của gia đình ở K0 tầm 30 đổi từ +0,12 thành +0,51 triệu sau Task 7b

- Theo thành phần (tập phát triển, `--diagnose` mục C1, sau Task 7b): sai số +0,51 triệu ở tầm 30 gồm khoản vay / chuyển ví **+1,06**, chi linh hoạt +0,15, thu định kỳ −0,47, chi định kỳ −0,22. Phần +1,06 là các lần trả nợ hằng tháng trước hạn mà mô hình không chiếu (D8: sổ vay không có lịch trả).
- Trước Task 7b, các hóa đơn điện, nước dao động quá 10% giữa các tháng bị đếm **hai lần**: một lần ở ngày đến hạn, một lần trong chi linh hoạt (thiên lệch chi linh hoạt của gia đình +1,7% ở tầm 30). Phần đếm thừa này vô tình bù gần hết phần trả nợ không được chiếu, nên tổng chỉ còn +0,12.
- Task 7b bỏ phần đếm hai lần (thiên lệch chi linh hoạt về −1,1%), nên phần trả nợ không được chiếu lộ ra. Con số mới đúng hơn về cơ chế, dù trông kém hơn. Lệch trung bình không đổi (4,02 triệu); tập kiểm tra cho cùng hình ảnh (+0,57).

## Ảnh hưởng tới sản phẩm

- **Đường thận trọng / lạc quan** (`lowBalanceCents` / `highBalanceCents` của `/forecast/cashflow`) giờ là khoảng P25–P75 của số dư: khoảng một nửa khả năng số dư thật nằm trong vùng này. Trước đây nó là chi theo phân vị 75/25 cộng dồn từng ngày, rộng dần quá mức theo tầm với người thu chi đều (85% số lần nằm trong dải ở tầm 30) và quá hẹp với người thu không đều (vì không có thu). Chi tiết: `docs/forecast-cashflow-api.md`.
- **"Mua món này có ổn không?" (purchase-check)** so số dư thấp nhất trên **đường thận trọng** sau khi mua với mức dự phòng. Với người thu chi đều, đường này giờ cao hơn ở tầm dài (dải hẹp lại), nên kết luận bớt khắt khe. Với người thu không đều (freelancer, tiểu thương), đường thận trọng có cả bất định của thu, nên kết luận khắt khe hơn trước, dù đường dự kiến cao hơn. Không test nào của purchase-check đổi kỳ vọng.
- **Cảnh báo số dư thấp** dùng **đường dự kiến**, nên không bị ảnh hưởng bởi dải. Nó đổi theo tầng 1: với người thu không đều, đường dự kiến cao hơn trước (có thu không định kỳ), nên cảnh báo ít hơn và đúng hơn. Với người thu chi đều, đường dự kiến hơi thấp hơn (chi theo trung bình thay vì trung vị), nên cảnh báo có thể sớm hơn vài ngày.
- **Spendable** (`/forecast/spendable`, `/me/spendable`), trang thu nhập, ngân sách, bất thường: không đổi.
- **Báo cáo độ chính xác** (`/ops/forecast/accuracy`): bản chụp mới mang phương pháp `mean_dow_income_sumband_v3`; số cũ xem bằng `?method=`.

## Hướng mở rộng (chưa làm)

- Dải có thêm bất định của khoản định kỳ và khoản vay (lệch ngày, lệch tiền), để dải của gia đình đúng 50%.
- Khoản theo kỳ học phí (vài tháng một lần) tách khỏi chi linh hoạt, để sinh viên không trả giá cho D6.
- Thu không đều theo "cục": mô hình số lần nhận và cỡ mỗi lần, thay cho mức trung bình mỗi ngày, cho freelancer ở tầm ngắn.

## Lần chạy lại sau sửa lỗi review

**Lý do.** Review PR Phase 97 có ba nhận xét, đều đã sửa, mỗi nhận xét một commit:

1. `f6b963c`: `RecurringMatcher.PeriodPayments` chỉ lùi từ `NextDueDate`. Sau một kỳ bị bỏ, `RecurringTracker` dừng ở kỳ chưa trả, nên lần trả của kỳ sau với số tiền ngoài dung sai (ví dụ trả gộp hai kỳ, K3) rơi vào chi linh hoạt. Nay hàm này đi tiếp cả về phía sau, qua `RecurringSchedule.DueDates`.
2. `ff0c8c8`: purchase-check thêm dòng "Thu nhập không định kỳ (ước tính)". Sửa này không đổi số dư dự báo.
3. `5ce9e4f`: sửa chữ mô tả dải trong báo cáo. Không đổi số.

Chỉ (1) có thể đổi số của mô hình. Theo luật đã chốt, tập phát triển và tập kiểm tra được chạy lại **đúng một lần** sau khi áp cả ba sửa, ở commit `5ce9e4f`, vào `dev-after-review/` và `test-after-review/`. `test-after/` được giữ nguyên làm lần chạy đầu; trong thư mục này chỉ sửa chữ ở mục dải. Tên phương pháp giữ `mean_dow_income_sumband_v3`, vì đây là sửa lỗi chứ không đổi phương pháp.

**Kết quả: không có chênh lệch nào.** Cả 5 file CSV (`balance`, `band`, `flexible`, `income`, `effects`) của cả hai tập trùng từng byte với lần chạy đầu: mọi kịch bản, mọi hồ sơ, mọi tầm. Hai file `forecast-backtest.md` chỉ khác dòng commit. Bảng dưới đây trích phần gộp mọi hồ sơ:

| Tập | Kịch bản | Tầm | n | Sai số TB (tr) lần đầu → lại | Thiên lệch (tr) | Trong dải | Rộng dải (tr) | Chênh lệch |
|---|---|---|---|---|---|---|---|---|
| phát triển | K0 | 7 | 36500 | 3,29 → 3,29 | −0,22 → −0,22 | 48,1% → 48,1% | 4,78 → 4,78 | 0 |
| phát triển | K0 | 14 | 36500 | 4,67 → 4,67 | −0,18 → −0,18 | 46,2% → 46,2% | 6,79 → 6,79 | 0 |
| phát triển | K0 | 30 | 36500 | 7,27 → 7,27 | −0,10 → −0,10 | 45,3% → 45,3% | 10,50 → 10,50 | 0 |
| phát triển | K3 | 7 | 36500 | 3,35 → 3,35 | −0,30 → −0,30 | 47,2% → 47,2% | 4,78 → 4,78 | 0 |
| phát triển | K3 | 14 | 36500 | 4,76 → 4,76 | −0,30 → −0,30 | 45,1% → 45,1% | 6,79 → 6,79 | 0 |
| phát triển | K3 | 30 | 36500 | 7,39 → 7,39 | −0,31 → −0,31 | 44,1% → 44,1% | 10,50 → 10,50 | 0 |
| phát triển | K4 | 7 | 36500 | 3,31 → 3,31 | −0,21 → −0,21 | 47,8% → 47,8% | 4,78 → 4,78 | 0 |
| phát triển | K4 | 14 | 36500 | 4,70 → 4,70 | −0,15 → −0,15 | 45,8% → 45,8% | 6,79 → 6,79 | 0 |
| phát triển | K4 | 30 | 36500 | 7,29 → 7,29 | −0,04 → −0,04 | 44,9% → 44,9% | 10,50 → 10,50 | 0 |
| kiểm tra | K0 | 7 | 36500 | 3,07 → 3,07 | −0,15 → −0,15 | 48,6% → 48,6% | 4,55 → 4,55 | 0 |
| kiểm tra | K0 | 14 | 36500 | 4,35 → 4,35 | −0,06 → −0,06 | 45,8% → 45,8% | 6,46 → 6,46 | 0 |
| kiểm tra | K0 | 30 | 36500 | 6,80 → 6,80 | 0,11 → 0,11 | 45,1% → 45,1% | 10,00 → 10,00 | 0 |
| kiểm tra | K3 | 7 | 36500 | 3,13 → 3,13 | −0,24 → −0,24 | 47,5% → 47,5% | 4,55 → 4,55 | 0 |
| kiểm tra | K3 | 14 | 36500 | 4,44 → 4,44 | −0,18 → −0,18 | 44,4% → 44,4% | 6,46 → 6,46 | 0 |
| kiểm tra | K3 | 30 | 36500 | 6,93 → 6,93 | −0,11 → −0,11 | 43,3% → 43,3% | 10,00 → 10,00 | 0 |
| kiểm tra | K4 | 7 | 36500 | 3,09 → 3,09 | −0,15 → −0,15 | 48,2% → 48,2% | 4,55 → 4,55 | 0 |
| kiểm tra | K4 | 14 | 36500 | 4,38 → 4,38 | −0,03 → −0,03 | 45,4% → 45,4% | 6,46 → 6,46 | 0 |
| kiểm tra | K4 | 30 | 36500 | 6,84 → 6,84 | 0,17 → 0,17 | 44,8% → 44,8% | 10,00 → 10,00 | 0 |

**Vì sao bằng 0.** Chế độ đáp án (`OracleRecurring`) đặt `NextDueDate` ngay sau kỳ **đã trả** gần nhất, nên một kỳ bị bỏ không bao giờ giữ `NextDueDate` lại phía sau. Lần trả gộp của K3 vì vậy luôn nằm trước `NextDueDate` và đã được vòng lùi nhận từ trước. Trạng thái mà nhận xét (1) sửa, tức tracker dừng ở kỳ chưa trả trong khi kỳ sau đã trả, chỉ có ở production (`RecurringTracker`). Chế độ phát lại (replay) mới tái hiện được trạng thái này, và chế độ đó chưa được xây (Phase 96, tầng 2). Lỗi này vì vậy được chứng minh bằng test đơn vị (đỏ trên code cũ, xanh sau sửa), không phải bằng backtest. Backtest chỉ xác nhận rằng sửa lỗi không làm đổi các con số đã báo cáo.

## Tái lập

```
# trước (worktree ở main @ e9664f7)
dotnet run --project FinMate.Backtest -c Release -- --seed 20261127 --out <ROOT>/dev-before
dotnet run --project FinMate.Backtest -c Release -- --seed 20261128 --out <ROOT>/test-before
# sau (nhánh feat/forecast-model-fixes)
dotnet run --project FinMate.Backtest -c Release -- --seed 20261127 --out <ROOT>/dev-after
dotnet run --project FinMate.Backtest -c Release -- --seed 20261128 --out <ROOT>/test-after
# chạy lại sau sửa lỗi review (commit 5ce9e4f)
dotnet run --project FinMate.Backtest -c Release -- --seed 20261127 --out <ROOT>/dev-after-review
dotnet run --project FinMate.Backtest -c Release -- --seed 20261128 --out <ROOT>/test-after-review
# báo cáo này
dotnet run --project FinMate.Backtest -c Release -- --compare-runs ../docs/evaluation/phase97 --notes ../docs/evaluation/phase97/notes.md --out ../docs/evaluation/forecast-model-fixes.md
```

`dev-before` trùng từng byte với CSV của báo cáo Phase 96 (`docs/evaluation/forecast-backtest/`). Tập kiểm tra chạy đúng một lần cho mỗi mô hình, sau khi 11 phương án đã được chốt. Không có thay đổi mô hình nào sau khi thấy số của tập kiểm tra.
