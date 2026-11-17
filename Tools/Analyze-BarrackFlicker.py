"""Analyze an opt-in BarrackFlickerProbe Game-view run (no third-party packages)."""
import argparse
import csv
import json
import math
from pathlib import Path


def analyze(path):
    rows = list(csv.DictReader(path.open(encoding="utf-8-sig")))
    phases = {}
    for phase in dict.fromkeys(row["phase"] for row in rows):
        selected = [row for row in rows if row["phase"] == phase]
        means = [sum(float(row[key]) for key in ("meanR", "meanG", "meanB")) / 3 for row in selected]
        deltas = sorted(float(row["deltaRgb"]) for row in selected[1:])
        phases[phase] = {
            "frames": len(selected),
            "blackFrames": sum(value < 1e-6 for value in means),
            "meanMin": min(means), "meanMax": max(means),
            "deltaP95": deltas[min(len(deltas)-1, math.ceil(len(deltas)*.95)-1)] if deltas else 0,
            "maxPositionDelta": max(float(row["positionDelta"]) for row in selected),
            "maxRotationDelta": max(float(row["rotationDelta"]) for row in selected),
        }
    frame_ids = [int(row["frame"]) for row in rows]
    consecutive = all(b == a+1 for a, b in zip(frame_ids, frame_ids[1:]))
    accepted = len(rows) == 224 and consecutive and all(
        phases.get(phase, {}).get("blackFrames", -1) == 0 for phase in ("live", "frozen-taa"))
    return {"status": "PASS" if accepted else "FAIL", "frames": len(rows), "consecutive": consecutive, "phases": phases}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1]/"LocalOnly/Barrack/flicker")
    parser.add_argument("--baseline", type=Path)
    args = parser.parse_args()
    result = analyze(args.root/"frames.csv")
    if args.baseline:
        result["baseline"] = analyze(args.baseline)
    (args.root/"validation.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    raise SystemExit(0 if result["status"] == "PASS" else 1)
