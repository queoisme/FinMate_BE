"""Nạp model đang ``active`` từ ``model_versions`` + ``MODEL_REGISTRY_PATH``.

Nguồn sự thật là DB, không phải thư mục: chỉ dòng ``model_versions`` có
``status='active'`` mới được phục vụ, và partial unique index bảo đảm mỗi stage chỉ có
một dòng như vậy. File trên đĩa mà không có dòng active tương ứng sẽ bị bỏ qua — đó là
điều phân biệt "đã train" với "đã promote".

Không có model nào active thì ``get()`` trả None và stage tự rơi về nhánh rule. Đây là
trạng thái BÌNH THƯỜNG của một lần triển khai mới, không phải lỗi.
"""

from __future__ import annotations

import time
from pathlib import Path

import joblib
import structlog
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import settings
from app.core.enums import ModelStage
from app.db.repositories import model_repo
from app.pipeline.models.base_model import SklearnStageModel, StageModel

logger = structlog.get_logger(__name__)

# Nạp lại tối đa mỗi 60 giây: promote một model không cần restart service, nhưng cũng
# không đáng một truy vấn DB trên mỗi request.
RELOAD_INTERVAL_SECONDS = 60.0


class ModelRegistry:
    def __init__(self, root: str | Path | None = None) -> None:
        self._root = Path(root or settings.model_registry_path)
        self._models: dict[ModelStage, StageModel] = {}
        self._loaded_version_ids: dict[ModelStage, str] = {}
        self._checked_at: float = 0.0

    async def ensure_loaded(self, session: AsyncSession, force: bool = False) -> None:
        now = time.monotonic()
        if not force and now - self._checked_at < RELOAD_INTERVAL_SECONDS:
            return
        self._checked_at = now

        active = await model_repo.list_active(session)
        active_by_stage = {ModelStage(row.stage): row for row in active}

        for stage in ModelStage:
            row = active_by_stage.get(stage)
            if row is None:
                self._models.pop(stage, None)
                self._loaded_version_ids.pop(stage, None)
                continue
            if self._loaded_version_ids.get(stage) == str(row.id):
                continue
            model = self._load_artifact(stage, row.version, row.artifact_path)
            if model is None:
                continue
            self._models[stage] = model
            self._loaded_version_ids[stage] = str(row.id)

    def _load_artifact(
        self, stage: ModelStage, version: str, artifact_path: str
    ) -> StageModel | None:
        path = self._root / artifact_path
        if not path.exists():
            # Dòng active trỏ tới file không tồn tại: đây là lỗi triển khai thật (quên
            # mount volume model). Log rồi chạy tiếp bằng rule — trả 500 cho mọi thông báo
            # còn tệ hơn là phân loại kém.
            logger.error(
                "model_artifact_missing",
                stage=stage.value,
                version=version,
                path=str(path),
            )
            return None
        # Artifact hỏng không được phép làm sập service — rơi về luật là đủ.
        try:
            pipeline = joblib.load(path)
        except Exception as error:  # noqa: BLE001
            logger.error(
                "model_artifact_unreadable",
                stage=stage.value,
                version=version,
                error=str(error),
            )
            return None
        logger.info("model_loaded", stage=stage.value, version=version)
        return SklearnStageModel(version=version, pipeline=pipeline)

    def reset(self) -> None:
        """Quên mọi model đã nạp và buộc lần refresh sau đọc lại DB.

        Registry là singleton dùng chung cả tiến trình, nên test (hoặc một lần chuyển
        database) phải có cách xoá trạng thái cũ thay vì chọc vào thuộc tính private.
        """
        self._models.clear()
        self._loaded_version_ids.clear()
        self._checked_at = 0.0

    def get(self, stage: ModelStage) -> StageModel | None:
        return self._models.get(stage)

    def version(self, stage: ModelStage) -> str | None:
        model = self._models.get(stage)
        return model.version if model else None

    def artifact_dir(self, stage: ModelStage, version: str) -> Path:
        return self._root / stage.value / version


registry = ModelRegistry()
