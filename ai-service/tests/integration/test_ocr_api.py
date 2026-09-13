"""``POST /api/v1/ocr`` chạy thật, trên Postgres thật."""

import io

import pytest
from sqlalchemy import select

from app.api.v1 import ocr as ocr_route
from app.core.anonymizer import sha256_hex
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.raw_sample import RawSample

RECEIPT = """WINMART+ NGUYEN TRAI
MST: 0312345678
Ngay: 12/09/2026 19:45
Sua tuoi Vinamilk   2 x 32.000   64.000
Banh mi sandwich    1 x 25.000   25.000
Tong cong:                      110.000
Tien khach dua:                 200.000"""


class FakeEngine:
    def __init__(self, text: str) -> None:
        self.text = text

    def read_text(self, image: bytes) -> str:
        return self.text


@pytest.fixture(autouse=True)
def fake_engine():
    """Không cần binary tesseract để chạy test.

    Engine thật được xác minh riêng bằng smoke test trên `docker compose`, nơi Dockerfile đã
    cài `tesseract-ocr-vie` — chứ không bắt mọi máy chạy test phải có gói hệ điều hành đó.
    """
    original = ocr_route._engine
    ocr_route.set_engine(FakeEngine(RECEIPT))
    yield
    ocr_route.set_engine(original)


def image_file(
    name: str = "hoa-don.jpg", content_type: str = "image/jpeg", size: int = 32
):
    return {"file": (name, io.BytesIO(b"x" * size), content_type)}


async def test_missing_api_key_is_rejected(client, user_hash):
    response = await client.post(
        "/api/v1/ocr", files=image_file(), data={"user_id_hash": user_hash}
    )
    assert response.status_code == 401


async def test_receipt_becomes_prefill_fields(client, auth_headers, user_hash):
    response = await client.post(
        "/api/v1/ocr",
        files=image_file(),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )
    assert response.status_code == 200

    body = response.json()
    assert body["ocr_result"] == "success"
    assert body["extraction"]["amount_cents"] == 110_000
    assert body["extraction"]["transaction_type"] == "debit"
    assert body["extraction"]["merchant_name"] == "WINMART+ NGUYEN TRAI"
    # Tên cửa hàng quyết định danh mục, không phải các dòng hàng trong giỏ: một hóa đơn
    # WinMart có dòng "Banh mi" vẫn là mua sắm, không phải ăn uống.
    assert body["categorization"]["category_slug"] == "shopping"


async def test_response_carries_no_raw_receipt_text(client, auth_headers, user_hash):
    """Client đã cầm sẵn tấm ảnh; đẩy thêm toàn văn hóa đơn qua hai hệ thống chỉ để hiển thị
    là chuyển dữ liệu nhạy cảm đi xa hơn mức cần."""
    response = await client.post(
        "/api/v1/ocr",
        files=image_file(),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )
    assert "Tien khach dua" not in response.text
    assert set(response.json()) == {
        "ocr_result",
        "extraction",
        "categorization",
        "processing_ms",
    }


async def test_unreadable_image_is_not_an_error(client, auth_headers, user_hash):
    """ "Ảnh mờ, chụp lại đi" phải phân biệt được với "hệ thống hỏng, thử lại sau"."""
    ocr_route.set_engine(FakeEngine("   "))

    response = await client.post(
        "/api/v1/ocr",
        files=image_file(),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )

    assert response.status_code == 200
    assert response.json()["ocr_result"] == "unreadable"
    assert response.json()["extraction"] is None


async def test_text_without_a_total_is_reported_separately(
    client, auth_headers, user_hash
):
    ocr_route.set_engine(FakeEngine("CUA HANG ABC\nCam on quy khach"))

    response = await client.post(
        "/api/v1/ocr",
        files=image_file(),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )

    assert response.json()["ocr_result"] == "no_amount"


@pytest.mark.parametrize(
    ("content_type", "expected"),
    [("text/plain", 415), ("application/pdf", 415), ("image/png", 200)],
)
async def test_only_images_are_accepted(
    client, auth_headers, user_hash, content_type, expected
):
    response = await client.post(
        "/api/v1/ocr",
        files=image_file(content_type=content_type),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )
    assert response.status_code == expected


async def test_oversized_image_is_rejected(client, auth_headers, user_hash):
    """Không chặn thì một request 50MB ngốn hết bộ nhớ tiến trình trước khi có ai kịp từ chối."""
    response = await client.post(
        "/api/v1/ocr",
        files=image_file(size=ocr_route.MAX_IMAGE_BYTES + 1),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )
    assert response.status_code == 413


async def test_the_run_is_logged_and_the_text_is_kept_anonymised(
    client, auth_headers, user_hash, session
):
    await client.post(
        "/api/v1/ocr",
        files=image_file(),
        data={"user_id_hash": user_hash},
        headers=auth_headers,
    )

    row = (
        await session.execute(
            select(PipelineRequest).where(PipelineRequest.user_id_hash == user_hash)
        )
    ).scalar_one()
    assert row.package_name == ocr_route.RECEIPT_PACKAGE
    assert row.amount_cents == 110_000
    assert row.matched_pattern_name == "tong cong"
    assert row.raw_sample_id is not None

    sample = (
        await session.execute(
            select(RawSample).where(RawSample.id == row.raw_sample_id)
        )
    ).scalar_one()
    assert sample.source == "receipt"
    # Mã số thuế là dãy 10 số liền — anonymizer phải che nó như che số tài khoản.
    assert "0312345678" not in sample.notification_body
    assert "110.000" in sample.notification_body


async def test_two_users_do_not_share_a_sample_row(client, auth_headers, session):
    """raw_samples chống trùng theo nội dung, nên hai người chụp cùng một mẫu hóa đơn dùng
    chung một dòng. Điều đó ổn với dữ liệu huấn luyện, nhưng nhật ký chạy thì phải tách.
    """
    first = sha256_hex("nguoi-dung-1")
    second = sha256_hex("nguoi-dung-2")

    for user in (first, second):
        await client.post(
            "/api/v1/ocr",
            files=image_file(),
            data={"user_id_hash": user},
            headers=auth_headers,
        )

    rows = (await session.execute(select(PipelineRequest))).scalars().all()
    assert {row.user_id_hash for row in rows} == {first, second}

    samples = (await session.execute(select(RawSample))).scalars().all()
    assert len(samples) == 1
