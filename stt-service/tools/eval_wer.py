"""Đo tỷ lệ lỗi từ (WER) và tốc độ của engine STT thật trên một thư mục file ghi âm, in một dòng JSON.

Chạy trong container stt-service (có model và `speech.py`), thư mục ghi âm chép vào /tmp/voice:

    docker compose cp stt-service/Data_Voice stt-service:/tmp/voice
    docker compose exec -T stt-service python - < stt-service/tools/eval_wer.py

Lời tham chiếu: nếu có `references.tsv` trong thư mục (mỗi dòng `tên_file<TAB>lời nói đầy đủ`) thì dùng nó. Không có thì suy ra từ tên file
(`<lời_nói_không_dấu>_<uuid>.mp3`, bị cắt ở 48 ký tự), nên chỉ chấm PHẦN ĐẦU câu: WER so với tiền tố gần nhất của kết quả nhận dạng,
bỏ từ cuối của tên bị cắt. Số đo này chỉ là cận trên của lỗi thật; có `references.tsv` đầy đủ mới đo được cả câu.
"""

import json
import re
import sys
import time
import unicodedata
from pathlib import Path

sys.path.insert(0, "/app")
from speech import SAMPLE_RATE, GipformerEngine, decode_audio  # noqa: E402

VOICE = Path("/tmp/voice")
CUT_AT = 44  # tên file dài từ ngần này ký tự (trước _uuid) là đã bị cắt giữa từ: bỏ từ cuối


def words(text: str) -> list[str]:
    plain = unicodedata.normalize("NFD", text.lower().replace("đ", "d"))
    plain = "".join(c for c in plain if unicodedata.category(c) != "Mn")
    return re.sub(r"[^a-z0-9]+", " ", plain).split()


def edit_distance(ref: list[str], hyp: list[str], open_end: bool) -> int:
    """Levenshtein theo từ; open_end = hyp được phép dài hơn ref (chỉ tính phần đầu của hyp)."""
    row = list(range(len(hyp) + 1))
    for i, r in enumerate(ref, 1):
        new = [i]
        for j, h in enumerate(hyp, 1):
            new.append(min(row[j] + 1, new[j - 1] + 1, row[j - 1] + (r != h)))
        row = new
    return min(row) if open_end else row[-1]


def references() -> dict[str, tuple[list[str], bool]]:
    """tên file -> (từ tham chiếu, chỉ là tiền tố)"""
    table = VOICE / "references.tsv"
    if table.is_file():
        rows = (line.split("\t", 1) for line in table.read_text(encoding="utf-8").splitlines() if "\t" in line)
        return {name: (words(text), False) for name, text in rows}
    out = {}
    for path in VOICE.glob("*.mp3"):
        stem = re.sub(r"_[0-9a-f-]{36}$", "", path.stem)
        ref = stem.split("_")
        # Tên bị cắt theo ký tự lẫn theo từ, không biết file nào còn nguyên: coi mọi tham chiếu suy ra từ tên là tiền tố.
        out[path.name] = (ref[:-1] if len(stem) >= CUT_AT else ref, True)
    return out


if __name__ == "__main__":
    engine = GipformerEngine("/models/gipformer1.5-fp32", 2)
    clips, errors, ref_words, exact = [], 0, 0, 0
    for name, (ref, prefix_only) in sorted(references().items()):
        data = (VOICE / name).read_bytes()
        audio_s = len(decode_audio(data)) / SAMPLE_RATE
        started = time.perf_counter()
        hyp = words(engine.recognize(data)["text"])
        proc_s = time.perf_counter() - started
        distance = edit_distance(ref, hyp, open_end=prefix_only)
        errors += distance
        ref_words += len(ref)
        exact += distance == 0
        clips.append({"ref": " ".join(ref), "hyp": " ".join(hyp), "wer": round(distance / len(ref), 3),
                      "audio_s": round(audio_s, 1), "proc_s": round(proc_s, 2), "prefix_only": prefix_only})
    sys.stdout.write(json.dumps({
        "n": len(clips),
        "wer": round(errors / ref_words, 3),
        "exact_rate": round(exact / len(clips), 3),
        "rtf": round(sum(c["proc_s"] for c in clips) / sum(c["audio_s"] for c in clips), 3),
        "prefix_only": all(c["prefix_only"] for c in clips),
        "worst": sorted(clips, key=lambda c: -c["wer"])[:5],
    }, ensure_ascii=False))
