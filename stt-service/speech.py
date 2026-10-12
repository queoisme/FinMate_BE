"""Decode bounded audio in memory and run the local GIPFormer transducer."""
from io import BytesIO
from pathlib import Path

MAX_SECONDS = 60
SAMPLE_RATE = 16000


def decode_audio(data: bytes):
    import av
    import numpy as np

    chunks = []
    length = 0
    try:
        with av.open(BytesIO(data)) as container:
            resampler = av.AudioResampler(format="fltp", layout="mono", rate=SAMPLE_RATE)
            for frame in container.decode(audio=0):
                for output in resampler.resample(frame):
                    length += output.samples
                    if length > SAMPLE_RATE * MAX_SECONDS:
                        raise ValueError("audio_duration_limit")
                    chunks.append(output.to_ndarray().reshape(-1))
            for output in resampler.resample(None):
                length += output.samples
                if length > SAMPLE_RATE * MAX_SECONDS:
                    raise ValueError("audio_duration_limit")
                chunks.append(output.to_ndarray().reshape(-1))
    except (av.error.FFmpegError, IndexError) as error:
        raise ValueError("invalid_audio") from error
    if not chunks:
        raise ValueError("empty_audio")
    return np.concatenate(chunks).astype(np.float32, copy=False)


class GipformerEngine:
    def __init__(self, directory: str, threads: int):
        import sherpa_onnx

        root = Path(directory)
        paths = {name: root / f"{name}.onnx" for name in ("encoder", "decoder", "joiner")}
        paths["tokens"] = root / "tokens.txt"
        if not all(path.is_file() for path in paths.values()):
            raise RuntimeError("asr_assets_missing")
        self.recognizer = sherpa_onnx.OfflineRecognizer.from_transducer(
            **{name: str(path) for name, path in paths.items()},
            num_threads=threads, sample_rate=SAMPLE_RATE, feature_dim=80,
            decoding_method="modified_beam_search",
        )

    def recognize(self, data: bytes) -> dict:
        import numpy as np

        samples = decode_audio(data)
        duration_ms = len(samples) * 1000 // SAMPLE_RATE
        # Silence gate only; this is not a calibrated speech-confidence score or VAD.
        if float(np.sqrt(np.mean(samples * samples))) < 0.0001:
            text = ""
        else:
            stream = self.recognizer.create_stream()
            stream.accept_waveform(SAMPLE_RATE, samples)
            self.recognizer.decode_streams([stream])
            text = stream.result.text.strip()
        return {"text": text, "duration_ms": duration_ms, "engine": "gipformer1.5-68M-rnnt-fp32"}
