"""Reject oversized recognition bodies before multipart parsing; prevent disk spooling."""
from starlette.formparsers import MultiPartParser

# Giới hạn nhận âm thanh: MỘT nơi định nghĩa trong stt-service. Backend kiểm tra cùng hai giá trị này trước khi gọi sang
# (VoiceChatCommandHandler.MaxAudioBytes / AudioTypes); tests/test_backend_contract.py canh cho hai bên không lệch.
MAX_AUDIO_BYTES = 10 * 1024 * 1024
AUDIO_TYPES = frozenset({"audio/wav", "audio/x-wav", "audio/mpeg", "audio/mp4", "audio/aac", "audio/ogg", "audio/webm", "audio/flac"})
MAX_BODY_BYTES = MAX_AUDIO_BYTES + 4096  # chừa phần đầu multipart
# Starlette renamed max_file_size to spool_max_size in later releases. Cover both.
MultiPartParser.max_file_size = MAX_BODY_BYTES
MultiPartParser.spool_max_size = MAX_BODY_BYTES


class RecognitionUploadLimits:
    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http" or not scope["path"].startswith("/transcribe"):
            return await self.app(scope, receive, send)
        body = bytearray()
        while True:
            message = await receive()
            if message["type"] == "http.disconnect":
                return
            chunk = message.get("body", b"")
            if len(body) + len(chunk) > MAX_BODY_BYTES:
                await send({"type": "http.response.start", "status": 413,
                            "headers": [(b"content-type", b"application/json")]})
                await send({"type": "http.response.body", "body": b'{"detail":"Upload too large"}'})
                return
            body.extend(chunk)
            if not message.get("more_body", False):
                break
        delivered = False

        async def replay():
            nonlocal delivered
            if not delivered:
                delivered = True
                return {"type": "http.request", "body": bytes(body), "more_body": False}
            return await receive()

        await self.app(scope, replay, send)
