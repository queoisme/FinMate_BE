"""Reject oversized recognition bodies before multipart parsing; prevent disk spooling."""
from starlette.formparsers import MultiPartParser

MAX_BODY_BYTES = 10 * 1024 * 1024 + 4096
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
