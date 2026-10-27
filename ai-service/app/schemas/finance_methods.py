"""Schema của ``POST /api/v1/finance-methods/recommend`` (hợp đồng: ``docs/ai-finance-method-recommend-api.md``).

``extra = forbid`` có chủ ý: request này chỉ có signals ẩn danh và danh sách phương pháp, nên ai đó thêm ``user_id`` vào thì hỏng ngay (422)
thay vì lặng lẽ đi sang bên thứ ba.
"""

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, StrictInt, model_validator

_CODE = r"^[a-z][a-z0-9_]{0,47}$"


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


class SignalIn(_Strict):
    key: str = Field(pattern=_CODE)
    weight: StrictInt = Field(ge=1, le=50)


class MethodIn(_Strict):
    code: str = Field(pattern=_CODE)
    name: str = Field(min_length=1, max_length=100)
    summary: str = Field(max_length=400)
    role: Literal["primary", "supporting", "both"]  # đúng FinanceMethodRole của backend


class RecommendRequest(_Strict):
    signals: list[SignalIn] = Field(min_length=1, max_length=60)
    methods: list[MethodIn] = Field(min_length=1, max_length=20)

    @model_validator(mode="after")
    def _unique(self) -> "RecommendRequest":
        if len({s.key for s in self.signals}) != len(self.signals):
            raise ValueError("signals trùng key")
        if len({m.code for m in self.methods}) != len(self.methods):
            raise ValueError("methods trùng code")
        return self


class Pick(BaseModel):
    code: str
    reason: str


class RecommendResponse(BaseModel):
    primary: Pick
    support: Pick | None
    prompt_version: str
    processing_ms: int
