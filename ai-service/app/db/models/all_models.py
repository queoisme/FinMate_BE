"""Import gom mọi ORM model.

Dự án không dùng ``__init__.py`` (namespace package) nên alembic và các script cần một
điểm import duy nhất để ``Base.metadata`` biết đủ bảng (bộ ``kb_``/``ml_``/``ops_`` + ``nlu_`` + vài bảng ``bill_*`` còn lại). Thêm model mới thì thêm vào đây.
"""

from app.db.base import Base
from app.db.models.bill import (
    BillAiInstruction,
    BillBrand,
    BillKeyword,
    BillRuleAudit,
    BillClassifyStat,
    BillTrainingSample,
    BillUnknownScan,
    BillItemCache,
    BillUserMerchantChoice,
)
from app.db.models.category import KbCategory
from app.db.models.config_document import ConfigDocument
from app.db.models.evaluation import (
    EvaluationCategoryMetric,
    EvaluationPrediction,
    EvaluationRun,
)
from app.db.models.labeled_sample import LabeledSample, SampleSplit
from app.db.models.ml_item_feedback import MlItemFeedback
from app.db.models.ml_label_event import MlLabelEvent
from app.db.models.method_prompt import MethodPrompt
from app.db.models.ml_model import MlModel
from app.db.models.model_version import ModelVersion, TrainingJob
from app.db.models.nlu_candidate import NluCandidate
from app.db.models.nlu_example import NluExample
from app.db.models.nlu_model_version import NluModelVersion
from app.db.models.pipeline_request import PipelineRequest
from app.db.models.provider_format import ProviderFormat, ProviderFormatField, ProviderFormatSample
from app.db.models.provider_pattern import ProviderPattern
from app.db.models.raw_sample import RawSample
from app.db.models.user_feedback import FeedbackBatchJob, UserFeedback

__all__ = [
    "Base",
    "BillAiInstruction",
    "BillBrand",
    "BillKeyword",
    "BillRuleAudit",
    "BillUnknownScan",
    "BillItemCache",
    "BillUserMerchantChoice",
    "ConfigDocument",
    "EvaluationCategoryMetric",
    "EvaluationPrediction",
    "EvaluationRun",
    "FeedbackBatchJob",
    "KbCategory",
    "LabeledSample",
    "MethodPrompt",
    "MlItemFeedback",
    "MlLabelEvent",
    "MlModel",
    "ModelVersion",
    "NluCandidate",
    "NluExample",
    "NluModelVersion",
    "PipelineRequest",
    "ProviderFormat",
    "ProviderFormatField",
    "ProviderFormatSample",
    "ProviderPattern",
    "RawSample",
    "SampleSplit",
    "TrainingJob",
    "UserFeedback",
]
