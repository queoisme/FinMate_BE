"""Đọc lại một dòng chữ bằng VietOCR (seq2seq) chạy qua onnxruntime — có đủ dấu tiếng Việt, không cần PyTorch.

Mô hình: cnn.onnx + encoder.onnx + decoder.onnx trong models/vietocr/. Ảnh dòng cao 32px, giải mã tham lam.
"""
import math
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image

_DIR = Path(__file__).resolve().parent.parent / "models" / "vietocr"  # models/ nằm ở gốc dịch vụ, module này ở reading/
_VOCAB = ('aAàÀảẢãÃáÁạẠăĂằẰẳẲẵẴắẮặẶâÂầẦẩẨẫẪấẤậẬbBcCdDđĐeEèÈẻẺẽẼéÉẹẸêÊềỀểỂễỄếẾệỆfFgGhHiIìÌỉỈĩĨíÍịỊjJkKlLmMnNoOòÒỏỎõÕóÓọỌôÔồỒổỔỗỖốỐộỘơƠờỜởỞỡỠớỚợỢpPqQrRsStTuUùÙủỦũŨúÚụỤưƯừỪửỬữỮứỨựỰvVwWxXyYỳỲỷỶỹỸýÝỵỴzZ'
          '0123456789!"#$%&\'()*+,-./:;<=>?@[\\]^_`{|}~ ')
_GO, _EOS = 1, 2
_I2C = {i + 4: c for i, c in enumerate(_VOCAB)}
_H, _MIN_W, _MAX_W, _MAX_LEN = 32, 32, 512, 128


def available() -> bool:
    return all((_DIR / f"{n}.onnx").exists() for n in ("cnn", "encoder", "decoder"))


@lru_cache(maxsize=1)
def _sessions():
    import onnxruntime as ort

    opts = ort.SessionOptions()
    opts.intra_op_num_threads = 2
    opts.log_severity_level = 3  # bỏ cảnh báo về hình dạng đầu ra 'last' không dùng
    return tuple(ort.InferenceSession(str(_DIR / f"{n}.onnx"), opts, providers=["CPUExecutionProvider"])
                 for n in ("cnn", "encoder", "decoder"))


def _prepare(img: Image.Image) -> np.ndarray:
    w, h = img.size
    new_w = min(max(math.ceil(int(_H * w / h) / 10) * 10, _MIN_W), _MAX_W)
    arr = np.asarray(img.convert("RGB").resize((new_w, _H), Image.LANCZOS), dtype=np.float32) / 255
    return arr.transpose(2, 0, 1)[None]


def recognize(img: Image.Image) -> str:
    cnn, encoder, decoder = _sessions()
    feats = cnn.run(None, {"img": _prepare(img)})[0]
    enc_out, hidden = encoder.run(None, {"src": feats})
    tokens, out = [_GO], []
    for _ in range(_MAX_LEN):
        logits, hidden, _ = decoder.run(None, {"tgt": np.array([tokens[-1]], dtype=np.int64),
                                               "hidden": hidden, "encoder_outputs": enc_out})
        nxt = int(np.argmax(logits[0]))
        if nxt == _EOS:
            break
        tokens.append(nxt)
        out.append(_I2C.get(nxt, ""))
    return "".join(out)
