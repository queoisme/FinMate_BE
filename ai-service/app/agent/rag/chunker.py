"""Cắt tài liệu thành các đoạn để nhúng và truy xuất. Ranh giới đoạn LUÔN rơi vào ranh giới câu (một câu bị cắt đôi mất nghĩa, và đoạn trích dẫn cho người dùng đọc phải trọn câu).

Mỗi đoạn ≤ ``MAX_CHARS`` (nhỏ hơn giới hạn 500 ký tự của ``Evidence.text``). Câu dài hơn giới hạn là ngoại lệ duy nhất: bị cắt ở khoảng trắng gần nhất."""

from __future__ import annotations

import re

MAX_CHARS = 450
_SENTENCE_END = re.compile(r"(?<=[.!?…])\s+")


def _sentences(text: str) -> list[str]:
    out: list[str] = []
    for paragraph in re.split(r"\n\s*\n|\n", text):
        out.extend(s.strip() for s in _SENTENCE_END.split(paragraph.strip()) if s.strip())
    return out


def _split_long(sentence: str, limit: int) -> list[str]:
    parts: list[str] = []
    while len(sentence) > limit:
        cut = sentence.rfind(" ", 0, limit)
        cut = cut if cut > 0 else limit
        parts.append(sentence[:cut].strip())
        sentence = sentence[cut:].strip()
    return [*parts, sentence] if sentence else parts


def chunk(text: str, max_chars: int = MAX_CHARS) -> list[str]:
    chunks: list[str] = []
    current = ""
    for sentence in _sentences(text):
        for piece in _split_long(sentence, max_chars):
            if current and len(current) + 1 + len(piece) > max_chars:
                chunks.append(current)
                current = piece
            else:
                current = f"{current} {piece}".strip()
    if current:
        chunks.append(current)
    return chunks
