"""GET /api/v1/health — dùng cho docker healthcheck, không cần auth."""

from fastapi import APIRouter

from app.core.enums import ModelStage
from app.pipeline.models.model_registry import registry

router = APIRouter()


@router.get("/health")
async def health() -> dict[str, object]:
    """Không chạm DB: healthcheck phải trả lời được cả khi DB chập chờn, nếu không
    orchestrator sẽ giết container đang hoạt động bình thường ở nhánh rule.

    ``models`` cho biết tiến trình NÀY đang nạp model version nào, không phải model nào
    đang active trong DB — registry chỉ nạp lại ở đường ``/analyze`` (tối đa 60 giây một
    lần). Một tiến trình vừa khởi động và chưa nhận request nào sẽ báo null dù đã có model
    được promote; giá trị đổi sang version mới sau request đầu tiên.

    null cũng là trạng thái hợp lệ khi chưa promote model nào — pipeline chạy bằng luật.
    """
    return {
        "status": "healthy",
        "models": {stage.value: registry.version(stage) for stage in ModelStage},
    }
