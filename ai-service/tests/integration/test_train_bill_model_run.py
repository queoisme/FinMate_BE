"""``run_train`` đầu-cuối trên Postgres thật: đọc mẫu có nhãn của người dùng, huấn luyện, ghi phiên bản; không bao giờ tự bật."""

import random
import sys
import uuid
from pathlib import Path

from sqlalchemy import text

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts"))

from train_bill_model import run_train  # noqa: E402

SLUGS = ["food", "transport", "health", "shopping"]


def shop(i: int) -> str:
    return "".join(chr(97 + (i // 26**k) % 26) for k in range(3)) + " store"


async def add_labeled(session, count_per_slug: int, merchants: int) -> None:
    """Từ vô nghĩa ``zq<nhóm><k>``: luật hiện tại không biết nên mô hình có chỗ để hơn."""
    rng = random.Random(0)
    for s, slug in enumerate(SLUGS):
        for i in range(count_per_slug):
            words = [f"zq{chr(97 + s)}{chr(97 + rng.randrange(6))}" for _ in range(3)]
            await session.execute(
                text(
                    "INSERT INTO ml_training_samples (scan_id, user_id_hash, merchant_key, lines, final_slug, labeled_at) "
                    "VALUES (:id, :user, :key, :lines, :slug, now())"
                ),
                {"id": uuid.uuid4(), "user": "a" * 64, "key": shop(s * 12 + i % merchants), "lines": "\n".join(words), "slug": slug},
            )
    await session.commit()


async def versions(session) -> list[tuple[str, str]]:
    return [(r.status, r.notes) for r in (await session.execute(text("SELECT v.status, v.notes FROM ml_model_versions v JOIN ml_models m ON m.id = v.model_id WHERE m.code = 'bill_slug'"))).all()]


async def test_run_train_records_a_candidate_and_never_activates(session, tmp_path):
    await add_labeled(session, count_per_slug=100, merchants=12)
    version, result = await run_train(session, str(tmp_path))
    assert result.status == "candidate", result.reason
    assert [s for s, _ in await versions(session)] == ["candidate"]
    assert (tmp_path / "bill_slug" / f"{version}.joblib").exists()


async def test_run_train_with_no_samples_records_a_rejection(session, tmp_path):
    version, result = await run_train(session, str(tmp_path))
    assert result.status == "rejected"
    [(status, notes)] = await versions(session)
    assert status == "rejected" and "Chưa đủ dữ liệu" in notes
