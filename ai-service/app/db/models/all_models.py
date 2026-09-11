"""Import gom mọi ORM model.

Dự án không dùng ``__init__.py`` (namespace package) nên alembic và các script cần một
điểm import duy nhất để ``Base.metadata`` biết đủ 11 bảng. Thêm model mới thì thêm vào đây.
"""

from app.db.base import Base
from app.db.models.evaluation import (
    EvaluationCategoryMetric,
    EvaluationPrediction,
    EvaluationRun,
)
from app.db.models.labeled_sample import LabeledSample, SampleSplit
from app.db.models.model_version import ModelVersion, TrainingJob
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.provider_pattern import ProviderPattern
from app.db.models.raw_sample import RawSample
from app.db.models.user_feedback import FeedbackBatchJob, UserFeedback

__all__ = [
    "Base",
    "EvaluationCategoryMetric",
    "EvaluationPrediction",
    "EvaluationRun",
    "FeedbackBatchJob",
    "LabeledSample",
    "ModelVersion",
    "PipelineRequest",
    "ProviderPattern",
    "RawSample",
    "SampleSplit",
    "TrainingJob",
    "UserFeedback",
]
