"""Hợp đồng giữa backend và stt-service: hai nơi cùng giới hạn dung lượng và loại âm thanh nhận vào (VoiceChatCommandHandler.cs và upload_limits.py).

Hai ngôn ngữ nên không dùng chung được hằng số; test này đọc tệp C# và so với bản Python để sửa một bên mà quên bên kia thì đỏ ngay."""
import pathlib
import re

import pytest

import upload_limits

HANDLER = pathlib.Path(__file__).resolve().parents[2] / "backend" / "FinMate.Application" / "Transactions" / "Commands" / "VoiceChatCommandHandler.cs"
pytestmark = pytest.mark.skipif(not HANDLER.is_file(), reason="chạy riêng stt-service (không có thư mục backend cạnh bên)")


def _source() -> str:
    return HANDLER.read_text(encoding="utf-8")


def test_the_size_limit_matches_the_backend():
    m = re.search(r"MaxAudioBytes\s*=\s*([\d\s*]+);", _source())
    assert m, "không thấy MaxAudioBytes trong VoiceChatCommandHandler.cs"
    size = 1
    for factor in m.group(1).split("*"):
        size *= int(factor)
    assert size == upload_limits.MAX_AUDIO_BYTES


def test_the_accepted_audio_types_match_the_backend():
    m = re.search(r"AudioTypes\s*=\s*\[(.*?)\];", _source(), re.S)
    assert m, "không thấy AudioTypes trong VoiceChatCommandHandler.cs"
    assert set(re.findall(r'"([^"]+)"', m.group(1))) == set(upload_limits.AUDIO_TYPES)
