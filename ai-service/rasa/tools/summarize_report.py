#!/usr/bin/env python3
"""In gọn kết quả `rasa test nlu`: độ chính xác và F1 macro của ý định, precision/recall/F1 từng thực thể. Dùng: summarize_report.py results/<thư mục>."""
import json
import pathlib
import sys

d = pathlib.Path(sys.argv[1])
intent = json.loads((d / "intent_report.json").read_text(encoding="utf-8"))
print(f"{d.name}: ý định acc={intent['accuracy']:.3f} macroF1={intent['macro avg']['f1-score']:.3f} (n={int(intent['macro avg']['support'])})")
low = sorted(((v['f1-score'], k) for k, v in intent.items() if isinstance(v, dict) and k not in ('macro avg', 'weighted avg', 'micro avg')))[:3]
print("   ý định yếu nhất:", ", ".join(f"{k}={f:.2f}" for f, k in low))
ent_file = d / "DIETClassifier_report.json"
if ent_file.exists():
    ent = json.loads(ent_file.read_text(encoding="utf-8"))
    for k, v in ent.items():
        if isinstance(v, dict) and k not in ("macro avg", "weighted avg", "micro avg"):
            print(f"   thực thể {k}: P={v['precision']:.2f} R={v['recall']:.2f} F1={v['f1-score']:.2f} (n={int(v['support'])})")
