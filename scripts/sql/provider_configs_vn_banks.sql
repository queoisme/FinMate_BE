-- Thêm các ngân hàng / ví Việt Nam còn thiếu vào bảng provider_configs (DB backend).
--
-- Nguồn tên gói: kết quả deep research (Google Play + nguồn thứ hai), ngày 2026-10-30.
-- Đã kiểm chứng trên điện thoại thật: com.vnpay.bidv (adb shell pm list packages).
-- Quy tắc: CHỈ bật (is_active = TRUE) provider đã kiểm chứng bằng thông báo/thiết bị thật.
-- Các provider còn lại để TẮT: sai package_name là hỏng im lặng (người dùng chọn rồi không thông
-- báo nào khớp). Bật từng cái bằng PATCH /api/v1/admin/provider-configs/{id}/activation sau khi
-- kiểm tra trên máy thật.
--
-- An toàn khi chạy lại: ON CONFLICT DO NOTHING (bỏ qua nếu provider_key HOẶC package_name đã có).
-- Đã có sẵn trong hệ thống nên KHÔNG thêm ở đây: mb_bank, vietcombank, momo, zalopay, vnpay,
-- techcombank, vpbank, shopeepay.
--   Lưu ý shopeepay: bản seed hiện có dùng package com.shopee.vn, còn nghiên cứu cho thấy
--   ShopeePay là com.beeasy.toppay (com.shopee.vn là app Shopee). Cần sửa bằng admin API.
--
-- Chạy (ví dụ): psql -U <user> -d <db backend> -f scripts/sql/provider_configs_vn_banks.sql

INSERT INTO provider_configs (id, provider_key, display_name, package_name, account_type, is_active, created_at, updated_at)
VALUES
    -- Đã kiểm chứng: bật
    (gen_random_uuid(), 'bidv',          'BIDV',                 'com.vnpay.bidv',                      'bank',    TRUE,  now(), now()),
    (gen_random_uuid(), 'vietinbank',    'VietinBank',           'com.vietinbank.ipay',                 'bank',    TRUE,  now(), now()),

    -- Ngân hàng: chưa kiểm chứng trên máy thật, để tắt
    (gen_random_uuid(), 'agribank',      'Agribank',             'com.vnpay.Agribank3g',                'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'acb',           'ACB',                  'mobile.acb.com.vn',                   'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'sacombank',     'Sacombank',            'com.sacombank.ewallet',               'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'tpbank',        'TPBank',               'com.tpb.mb.gprsandroid',              'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'vib',           'VIB',                  'com.vib.myvib2',                      'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'msb',           'MSB',                  'com.msb.digibank.retail',             'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'ocb',           'OCB',                  'vn.com.ocb.awe',                      'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'shb',           'SHB',                  'vn.shb.saha.mbanking',                'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'seabank',       'SeABank',              'vn.com.seabank.mb1',                  'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'lpbank',        'LPBank',               'vn.com.lpbank.digibank',              'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'eximbank',      'Eximbank',             'com.vnpay.EximBankOmni',              'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'namabank',      'Nam A Bank',           'ops.namabank.com.vn',                 'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'bacabank',      'Bac A Bank',           'com.bab.retailUAT',                   'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'abbank',        'ABBank',               'vn.abbank.retail',                    'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'ncb',           'NCB',                  'com.ncb.bank',                        'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'pvcombank',     'PVcomBank',            'com.pvcombank.retail',                'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'bvbank',        'BVBank (Bản Việt)',    'vn.banvietbank.mobilebanking',        'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'cake',          'Cake',                 'xyz.be.cake',                         'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'tnex',          'TNEX',                 'vn.tnex.consumer',                    'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'timo',          'Timo',                 'io.lifestyle.plus',                   'bank',    FALSE, now(), now()),

    -- Độ chắc chắn Thấp / Trung bình theo nghiên cứu (chỉ một nguồn, hoặc đang chuyển đổi app): để tắt
    (gen_random_uuid(), 'hdbank',        'HDBank (app cũ)',      'com.vnpay.hdbank',                    'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'hdbank_di',     'HDBank (Đi HDBank)',   'com.hdbank.dihdbank',                 'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'vietbank',      'Vietbank',             'com.vn.vietbankomni',                 'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'baovietbank',   'BAOVIET Bank',         'com.vnpay.bvbank',                    'bank',    FALSE, now(), now()),
    (gen_random_uuid(), 'kienlongbank',  'KienlongBank',         'com.sunshine.ksbank',                 'bank',    FALSE, now(), now()),

    -- Ví điện tử: để tắt
    (gen_random_uuid(), 'viettelmoney',  'Viettel Money',        'com.bplus.vtpay',                     'ewallet', FALSE, now(), now()),
    (gen_random_uuid(), 'vnptmoney',     'VNPT Money',           'vnptpay.vnptmedia.vnpt.com.vnptpay',  'ewallet', FALSE, now(), now())
ON CONFLICT DO NOTHING;
