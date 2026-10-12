"""STT service: audio -> text (GIPFormer ONNX, CPU). Audio chỉ đi qua bộ nhớ, không lưu, không log nội dung."""
import asyncio
import hmac
import os
import time

from fastapi import Depends, FastAPI, File, Header, HTTPException, UploadFile

import upload_limits
from runtime import EngineSlot
from speech import GipformerEngine

MAX_AUDIO_BYTES = 10 * 1024 * 1024
AUDIO_TYPES = {"audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/aac", "audio/ogg", "audio/webm", "audio/flac"}

app = FastAPI(title="FinMate STT service")
app.add_middleware(upload_limits.RecognitionUploadLimits)
slot = EngineSlot(lambda: GipformerEngine(os.getenv("ASR_MODEL_DIR", "/models/gipformer1.5-fp32"),
                                          int(os.getenv("STT_THREADS", "2"))))


def verify_internal_api_key(authorization: str | None = Header(None)) -> None:
    """`Authorization: Bearer {STT_SERVICE_API_KEY}`. Chưa đặt khóa thì từ chối hết, không mở."""
    key = os.getenv("STT_SERVICE_API_KEY", "")
    token = authorization.removeprefix("Bearer ").strip() if authorization and authorization.startswith("Bearer ") else ""
    if not key or not hmac.compare_digest(token, key):
        raise HTTPException(401, "Unauthorized")


@app.get("/health")
async def health():
    return {"status": "ok"}


@app.post("/transcribe", dependencies=[Depends(verify_internal_api_key)])
async def transcribe(file: UploadFile = File(...)):
    if file.content_type not in AUDIO_TYPES:
        raise HTTPException(415, "Unsupported media type")
    data = await file.read(MAX_AUDIO_BYTES + 1)
    if not data or len(data) > MAX_AUDIO_BYTES:
        raise HTTPException(413 if data else 422, "Invalid upload size")
    started = time.perf_counter()
    try:
        result = await slot.run(data)
    except (TimeoutError, asyncio.TimeoutError) as error:
        raise HTTPException(429, "STT busy", headers={"Retry-After": "2"}) from error
    except ValueError as error:
        raise HTTPException(422, "Invalid audio or duration limit exceeded") from error
    except Exception as error:  # không đưa nội dung lỗi/đường dẫn ra ngoài
        raise HTTPException(503, "STT engine unavailable") from error
    result["text"] = result["text"].lower()
    result["processing_ms"] = int((time.perf_counter() - started) * 1000)
    return result
